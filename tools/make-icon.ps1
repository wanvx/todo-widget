# Generates assets\logo.ico: the app / installer icon, multi-size, uncompressed DIB
# entries only (16/32/48/64/128/256) so every Windows shell path and Inno Setup's
# own icon parser can read it — PNG-in-ICO entries are not universally accepted.
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\make-icon.ps1
param(
    [string]$Out = (Join-Path (Split-Path -Parent $PSScriptRoot) 'assets\logo.ico')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$sizes = @(16, 32, 48, 64, 128, 256)

function New-RoundedRect([System.Drawing.RectangleF]$r, [float]$radius) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $radius * 2
    $p.AddArc($r.Left, $r.Top, $d, $d, 180, 90)
    $p.AddArc($r.Right - $d, $r.Top, $d, $d, 270, 90)
    $p.AddArc($r.Right - $d, $r.Bottom - $d, $d, $d, 0, 90)
    $p.AddArc($r.Left, $r.Bottom - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function New-LogoBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    $pad = [float]([Math]::Max(0.5, $size * 0.02))
    $rect = New-Object System.Drawing.RectangleF($pad, $pad, ($size - 2 * $pad), ($size - 2 * $pad))
    $path = New-RoundedRect $rect ([float]($size * 0.23))
    $bg = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 24, 24, 27))
    $g.FillPath($bg, $path)

    $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::White, ([float]($size * 0.115)))
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $g.DrawLines($pen, [System.Drawing.PointF[]]@(
        (New-Object System.Drawing.PointF(($size * 0.285), ($size * 0.525))),
        (New-Object System.Drawing.PointF(($size * 0.435), ($size * 0.675))),
        (New-Object System.Drawing.PointF(($size * 0.725), ($size * 0.335)))
    ))

    $pen.Dispose(); $bg.Dispose(); $path.Dispose(); $g.Dispose()
    return $bmp
}

# ICO stores each image as a BITMAPINFOHEADER + bottom-up BGRA pixels + a 1bpp AND mask.
function Get-DibBytes([System.Drawing.Bitmap]$bmp) {
    $w = $bmp.Width
    $h = $bmp.Height
    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter($ms)

    $bw.Write([int]40); $bw.Write([int]$w); $bw.Write([int]($h * 2))
    $bw.Write([int16]1); $bw.Write([int16]32); $bw.Write([int]0)
    $bw.Write([int]($w * $h * 4))
    $bw.Write([int]0); $bw.Write([int]0); $bw.Write([int]0); $bw.Write([int]0)

    for ($y = $h - 1; $y -ge 0; $y--) {
        for ($x = 0; $x -lt $w; $x++) {
            $c = $bmp.GetPixel($x, $y)
            $bw.Write([byte]$c.B); $bw.Write([byte]$c.G); $bw.Write([byte]$c.R); $bw.Write([byte]$c.A)
        }
    }

    # 32bpp images carry their own alpha, so the AND mask is all zero.
    $rowBytes = [int]([Math]::Ceiling($w / 32.0) * 4)
    $bw.Write((New-Object byte[] ($rowBytes * $h)), 0, $rowBytes * $h)

    $bw.Flush()
    $bytes = $ms.ToArray()
    $bw.Dispose(); $ms.Dispose()
    return $bytes
}

$images = @()
foreach ($s in $sizes) {
    $bmp = New-LogoBitmap $s
    $images += [pscustomobject]@{ Size = $s; Data = (Get-DibBytes $bmp) }
    $bmp.Dispose()
}

$dir = Split-Path -Parent $Out
if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }

$fs = [System.IO.File]::Create($Out)
$bw = New-Object System.IO.BinaryWriter($fs)

$bw.Write([int16]0)            # reserved
$bw.Write([int16]1)            # type: icon
$bw.Write([int16]$images.Count)

$offset = 6 + 16 * $images.Count
foreach ($img in $images) {
    $dim = if ($img.Size -ge 256) { 0 } else { $img.Size }   # 0 means 256
    $bw.Write([byte]$dim); $bw.Write([byte]$dim)
    $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([int16]1); $bw.Write([int16]32)
    $bw.Write([int]$img.Data.Length)
    $bw.Write([int]$offset)
    $offset += $img.Data.Length
}

foreach ($img in $images) { $bw.Write($img.Data, 0, $img.Data.Length) }
$bw.Flush(); $bw.Dispose(); $fs.Dispose()

# Read it back so a malformed container fails here rather than inside Inno Setup.
$icon = New-Object System.Drawing.Icon($Out)
Write-Output ("wrote {0}  ({1:N1} KB, {2} sizes, loaded back as {3}x{4})" -f `
    $Out, ((Get-Item $Out).Length / 1KB), $images.Count, $icon.Width, $icon.Height)
$icon.Dispose()
