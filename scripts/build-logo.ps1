# Render the simple SVG shapes into PNG artwork and a multi-resolution Windows icon.
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$assets = Join-Path (Split-Path -Parent $PSScriptRoot) 'assets'
[xml]$svg = Get-Content -LiteralPath (Join-Path $assets 'HoloWin.svg') -Raw
function New-LogoBitmap([int]$size) {
    $bitmap = [Drawing.Bitmap]::new($size, $size)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.Clear([Drawing.Color]::Transparent)
        $graphics.ScaleTransform($size / 256.0, $size / 256.0)
        foreach ($shape in $svg.DocumentElement.ChildNodes) {
            $brush = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml($shape.fill))
            try {
                if ($shape.LocalName -eq 'circle') {
                    $radius = [single]$shape.r
                    $graphics.FillEllipse($brush, ([single]$shape.cx - $radius), ([single]$shape.cy - $radius), 2*$radius, 2*$radius)
                } elseif ($shape.LocalName -eq 'rect') {
                    $x = [single]$shape.x; $y = [single]$shape.y
                    $w = [single]$shape.width; $h = [single]$shape.height; $d = 2 * [single]$shape.rx
                    $path = [Drawing.Drawing2D.GraphicsPath]::new()
                    try {
                        $path.AddArc($x, $y, $d, $d, 180, 90)
                        $path.AddArc($x+$w-$d, $y, $d, $d, 270, 90)
                        $path.AddArc($x+$w-$d, $y+$h-$d, $d, $d, 0, 90)
                        $path.AddArc($x, $y+$h-$d, $d, $d, 90, 90)
                        $path.CloseFigure(); $graphics.FillPath($brush, $path)
                    } finally { $path.Dispose() }
                } else { throw "Unsupported SVG shape: $($shape.LocalName)" }
            } finally { $brush.Dispose() }
        }
    } finally { $graphics.Dispose() }
    return $bitmap
}
$preview = New-LogoBitmap 512
try { $preview.Save((Join-Path $assets 'HoloWin.png'), [Drawing.Imaging.ImageFormat]::Png) } finally { $preview.Dispose() }
$sizes = @(16, 24, 32, 48, 64, 128, 256)
$images = foreach ($size in $sizes) {
    $bitmap = New-LogoBitmap $size
    $stream = [IO.MemoryStream]::new()
    try { $bitmap.Save($stream, [Drawing.Imaging.ImageFormat]::Png); ,$stream.ToArray() }
    finally { $stream.Dispose(); $bitmap.Dispose() }
}
$output = [IO.File]::Create((Join-Path $assets 'HoloWin.ico'))
$writer = [IO.BinaryWriter]::new($output)
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $dimension = if ($sizes[$i] -eq 256) { 0 } else { $sizes[$i] }
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
        $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$images[$i].Length); $writer.Write([uint32]$offset)
        $offset += $images[$i].Length
    }
    foreach ($bytes in $images) { $writer.Write([byte[]]$bytes) }
} finally { $writer.Dispose() }
Write-Host 'Generated assets/HoloWin.png and assets/HoloWin.ico from assets/HoloWin.svg.'
