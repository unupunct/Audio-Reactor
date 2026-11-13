# Generates Assets\AudioReactor.ico (16..256 px, PNG-compressed frames) from code: a neon equalizer glyph on a rounded gradient tile.
param([string]$OutDir = "$PSScriptRoot\..\Source\AudioReactor.App\Assets")
Add-Type -AssemblyName System.Drawing
New-Item -ItemType Directory -Force $OutDir | Out-Null

function New-Frame([int]$S) {
    $bmp = New-Object System.Drawing.Bitmap $S, $S, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp); $g.SmoothingMode = 'AntiAlias'; $g.Clear([System.Drawing.Color]::Transparent)
    $r = [int]($S * 0.22); $d = $r * 2
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc(0, 0, $d, $d, 180, 90); $path.AddArc($S - $d - 1, 0, $d, $d, 270, 90); $path.AddArc($S - $d - 1, $S - $d - 1, $d, $d, 0, 90); $path.AddArc(0, $S - $d - 1, $d, $d, 90, 90); $path.CloseFigure()
    $bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.Point 0, 0), (New-Object System.Drawing.Point $S, $S), ([System.Drawing.Color]::FromArgb(255, 14, 18, 32)), ([System.Drawing.Color]::FromArgb(255, 40, 16, 70))
    $g.FillPath($bg, $path)
    $heights = @(0.35, 0.6, 0.85, 0.5, 0.7)
    $bw = $S * 0.11; $gap = $S * 0.055; $total = 5 * $bw + 4 * $gap; $x0 = ($S - $total) / 2
    for ($i = 0; $i -lt 5; $i++) {
        $h = $S * 0.62 * $heights[$i]; $x = $x0 + $i * ($bw + $gap); $y = ($S - $h) / 2
        $br = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.PointF $x, $y), (New-Object System.Drawing.PointF $x, ($y + $h)), ([System.Drawing.Color]::FromArgb(255, 0, 229, 255)), ([System.Drawing.Color]::FromArgb(255, 166, 77, 255))
        $rr = [Math]::Max(1, $bw / 2)
        $bp = New-Object System.Drawing.Drawing2D.GraphicsPath
        $bp.AddArc($x, $y, $bw, $bw, 180, 180); $bp.AddArc($x, $y + $h - $bw, $bw, $bw, 0, 180); $bp.CloseFigure()
        $g.FillPath($br, $bp)
    }
    $g.Dispose(); return $bmp
}

$sizes = 16, 24, 32, 48, 64, 128, 256
$pngs = foreach ($s in $sizes) { $b = New-Frame $s; $ms = New-Object System.IO.MemoryStream; $b.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png); ,$ms.ToArray(); if ($s -eq 256) { $b.Save("$OutDir\icon256.png", [System.Drawing.Imaging.ImageFormat]::Png) } }
$fs = [System.IO.File]::Create("$OutDir\AudioReactor.ico"); $bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $bytes = $pngs[$i]
    $bw.Write([byte]($(if ($s -ge 256) { 0 } else { $s }))); $bw.Write([byte]($(if ($s -ge 256) { 0 } else { $s }))); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([UInt16]1); $bw.Write([UInt16]32); $bw.Write([UInt32]$bytes.Length); $bw.Write([UInt32]$offset); $offset += $bytes.Length
}
foreach ($b in $pngs) { $bw.Write([byte[]]$b) }
$bw.Close(); $fs.Close(); "icon written to $OutDir"
