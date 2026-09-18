# Requires PowerShell 7. Executes only fixed read-only CLI operations; never prints service payloads.
[CmdletBinding()]
param([string]$ToolPath, [string[]]$ToolArguments = @(), [switch]$ValidateOnly)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Require([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}
try {
    $fixture = $env:ADO_LIVE_FIXTURES | ConvertFrom-Json -AsHashtable
    Require ($fixture -is [System.Collections.IDictionary]) 'Missing fixture object.'
    foreach ($field in @('organization', 'project', 'sourceSha', 'feed', 'packageName', 'packageVersion')) {
        Require ($fixture[$field] -is [string] -and -not [string]::IsNullOrWhiteSpace($fixture[$field])) 'Missing fixture string.'
    }
    Require ($fixture.sourceSha -match '^[a-fA-F0-9]{40}$') 'Invalid fixture SHA.'
    Require ($fixture.scope -in @('organization', 'project')) 'Invalid feed scope.'
    foreach ($field in @('buildId', 'logId')) {
        Require ($fixture[$field] -is [long] -or $fixture[$field] -is [int]) 'Invalid fixture ID.'
        Require ($fixture[$field] -gt 0 -and $fixture[$field] -le [int]::MaxValue) 'Invalid fixture ID.'
    }
    Require ($fixture.artifacts.Count -eq 2) 'Supply two artifact fixtures.'
    Require ((@($fixture.artifacts.resourceType | Sort-Object -Unique) -join ',') -ceq 'Container,PipelineArtifact') 'Supply both artifact types.'
    foreach ($artifact in $fixture.artifacts) {
        Require ($artifact.buildId -is [long] -or $artifact.buildId -is [int]) 'Invalid artifact build ID.'
        Require ($artifact.buildId -gt 0 -and $artifact.buildId -le [int]::MaxValue) 'Invalid artifact build ID.'
        foreach ($field in @('name', 'entry')) { Require (-not [string]::IsNullOrWhiteSpace($artifact[$field])) 'Missing artifact selector.' }
        Require ($artifact.memberSha256 -match '^[a-fA-F0-9]{64}$') 'Invalid artifact digest.'
    }
    Require ($fixture.packageSha256 -match '^[a-fA-F0-9]{64}$') 'Invalid package digest.'
} catch { throw 'Invalid ADO_LIVE_FIXTURES. See docs/live-integration.md; fixture values are not logged.' }
if ($ValidateOnly) { Write-Output 'Live fixture configuration is valid; no credentials or network accessed.'; return }
Require (-not [string]::IsNullOrWhiteSpace($env:ADO_TOKEN)) 'ADO_PAT is missing; live verification did not run.'
Require (Test-Path -LiteralPath $ToolPath -PathType Leaf) 'Installed ado executable is missing.'
$scratch = [IO.Directory]::CreateTempSubdirectory('ado-live-').FullName
$config = Join-Path $scratch 'config.json'
$configuration = @{ schemaVersion = 1; defaultProfile = 'live'; profiles = @{ live = @{
    organization = $fixture.organization; project = $fixture.project
    authentication = @{ type = 'pat'; provider = 'environment' }
} } }

function Invoke-Read([string]$Label, [string[]]$Arguments, [switch]$PartialAllowed) {
    $info = [Diagnostics.ProcessStartInfo]::new($ToolPath)
    $info.UseShellExecute = $false
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    foreach ($arg in $ToolArguments) { $info.ArgumentList.Add($arg) }
    foreach ($key in @($info.Environment.Keys)) { if ($key -like 'ADO_*') { $info.Environment.Remove($key) | Out-Null } }
    $info.Environment['ADO_TOKEN'] = $env:ADO_TOKEN
    foreach ($arg in @($Arguments) + @('--config', $config, '--read-only', '--non-interactive', '--json')) { $info.ArgumentList.Add($arg) }
    if (-not $PartialAllowed) { $info.ArgumentList.Add('--require-complete') }
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $info
    try {
        $process.Start() | Out-Null
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(120000)) {
            $process.Kill($true)
            throw "$Label timed out."
        }
        $raw = $stdout.GetAwaiter().GetResult()
        $null = $stderr.GetAwaiter().GetResult()
        Require ($process.ExitCode -eq 0) "$Label failed (exit $($process.ExitCode)); response omitted. Check access, fixture retention and token expiry."
        try { $result = $raw | ConvertFrom-Json } catch { throw "$Label returned invalid JSON; response omitted." }
        Require ($result.ok -eq $true -and $result.meta.schemaVersion -eq 1) "$Label returned an invalid envelope."
        if (-not $PartialAllowed) { Require ($result.meta.completeness -ceq 'complete' -and -not $result.meta.truncated) "$Label was incomplete." }
        return $result
    } finally { $process.Dispose() }
}

