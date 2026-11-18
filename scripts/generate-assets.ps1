# Renders the DriverCompass icon (compass rose) into the PNG/ICO sizes needed by the app and MSIX.
# Usage: powershell -ExecutionPolicy Bypass -File scripts\generate-assets.ps1
# Requires Windows PowerShell 5.1 (System.Drawing). Output: src\DriverCompass.App\Assets\

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$out = Join-Path $PSScriptRoot '..\src\DriverCompass.App\Assets'
New-Item -ItemType Directory -Force -Path $out | Out-Null

function New-IconBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    $m = $size * 0.06
    $d = $size - 2 * $m
    $bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.PointF 0, 0), (New-Object System.Drawing.PointF $size, $size),
        [System.Drawing.Color]::FromArgb(255, 0, 120, 212), [System.Drawing.Color]::FromArgb(255, 0, 78, 140))
    $g.FillEllipse($bg, $m, $m, $d, $d)

    $ring = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(230, 255, 255, 255)), ([Math]::Max(1, $size * 0.04))
    $r = $size * 0.16
    $g.DrawEllipse($ring, $m + $r * 0.5, $m + $r * 0.5, $d - $r, $d - $r)

    $c = $size / 2.0
    $len = $size * 0.33
    $w = $size * 0.11
    $north = [System.Drawing.PointF[]]@(
        (New-Object System.Drawing.PointF $c, ($c - $len)),
        (New-Object System.Drawing.PointF ($c + $w), $c),
        (New-Object System.Drawing.PointF ($c - $w), $c))
    $south = [System.Drawing.PointF[]]@(
        (New-Object System.Drawing.PointF $c, ($c + $len)),
        (New-Object System.Drawing.PointF ($c + $w), $c),
        (New-Object System.Drawing.PointF ($c - $w), $c))
    $g.FillPolygon((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 255, 92, 72))), $north)
    $g.FillPolygon((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)), $south)
    $dot = $size * 0.07
    $g.FillEllipse((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 0, 60, 110))), $c - $dot / 2, $c - $dot / 2, $dot, $dot)

    $g.Dispose()
    return $bmp
}

function Save-Png([int]$size, [string]$name) {
    $bmp = New-IconBitmap $size
    $bmp.Save((Join-Path $out $name), [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}

Save-Png 44 'Square44x44Logo.png'
Save-Png 150 'Square150x150Logo.png'
Save-Png 50 'StoreLogo.png'

# Multi-size ICO with PNG-compressed entries (supported since Windows Vista).
$sizes = 16, 24, 32, 48, 64, 128, 256
$images = foreach ($s in $sizes) {
    $bmp = New-IconBitmap $s
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    , $ms.ToArray()
}

$ico = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter $ico
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]
    $dim = if ($s -ge 256) { 0 } else { $s }
    $bw.Write([byte]$dim); $bw.Write([byte]$dim); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([UInt16]1); $bw.Write([UInt16]32)
    $bw.Write([UInt32]$images[$i].Length); $bw.Write([UInt32]$offset)
    $offset += $images[$i].Length
}
foreach ($img in $images) { $bw.Write($img) }
$bw.Flush()
[System.IO.File]::WriteAllBytes((Join-Path $out 'DriverCompass.ico'), $ico.ToArray())

Write-Host "Assets written to $out"
