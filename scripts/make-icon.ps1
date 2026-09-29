# Draws the speech-bubble icon (half said, half dashed) into src/QuickVoice/app.ico and docs/icon.png.
# Geometry follows partway's docs/icon.svg (1024 units). Run: pwsh scripts/make-icon.ps1
Add-Type -AssemblyName System.Drawing

$out = Join-Path $PSScriptRoot '..\src\QuickVoice\app.ico'
$sizes = 16, 20, 24, 32, 40, 48, 64, 256

function RoundRect([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = 2 * $r
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    $p
}

function Draw([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.PixelOffsetMode = 'HighQuality'
    # The yellow tile fills more of the canvas than on macOS: Windows icons have no built-in margin.
    $k = ($size * 0.94) / 824
    $g.TranslateTransform($size * 0.03 - 100 * $k, $size * 0.03 - 100 * $k)
    $g.ScaleTransform($k, $k)
    $ink = [System.Drawing.Color]::FromArgb(29, 29, 31)

    $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 214, 10))), (RoundRect 100 100 824 824 184))

    $g.SetClip((New-Object System.Drawing.RectangleF 0, 0, 512, 1024))
    $said = New-Object System.Drawing.SolidBrush $ink
    $g.FillPath($said, (RoundRect 232 312 560 360 180))
    $g.FillPolygon($said, [System.Drawing.PointF[]]@((New-Object System.Drawing.PointF 336, 622), (New-Object System.Drawing.PointF 286, 756), (New-Object System.Drawing.PointF 438, 658)))

    # Small sizes get a thicker, sparser dash so the unsaid half still reads at 16 px.
    $stroke = [Math]::Max(28, 1.6 / $k)
    $g.SetClip((New-Object System.Drawing.RectangleF 532, 0, 492, 1024))
    $pen = New-Object System.Drawing.Pen $ink, $stroke
    $pen.DashCap = 'Round'
    $pen.DashPattern = [float[]]@((54 / 28), (40 / 28) * [Math]::Max(1, $stroke / 28))
    $g.DrawPath($pen, (RoundRect 246 326 532 332 166))
    $g.Dispose()
    $bmp
}

$images = foreach ($s in $sizes) {
    $ms = New-Object System.IO.MemoryStream
    (Draw $s).Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    , $ms.ToArray()
}

# ICO container with PNG frames (Windows Vista and later).
$file = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $file
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]
    $w.Write([byte]($s % 256)); $w.Write([byte]($s % 256)); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$images[$i].Length); $w.Write([uint32]$offset)
    $offset += $images[$i].Length
}
foreach ($img in $images) { $w.Write($img) }
[System.IO.File]::WriteAllBytes((Resolve-Path (Split-Path $out)).Path + '\app.ico', $file.ToArray())
$docs = Join-Path $PSScriptRoot '..\docs'
New-Item -ItemType Directory -Force -Path $docs | Out-Null
[System.IO.File]::WriteAllBytes((Resolve-Path $docs).Path + '\icon.png', $images[-1])
"wrote $out and docs/icon.png"
