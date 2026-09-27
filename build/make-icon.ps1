# Convert assets\app-icon.png (1024x1024) into a multi-size assets\app.ico (PNG-in-ICO).
# Usage: powershell -NoProfile -ExecutionPolicy Bypass -File build\make-icon.ps1
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing
$root = Split-Path -Parent $PSScriptRoot
$srcPath = Join-Path $root "assets\app-icon.png"
$outPath = Join-Path $root "assets\app.ico"
if (-not (Test-Path $srcPath)) { Write-Error "missing $srcPath"; exit 1 }

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$src = [System.Drawing.Image]::FromFile($srcPath)
$frames = New-Object System.Collections.ArrayList
foreach ($s in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap($s, $s)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.DrawImage($src, 0, 0, $s, $s)
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    [void]$frames.Add($ms.ToArray())
    $ms.Dispose()
}
$src.Dispose()

$out = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter($out)
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]
    $dim = if ($s -ge 256) { 0 } else { $s }
    $bw.Write([Byte]$dim); $bw.Write([Byte]$dim)
    $bw.Write([Byte]0); $bw.Write([Byte]0)
    $bw.Write([UInt16]1); $bw.Write([UInt16]32)
    $bw.Write([UInt32]$frames[$i].Length)
    $bw.Write([UInt32]$offset)
    $offset += $frames[$i].Length
}
foreach ($f in $frames) { $bw.Write($f) }
$bw.Flush()
[System.IO.File]::WriteAllBytes($outPath, $out.ToArray())
$bw.Dispose(); $out.Dispose()
Write-Host ("wrote {0} ({1:N0} bytes)" -f $outPath, (Get-Item $outPath).Length)
