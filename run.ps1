# Build and start HoloWin from this checkout.
[CmdletBinding()]
param([switch]$NoBuild)

$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'src/HoloWin.App/HoloWin.App.csproj'
if (-not $NoBuild) {
    dotnet build $project -c Debug -r win-x64 --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Build failed. Close any running development copy of HoloWin and retry.' }
}
$exe = Join-Path $PSScriptRoot 'src/HoloWin.App/bin/Debug/net10.0-windows10.0.22621.0/win-x64/HoloWin.exe'
if (-not (Test-Path -LiteralPath $exe)) { throw 'No Debug app found. Run .\run.ps1 without -NoBuild first.' }
Start-Process -FilePath $exe -WorkingDirectory (Split-Path -Parent $exe)
Write-Host 'HoloWin started. This terminal is free; close the app using its title bar or tray menu.'
