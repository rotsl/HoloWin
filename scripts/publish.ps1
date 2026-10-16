# Build a portable, self-contained Windows x64 folder and ZIP.
[CmdletBinding()]
param([ValidateSet('Debug','Release')][string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
& (Join-Path $PSScriptRoot 'download-hand-model.ps1')
$destination = Join-Path $repoRoot 'artifacts/HoloWin-win-x64'
$project = Join-Path $repoRoot 'src/HoloWin.App/HoloWin.App.csproj'
# Recreate the PRI with distribution settings after any framework-dependent build.
dotnet clean $project -c $Configuration -r win-x64 -p:SelfContained=true -p:WindowsAppSDKSelfContained=true --verbosity quiet
if ($LASTEXITCODE -ne 0) { throw 'Cleaning before self-contained publishing failed.' }
dotnet publish $project -c $Configuration -r win-x64 --self-contained true -p:WindowsAppSDKSelfContained=true -p:PublishSingleFile=false -p:PublishTrimmed=false -o $destination
if ($LASTEXITCODE -ne 0) { throw 'Publishing HoloWin failed.' }
Copy-Item -LiteralPath (Join-Path $repoRoot 'LICENSE') -Destination (Join-Path $destination 'LICENSE.txt') -Force
Copy-Item -LiteralPath (Join-Path $repoRoot 'README.md') -Destination (Join-Path $destination 'README.md') -Force
@'
HoloWin - Windows 11 x64 portable application

Extract the complete ZIP, then launch HoloWin.exe. Keep all DLLs and resource
folders together. No Microsoft Store installation is needed.

Start with Hardware > select/enable microphone, Profiles > create/select,
Calibration > collect and save ten taps per zone, Actions > save and assign,
then Dashboard > Start listening. Each PC and desk needs its own calibration.

Camera preview, hand tracking and gesture assignments are available. Start
tracking in Camera Studio, then enable gesture actions for that session.
The verified palm/hand models are bundled; inference runs locally.
Global zone hotkeys are configured in Settings. The tray menu can show the
window, pause sensors or quit. Tracking continues while the window is hidden.

To update: close HoloWin, extract the new release into a separate folder, and
run its HoloWin.exe. Do not mix DLLs from different releases. Your local data
stays in %LOCALAPPDATA%\HoloWin. Back up that folder before upgrading. If you
enabled Start at sign-in, disable it before moving the app and enable it again
from the new location.

See README.md for usage and known limitations; see LICENSE.txt for the license.
'@ | Set-Content (Join-Path $destination 'START-HERE.txt')
$required = @('HoloWin.exe', 'coreclr.dll', 'Microsoft.UI.Xaml.dll',
    'resources.pri', 'Microsoft.UI.pri', 'Microsoft.UI.Xaml.Controls.pri',
    'Assets/HoloWin.ico', 'Assets/HoloWin.png',
    'HoloWin.Vision.dll', 'OpenCvSharpExtern.dll', 'Microsoft.ML.OnnxRuntime.dll', 'onnxruntime.dll',
    'models/palm_detection_mediapipe_2023feb.onnx', 'models/handpose_estimation_mediapipe_2023feb.onnx', 'models/LICENSE-OpenCV-Zoo.txt')
foreach ($file in $required) {
    if (-not (Test-Path -LiteralPath (Join-Path $destination $file))) { throw "Publish is missing $file" }
}
& (Join-Path $PSScriptRoot 'verify-publish-resources.ps1') -PublishDirectory $destination
Compress-Archive -Path "$destination/*" -DestinationPath (Join-Path $repoRoot 'artifacts/HoloWin-win-x64.zip') -Force
$zipPath = Join-Path $repoRoot 'artifacts/HoloWin-win-x64.zip'
$hash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash
"$hash  HoloWin-win-x64.zip" | Set-Content "$zipPath.sha256"
Write-Host 'Published artifacts/HoloWin-win-x64.zip. Extract all files before launching HoloWin.exe.'
