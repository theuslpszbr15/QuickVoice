# Draws the QuickVoice icon into src/QuickVoice/app.ico and docs/icon.png: a "Q" made of a voice ring with
# three level bars inside and a lightning-bolt tail (quick + voice), on a dark tile. Run: pwsh scripts/make-icon.ps1
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

function Pt([float]$x, [float]$y) { New-Object System.Drawing.PointF $x, $y }
function Rgb([int]$r, [int]$g, [int]$b) { [System.Drawing.Color]::FromArgb($r, $g, $b) }

function Spectrum([float]$x0, [float]$y0, [float]$x1, [float]$y1) {
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush (Pt $x0 $y0), (Pt $x1 $y1), (Rgb 92 225 255), (Rgb 255 111 181)
    $blend = New-Object System.Drawing.Drawing2D.ColorBlend 3
    $blend.Colors = [System.Drawing.Color[]]@((Rgb 92 225 255), (Rgb 139 108 255), (Rgb 255 111 181))
    $blend.Positions = [float[]]@(0, 0.5, 1)
    $brush.InterpolationColors = $blend
    $brush
}

function Draw([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.PixelOffsetMode = 'HighQuality'
    $k = $size / 1024
    $g.ScaleTransform($k, $k)
    $small = $size -le 24  # fewer, bolder strokes so it still reads in the tray

    $tileColor = Rgb 19 17 34
    $tile = New-Object System.Drawing.Drawing2D.LinearGradientBrush (Pt 0 0), (Pt 1024 1024), (Rgb 38 32 66), (Rgb 11 10 22)
    $g.FillPath($tile, (RoundRect 24 24 976 976 230))

    $ring = if ($small) { 118 } else { 92 }
    $pen = New-Object System.Drawing.Pen (Spectrum 200 200 820 820), $ring
    $g.DrawEllipse($pen, 222, 222, 560, 560)

    # The tail: a bolt cut out of the ring by a tile-colored outline.
    $bolt = New-Object System.Drawing.Drawing2D.GraphicsPath
    $bolt.AddPolygon([System.Drawing.PointF[]]@((Pt 40 0), (Pt 100 0), (Pt 72 55), (Pt 112 55), (Pt 30 150), (Pt 50 82), (Pt 8 82)))
    $m = New-Object System.Drawing.Drawing2D.Matrix
    $m.Translate(765, 765); $m.Rotate(-36); $m.Scale(2.0, 2.0); $m.Translate(-60, -75)
    $bolt.Transform($m)
    $cut = New-Object System.Drawing.Pen $tileColor, ($(if ($small) { 70 } else { 46 }))
    $cut.LineJoin = 'Round'
    $g.DrawPath($cut, $bolt)
    $g.FillPath((Spectrum 640 640 900 900), $bolt)

    $white = New-Object System.Drawing.SolidBrush (Rgb 245 245 250)
    $bars = if ($small) { @(@(400, 190), @(560, 300)) } else { @(@(398, 160), @(502, 300), @(606, 210)) }
    $w = if ($small) { 110 } else { 70 }
    foreach ($b in $bars) {
        $x = $b[0] - $w / 2; $h = $b[1]
        $g.FillPath($white, (RoundRect $x (502 - $h / 2) $w $h ($w / 2)))
    }
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
