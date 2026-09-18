# Requires PowerShell 7. Builds/packages must already exist; no publication or global installation.
[CmdletBinding()]
param([string]$PackageDirectory = (Join-Path $PSScriptRoot '../artifacts/packages'))

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$packageSource = (Resolve-Path -LiteralPath $PackageDirectory).Path
[xml]$properties = Get-Content -LiteralPath (Join-Path $repository 'Directory.Build.props') -Raw
$expectedVersion = [string]$properties.Project.PropertyGroup.Version
$packageFile = Join-Path $packageSource "PattonTech.Ado.Cli.$expectedVersion.nupkg"
if (-not (Test-Path -LiteralPath $packageFile -PathType Leaf)) { throw 'Pack the CLI before running the installation smoke test.' }
$scratch = Join-Path $repository ('artifacts/tool-smoke/' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($scratch) | Out-Null
$previousPackages = [Environment]::GetEnvironmentVariable('NUGET_PACKAGES', 'Process')
try {
    # Separate caches ensure this package, rather than an older package with the same version, is installed.
    $env:NUGET_PACKAGES = Join-Path $scratch 'nuget-cache'
    $nugetConfig = Join-Path $scratch 'NuGet.Config'
    $escapedSource = [Security.SecurityElement]::Escape($packageSource)
    [IO.File]::WriteAllText($nugetConfig, "<configuration><packageSources><clear/><add key=`"local`" value=`"$escapedSource`"/></packageSources></configuration>")
    $toolDirectory = Join-Path $scratch 'tool'
    dotnet tool install PattonTech.Ado.Cli --version $expectedVersion --tool-path $toolDirectory --configfile $nugetConfig --no-http-cache
    if ($LASTEXITCODE -ne 0) { throw 'Isolated tool installation failed.' }
    $executable = Join-Path $toolDirectory $(if ($IsWindows) { 'ado.exe' } else { 'ado' })
    $versionOutput = & $executable --version
    if ($LASTEXITCODE -ne 0 -or $versionOutput -cne $expectedVersion) { throw 'Installed command version mismatch.' }
    $helpOutput = & $executable package inspect --help
    if ($LASTEXITCODE -ne 0 -or ($helpOutput -join "`n") -notmatch '--expected-sha256') { throw 'Installed command help is incomplete.' }

    $fixture = Join-Path $scratch 'Fixture.nupkg'
    $manifest = '<package><metadata><id>Smoke.Fixture</id><version>1.2.3</version><dependencies><group targetFramework="net10.0"><dependency id="Smoke.Dependency" version="[1.0,2.0)"/></group></dependencies></metadata></package>'
    $archive = [IO.Compression.ZipFile]::Open($fixture, [IO.Compression.ZipArchiveMode]::Create)
    try {
        $writer = [IO.StreamWriter]::new($archive.CreateEntry('Fixture.nuspec').Open(), [Text.UTF8Encoding]::new($false))
        try { $writer.Write($manifest) } finally { $writer.Dispose() }
    } finally { $archive.Dispose() }
    $digest = (Get-FileHash -LiteralPath $fixture -Algorithm SHA256).Hash.ToLowerInvariant()
    $arguments = @('package', 'inspect', '--file', $fixture, '--expected-sha256', $digest,
        '--config', (Join-Path $scratch 'absent.json'), '--json', '--non-interactive', '--read-only')
    $first = & $executable @arguments
    if ($LASTEXITCODE -ne 0) { throw 'Installed package inspection failed.' }
    $second = & $executable @arguments
    if ($LASTEXITCODE -ne 0 -or ($first -join "`n") -cne ($second -join "`n")) { throw 'Local JSON output is not deterministic.' }
    $result = $first | ConvertFrom-Json
    if (-not $result.ok -or $result.meta.schemaVersion -ne 1 -or $result.meta.completeness -cne 'complete' -or
        $result.data.id -cne 'Smoke.Fixture' -or $result.data.version -cne '1.2.3' -or $result.data.archiveSha256 -cne $digest -or
        $result.data.dependencyGroups[0].dependencies[0].version -cne '[1.0,2.0)') { throw 'Installed command returned unexpected inspection data.' }
    $rejected = & $executable package inspect --file $fixture --expected-sha256 ('0' * 64) --json --non-interactive --read-only
    if ($LASTEXITCODE -ne 7 -or ($rejected | ConvertFrom-Json).error.code -cne 'archive_hash_mismatch') { throw 'Installed command did not enforce the expected hash.' }
    Write-Output "Installed ado $expectedVersion smoke checks passed on $([Runtime.InteropServices.RuntimeInformation]::RuntimeIdentifier)."
} finally {
    [Environment]::SetEnvironmentVariable('NUGET_PACKAGES', $previousPackages, 'Process')
    # Deliberately retain isolated outputs under ignored artifacts/ for failure investigation.
    Write-Output "Smoke artifacts: $scratch"
}
# The final native invocation intentionally returned 7. Report script success to CI callers.
$global:LASTEXITCODE = 0
