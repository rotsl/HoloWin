# Build HoloWin, optionally test and publish its portable EXE distribution.
[CmdletBinding()]
param(
    [ValidateSet('Debug','Release')][string]$Configuration = 'Debug',
    [switch]$Publish,
    [switch]$RunTests,
    [switch]$NoRestore
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $repoRoot 'HoloWin.sln'
if ($RunTests) { & (Join-Path $PSScriptRoot 'download-hand-model.ps1') }
$buildArgs = @('build', $solution, '-c', $Configuration)
if ($NoRestore) { $buildArgs += '--no-restore' }
dotnet @buildArgs
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
if ($RunTests) {
    dotnet test $solution -c $Configuration --no-build --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
}
if ($Publish) { & (Join-Path $PSScriptRoot 'publish.ps1') -Configuration $Configuration }
