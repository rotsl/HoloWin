# Fetch the OpenCV Zoo palm/hand models used by HoloWin. Models are Apache-2.0.
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$modelsDir = Join-Path $repoRoot 'models'
New-Item -ItemType Directory -Path $modelsDir -Force | Out-Null
$revision = '47534e27c9851bb1128ccc0102f1145e27f23f98'
$models = @(
    @{ Folder='palm_detection_mediapipe'; File='palm_detection_mediapipe_2023feb.onnx'; Hash='78ff51c38496b7fc8b8ebdb6cc8c1abb02fa6c38427c6848254cdaba57fcce7c' },
    @{ Folder='handpose_estimation_mediapipe'; File='handpose_estimation_mediapipe_2023feb.onnx'; Hash='db0898ae717b76b075d9bf563af315b29562e11f8df5027a1ef07b02bef6d81c' }
)
foreach ($model in $models) {
    $destination = Join-Path $modelsDir $model.File
    if ((Test-Path -LiteralPath $destination) -and (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash -eq $model.Hash) { continue }
    $temporary = "$destination.download"
    Invoke-WebRequest -Uri "https://media.githubusercontent.com/media/opencv/opencv_zoo/$revision/models/$($model.Folder)/$($model.File)" -OutFile $temporary
    if ((Get-FileHash -LiteralPath $temporary -Algorithm SHA256).Hash -ne $model.Hash) { throw "Checksum failed: $($model.File)" }
    Move-Item -LiteralPath $temporary -Destination $destination -Force
    Write-Host "Verified $($model.File)"
}
