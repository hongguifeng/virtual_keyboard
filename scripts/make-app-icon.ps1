# Generates src/VirtualKeyboard.App/Assets/app.ico (multi-size, PNG-compressed)
# matching the modern keyboard theme, plus a 256px PNG preview for review.
param(
    [string]$RepoRoot = (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))
)

Add-Type -AssemblyName System.Drawing

$AssetDir = Join-Path $RepoRoot 'src\VirtualKeyboard.App\Assets'
New-Item -ItemType Directory -Force -Path $AssetDir | Out-Null
$IcoPath = Join-Path $AssetDir 'app.ico'
$PreviewPath = Join-Path $RepoRoot 'artifacts\app-icon-preview.png'

# Colors from MainWindow.xaml modern theme
$Tile   = [System.Drawing.Color]::FromArgb(255, 0x1B, 0x25, 0x34)  # dark navy tile
$Key    = [System.Drawing.Color]::FromArgb(255, 0xF5, 0xF7, 0xFB)  # keycaps
$Space  = [System.Drawing.Color]::FromArgb(255, 0x50, 0x60, 0x78)  # spacebar
$Accent = [System.Drawing.Color]::FromArgb(255, 0x4F, 0x9B, 0xFF)  # status dot

function New-RoundedPath([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    if ($r -le 0) { $p.AddRectangle([System.Drawing.RectangleF]::new($x, $y, $w, $h)); return $p }
    $d = $r * 2
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

# Geometry is defined in a 256px design space and scaled per output size.
function Draw-Icon([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $xform = New-Object System.Drawing.Drawing2D.Matrix
    $xform.Scale($size / 256.0, $size / 256.0)
    $g.Transform = $xform

    $brushTile   = New-Object System.Drawing.SolidBrush($Tile)
    $brushKey    = New-Object System.Drawing.SolidBrush($Key)
    $brushSpace  = New-Object System.Drawing.SolidBrush($Space)
    $brushAccent = New-Object System.Drawing.SolidBrush($Accent)

    $g.FillPath($brushTile, (New-RoundedPath 8 8 240 240 52))          # rounded tile
    $g.FillPath($brushAccent, (New-RoundedPath 24 30 18 18 5))         # status dot
    foreach ($col in 0..3) {                                          # 2 rows x 4 keycaps
        $x = 38 + $col * 48
        $g.FillPath($brushKey, (New-RoundedPath $x 64 36 36 8))
        $g.FillPath($brushKey, (New-RoundedPath $x 112 36 36 8))
    }
    $g.FillPath($brushSpace, (New-RoundedPath 68 160 120 36 10))       # spacebar

    foreach ($b in @($brushTile, $brushKey, $brushSpace, $brushAccent)) { $b.Dispose() }
    $g.Dispose()
    return $bmp
}

$pngs = New-Object System.Collections.Generic.List[byte[]]
$sizes = @(16, 24, 32, 48, 64, 128, 256)
foreach ($s in $sizes) {
    $bmp = Draw-Icon $s
    if ($s -eq 256) { $bmp.Save($PreviewPath, [System.Drawing.Imaging.ImageFormat]::Png) }
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngs.Add($ms.ToArray())
    $ms.Dispose()
    $bmp.Dispose()
}

# Assemble ICO container: 6-byte header, 16-byte directory entries, PNG blobs.
$ms = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter($ms)
$bw.Write([Int16]0)
$bw.Write([Int16]1)
$bw.Write([Int16]$pngs.Count)
$offset = 6 + 16 * $pngs.Count
for ($i = 0; $i -lt $pngs.Count; $i++) {
    $d = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $bw.Write([Byte]$d); $bw.Write([Byte]$d)
    $bw.Write([Byte]0); $bw.Write([Byte]0)
    $bw.Write([Int16]1)      # planes
    $bw.Write([Int16]32)     # bpp
    $bw.Write([Int32]$pngs[$i].Length)
    $bw.Write([Int32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($png in $pngs) { $bw.Write($png) }
$bw.Flush()
[System.IO.File]::WriteAllBytes($IcoPath, $ms.ToArray())
$bw.Dispose(); $ms.Dispose()

Write-Host "wrote $IcoPath"
Write-Host "preview $PreviewPath"
