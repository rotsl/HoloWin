# Build one EXE containing the complete WinUI application and native dependencies.
[CmdletBinding()]
param([switch]$UseExistingPayload)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $UseExistingPayload) { & (Join-Path $PSScriptRoot 'publish.ps1') }
$project = Join-Path $repoRoot 'tools/PortableLauncher/PortableLauncher.csproj'
$output = Join-Path $repoRoot 'artifacts/standalone'
dotnet publish $project -c Release -r win-x64 --self-contained true -o $output
if ($LASTEXITCODE -ne 0) { throw 'Single EXE publishing failed.' }
$exe = Join-Path $output 'HoloWin.exe'
$hash = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash
"$hash  HoloWin.exe" | Set-Content "$exe.sha256"
Write-Host 'Single-file distribution: artifacts/standalone/HoloWin.exe'