try {
    [IO.File]::WriteAllText($config, ($configuration | ConvertTo-Json -Depth 6))
    $buildArgs = @('--build-id', [string]$fixture.buildId)
    $auth = Invoke-Read 'Authentication' (@('auth', 'check', '--capability', 'build') + $buildArgs)
    Require ($auth.data.accessConfirmed -eq $true) 'Authentication probe did not confirm access.'
    $build = Invoke-Read 'Build identity' (@('build', 'get') + $buildArgs)
    Require ($build.data.id -eq $fixture.buildId -and $build.data.sourceVersion -ieq $fixture.sourceSha) 'Build fixture identity changed.'
    $diagnosis = Invoke-Read 'Diagnosis' (@('build', 'diagnose', '--include-history', '--limit', '1000') + $buildArgs)
    Require ($diagnosis.data.build.id -eq $fixture.buildId) 'Diagnosis build mismatch.'
    Require (@($diagnosis.data.findings | Where-Object { $_.category -ceq 'failed_task' -and $_.record.logId -eq $fixture.logId }).Count -gt 0) 'Expected failed task/log reference missing.'
    $log = Invoke-Read 'Targeted log' (@('build', 'log', 'get', '--log-id', [string]$fixture.logId, '--limit', '1000') + $buildArgs)
    Require ($log.data.buildId -eq $fixture.buildId -and $log.data.logId -eq $fixture.logId -and $log.data.lines.Count -gt 0) 'Targeted log fixture mismatch.'
    Write-Output 'PASS: authenticated build identity, diagnosis and targeted log.'
    $index = 0
    foreach ($artifact in $fixture.artifacts) {
        $selectors = @('--build-id', [string]$artifact.buildId, '--artifact-name', $artifact.name)
        $metadata = Invoke-Read 'Artifact metadata' (@('build', 'artifact', 'get') + $selectors)
        Require ($metadata.data.resourceType -ceq $artifact.resourceType -and $metadata.data.name -ceq $artifact.name) 'Artifact type/name mismatch.'
        $destination = Join-Path $scratch "artifact-$index.zip"
        $null = Invoke-Read 'Artifact download' (@('build', 'artifact', 'download', '--destination', $destination) + $selectors)
        $inspection = Invoke-Read 'Artifact inspection' @('artifact', 'inspect', '--file', $destination, '--entry', $artifact.entry)
        Require ($inspection.data.entries.Count -eq 1 -and $inspection.data.entries[0].sha256 -ieq $artifact.memberSha256) 'Artifact member digest mismatch.'
        $index++
    }
    Write-Output 'PASS: both artifact types downloaded and selected-member hashes verified.'
    $feedArgs = @('--scope', $fixture.scope, '--feed', $fixture.feed)
    $first = Invoke-Read 'Package first page' (@('package', 'list', '--protocol', 'NuGet', '--limit', '1') + $feedArgs) -PartialAllowed
    Require ($first.data.Count -eq 1 -and $first.meta.truncated -and $first.meta.completeness -ceq 'partial' -and $first.meta.continuationToken -ceq '1') 'First page did not meet fixture pagination contract.'
    $second = Invoke-Read 'Package second page' (@('package', 'list', '--protocol', 'NuGet', '--limit', '1', '--continuation-token', $first.meta.continuationToken) + $feedArgs) -PartialAllowed
    Require ($second.data.Count -eq 1 -and $second.meta.truncated -and $second.meta.completeness -ceq 'partial' -and $second.meta.continuationToken -ceq '2' -and $first.data[0].id -ne $second.data[0].id) 'Package continuation did not advance.'
    $packageArgs = $feedArgs + @('--name', $fixture.packageName, '--package-version', $fixture.packageVersion, '--all')
    $resolution = Invoke-Read 'Package resolution' (@('package', 'resolve') + $packageArgs)
    Require ($resolution.data.package.name -ieq $fixture.packageName -and $resolution.data.version.version -ceq $fixture.packageVersion) 'Package resolution mismatch.'
    $packagePath = Join-Path $scratch 'package.nupkg'
    $download = Invoke-Read 'Package download' (@('package', 'download', '--destination', $packagePath) + $packageArgs)
    Require ($download.data.sha256 -ieq $fixture.packageSha256) 'Package digest mismatch.'
    $package = Invoke-Read 'Package inspection' @('package', 'inspect', '--file', $packagePath, '--expected-sha256', $fixture.packageSha256)
    Require ($package.data.id -ieq $fixture.packageName -and $package.data.version -ceq $fixture.packageVersion) 'Package manifest identity mismatch.'
    Write-Output 'PASS: package pagination, exact resolution, download digest and manifest.'
} finally {
    # Delete only this operation's uniquely created directory; never a fixture-supplied path.
    $resolved = [IO.Path]::GetFullPath($scratch)
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $resolved.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notlike 'ado-live-*') { throw 'Unexpected cleanup path.' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
