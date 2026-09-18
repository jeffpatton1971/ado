# Synthetic harness test: no Azure DevOps traffic or real credentials.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$directory = [IO.Directory]::CreateTempSubdirectory('ado-live-harness-').FullName
$oldToken = $env:ADO_TOKEN
$oldFixtures = $env:ADO_LIVE_FIXTURES
$oldFailure = $env:SYNTHETIC_LIVE_FAILURE
try {
    Remove-Item Env:SYNTHETIC_LIVE_FAILURE -ErrorAction SilentlyContinue
    $env:ADO_TOKEN = 'synthetic-live-token'
    $fixture = @{
        organization = 'example'; project = 'Demo'; buildId = 34; logId = 21; sourceSha = ('a' * 40)
        scope = 'organization'; feed = 'demo'; packageName = 'Example'; packageVersion = '1.0.0'; packageSha256 = ('c' * 64)
        artifacts = @(
            @{ buildId = 35; name = 'drop'; resourceType = 'Container'; entry = 'plugin.json'; memberSha256 = ('a' * 64) },
            @{ buildId = 36; name = 'output'; resourceType = 'PipelineArtifact'; entry = 'plugin.json'; memberSha256 = ('a' * 64) }
        )
    }
    $env:ADO_LIVE_FIXTURES = $fixture | ConvertTo-Json -Depth 6 -Compress
    $fake = Join-Path $directory 'fake-ado.ps1'
    @'
$ErrorActionPreference = 'Stop'
$arguments = @($args)
function Option($name) { $at = [Array]::IndexOf($arguments, $name); if ($at -lt 0) { return $null }; return $arguments[$at + 1] }
foreach ($required in @('--read-only', '--non-interactive', '--json', '--config')) { if ($required -notin $arguments) { exit 2 } }
if ($env:ADO_TOKEN -cne 'synthetic-live-token') { exit 4 }
if ($env:ADO_LIVE_FIXTURES) { exit 9 } # Parent's other ADO settings must not leak into the child.
if (-not (Test-Path -LiteralPath (Option '--config'))) { exit 3 }
if ($env:SYNTHETIC_LIVE_FAILURE -eq 'true') { [Console]::Error.WriteLine('private-payload-sentinel'); Write-Output 'private-payload-sentinel'; exit 9 }
$meta = @{ schemaVersion = 1; completeness = 'complete'; truncated = $false; continuationToken = $null }
$command = $arguments[0] + ' ' + $arguments[1]
switch ($command) {
    'auth check' { $data = @{ accessConfirmed = $true } }
    'build get' { $data = @{ id = 34; sourceVersion = ('a' * 40) } }
    'build diagnose' { $data = @{ build = @{ id = 34 }; findings = @(@{ category = 'failed_task'; record = @{ logId = 21 } }) } }
    'build log' { $data = @{ buildId = 34; logId = 21; lines = @('private-payload-sentinel') } }
    'build artifact' {
        if ($arguments[2] -eq 'get') { $data = @{ name = (Option '--artifact-name'); resourceType = $(if ((Option '--build-id') -eq '35') { 'Container' } else { 'PipelineArtifact' }) } }
        elseif ($arguments[2] -eq 'download') { [IO.File]::WriteAllText((Option '--destination'), 'synthetic'); $data = @{ bytes = 9 } }
        else { exit 2 }
    }
    'artifact inspect' { $data = @{ entries = @(@{ sha256 = ('a' * 64) }) } }
    'package list' {
        $offset = Option '--continuation-token'
        $data = @(@{ id = $(if ($offset) { 'second' } else { 'first' }) })
        $meta.completeness = 'partial'; $meta.truncated = $true; $meta.continuationToken = $(if ($offset) { '2' } else { '1' })
    }
    'package resolve' { $data = @{ package = @{ name = 'Example' }; version = @{ version = '1.0.0' } } }
    'package download' { [IO.File]::WriteAllText((Option '--destination'), 'synthetic'); $data = @{ sha256 = ('c' * 64) } }
    'package inspect' { $data = @{ id = 'Example'; version = '1.0.0' } }
    default { exit 2 }
}
@{ ok = $true; data = $data; meta = $meta } | ConvertTo-Json -Depth 8 -Compress
'@ | Set-Content -LiteralPath $fake
    $script = Join-Path $PSScriptRoot 'Test-LiveIntegration.ps1'
    $pwsh = (Get-Command pwsh).Source
    $before = @(Get-ChildItem ([IO.Path]::GetTempPath()) -Directory -Filter 'ado-live-*' | Select-Object -ExpandProperty FullName)
    $output = & $script -ToolPath $pwsh -ToolArguments @('-NoProfile', '-File', $fake)
    if (($output -join "`n") -match 'private-payload-sentinel|synthetic-live-token' -or @($output).Count -ne 3) { throw 'Live harness output contract failed.' }
    # Failure output must be discarded, and cleanup must run on failure as well.
    $env:SYNTHETIC_LIVE_FAILURE = 'true'
    $failed = $false
    try { & $script -ToolPath $pwsh -ToolArguments @('-NoProfile', '-File', $fake) | Out-Null }
    catch { $failed = $true; if ($_ | Out-String | Select-String 'private-payload-sentinel|synthetic-live-token') { throw 'Live harness leaked output.' } }
    if (-not $failed) { throw 'Live harness accepted a failing command.' }
    $env:ADO_LIVE_FIXTURES = '{}'
    $invalid = $false
    try { & $script -ValidateOnly | Out-Null } catch { $invalid = $true }
    if (-not $invalid) { throw 'Live harness accepted missing fixtures.' }
    $after = @(Get-ChildItem ([IO.Path]::GetTempPath()) -Directory -Filter 'ado-live-*' | Select-Object -ExpandProperty FullName)
    if (@($after | Where-Object { $_ -notin $before }).Count -ne 0) { throw 'Live harness failed to clean its temporary files.' }
    Write-Output 'Synthetic live harness checks passed (success, failure redaction, fixture validation and cleanup).'
} finally {
    $env:ADO_TOKEN = $oldToken
    $env:ADO_LIVE_FIXTURES = $oldFixtures
    $env:SYNTHETIC_LIVE_FAILURE = $oldFailure
    $resolved = [IO.Path]::GetFullPath($directory)
    $parent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $resolved.StartsWith($parent, [StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notlike 'ado-live-harness-*') { throw 'Unexpected harness cleanup path.' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
$global:LASTEXITCODE = 0
