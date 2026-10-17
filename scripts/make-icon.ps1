# Gera assets\app.ico (icone do app e do instalador). Uso: powershell -File scripts\make-icon.ps1
# Desenho: tres barras nas cores de CPU, GPU e RAM sobre um quadrado escuro arredondado.
Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $PSScriptRoot
$out  = Join-Path $root 'assets\app.ico'
$sizes = 16, 24, 32, 48, 64, 128, 256

function New-RoundedPath([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $d = [Math]::Min($r * 2, [Math]::Min($w, $h))
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function New-IconPng([int]$s) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.PixelOffsetMode = 'HighQuality'
    $g.Clear([System.Drawing.Color]::Transparent)

    # fundo: quadrado arredondado com degrade vertical
    $bgRect = New-Object System.Drawing.RectangleF 0, 0, $s, $s
    $bgPath = New-RoundedPath 0 0 $s $s ($s * 0.22)
    $bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush $bgRect,
        ([System.Drawing.Color]::FromArgb(255, 38, 46, 60)), ([System.Drawing.Color]::FromArgb(255, 16, 19, 25)), 90
    $g.FillPath($bg, $bgPath)

    # barras: CPU (azul), GPU (verde), RAM (lilas), alturas diferentes como num medidor
    $colors  = @(
        [System.Drawing.Color]::FromArgb(255, 79, 195, 247),
        [System.Drawing.Color]::FromArgb(255, 129, 199, 132),
        [System.Drawing.Color]::FromArgb(255, 206, 147, 216))
    $heights = 0.46, 0.68, 0.56
    $barW = $s * 0.17
    $gap  = $s * 0.075
    $total = 3 * $barW + 2 * $gap
    $x0 = ($s - $total) / 2
    $base = $s * 0.78
    for ($i = 0; $i -lt 3; $i++) {
        $h = $s * $heights[$i]
        $x = $x0 + $i * ($barW + $gap)
        $path = New-RoundedPath $x ($base - $h) $barW $h ($barW * 0.42)
        $brush = New-Object System.Drawing.SolidBrush $colors[$i]
        $g.FillPath($brush, $path)
        $brush.Dispose(); $path.Dispose()
    }

    # linha de base discreta (some nos tamanhos muito pequenos)
    if ($s -ge 32) {
        $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(70, 255, 255, 255)), ([Math]::Max(1, $s * 0.02))
        $pen.StartCap = 'Round'; $pen.EndCap = 'Round'
        $g.DrawLine($pen, $x0 - $s * 0.02, $base + $s * 0.055, $x0 + $total + $s * 0.02, $base + $s * 0.055)
        $pen.Dispose()
    }

    $g.Dispose(); $bg.Dispose(); $bgPath.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return , $ms.ToArray()
}

# monta o .ico (entradas PNG)
$pngs = foreach ($s in $sizes) { , (New-IconPng $s) }
$fs = [System.IO.File]::Create($out)
$bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]
    $wb = if ($s -ge 256) { 0 } else { $s }
    $bw.Write([byte]$wb); $bw.Write([byte]$wb); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32)
    $bw.Write([uint32]$pngs[$i].Length); $bw.Write([uint32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $bw.Write($p) }
$bw.Close(); $fs.Close()
"Gerado: $out ($((Get-Item $out).Length) bytes)"
