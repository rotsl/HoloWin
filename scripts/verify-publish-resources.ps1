[CmdletBinding()]
param([Parameter(Mandatory)][string]$PublishDirectory)
$ErrorActionPreference = 'Stop'
$packageRoot = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE '.nuget/packages' }
$makepri = Get-ChildItem (Join-Path $packageRoot 'microsoft.windows.sdk.buildtools') -Recurse -Filter makepri.exe |
    Where-Object { $_.Directory.Name -eq 'x64' } | Sort-Object FullName -Descending | Select-Object -First 1
if (!$makepri) { throw 'Windows SDK MakePRI is required to verify the published resources.' }
$dump = Join-Path ([IO.Path]::GetTempPath()) ('holowin-pri-' + [guid]::NewGuid().ToString('N') + '.xml')
try {
    & $makepri.FullName dump /if (Join-Path $PublishDirectory 'resources.pri') /of $dump /o | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Could not inspect the published resource index.' }
    [xml]$resources = Get-Content -LiteralPath $dump -Raw
    $theme = $resources.SelectNodes('//NamedResource') | Where-Object {
        $_.GetAttribute('uri') -match '/Files/Microsoft\.UI\.Xaml/Themes/themeresources\.xbf$'
    }
    if (!$theme) { throw 'Published resources.pri is missing the WinUI theme. Rebuild with WindowsAppSDKSelfContained=true.' }
    Write-Host 'Published resource index contains the WinUI theme.'
} finally { if (Test-Path -LiteralPath $dump) { Remove-Item -LiteralPath $dump } }
