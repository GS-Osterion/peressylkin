# Generates assets\sibir.ico (moustache + pipe) and assets\stamp.ico (red stamp) with System.Drawing.
# assets\custom-sibir.png / custom-stamp.png, if present, replace the drawings. Skips work if icons are newer than this script.
param([switch]$Force)
$ErrorActionPreference = 'Stop'
$dir = $PSScriptRoot
$targets = @('sibir', 'stamp')
$fresh = $targets | ForEach-Object { $ico = Join-Path $dir "$_.ico"; (Test-Path $ico) -and (Get-Item $ico).LastWriteTime -gt (Get-Item $PSCommandPath).LastWriteTime -and -not (Test-Path (Join-Path $dir "custom-$_.png")) }
if (-not $Force -and -not ($fresh -contains $false)) { return }
Add-Type -AssemblyName System.Drawing

function New-Frame([string]$kind, [int]$s) {
  $bmp = New-Object System.Drawing.Bitmap $s, $s
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.SmoothingMode = 'AntiAlias'; $g.TextRenderingHint = 'AntiAliasGridFit'
  $custom = Join-Path $dir "custom-$kind.png"
  if (Test-Path $custom) { $img = [System.Drawing.Image]::FromFile($custom); $g.DrawImage($img, 0, 0, $s, $s); $img.Dispose(); $g.Dispose(); return $bmp }
  $k = $s / 256.0
  if ($kind -eq 'sibir') {
    $g.FillEllipse((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(236, 226, 200))), 4*$k, 4*$k, 248*$k, 248*$k)
    $g.DrawEllipse((New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(150, 20, 20)), (10*$k)), 9*$k, 9*$k, 238*$k, 238*$k)
    $black = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(30, 25, 20))
    foreach ($side in -1, 1) {   # moustache: two drooping lobes
      $p = New-Object System.Drawing.Drawing2D.GraphicsPath
      $cx = 128*$k
      $pts = @(
        [System.Drawing.PointF]::new($cx, 104*$k),
        [System.Drawing.PointF]::new($cx + $side*40*$k, 84*$k), [System.Drawing.PointF]::new($cx + $side*92*$k, 96*$k), [System.Drawing.PointF]::new($cx + $side*104*$k, 150*$k),
        [System.Drawing.PointF]::new($cx + $side*78*$k, 128*$k), [System.Drawing.PointF]::new($cx + $side*40*$k, 128*$k), [System.Drawing.PointF]::new($cx, 128*$k))
      $p.AddBeziers([System.Drawing.PointF[]]$pts); $p.CloseFigure(); $g.FillPath($black, $p)
    }
    $brown = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(110, 60, 25))
    $stem = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(60, 35, 15)), (12*$k)
    $stem.StartCap = 'Round'; $stem.EndCap = 'Round'
    $g.DrawLine($stem, 150*$k, 140*$k, 196*$k, 168*$k)                     # pipe stem
    $g.FillPie($brown, 182*$k, 140*$k, 46*$k, 70*$k, 0, 180)                 # pipe bowl
    $g.FillRectangle($brown, 182*$k, 160*$k, 46*$k, 16*$k)
    if ($s -ge 32) {
      $smoke = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(140, 120, 120, 120)), (6*$k)
      $g.DrawBezier($smoke, 205*$k, 150*$k, 190*$k, 120*$k, 225*$k, 105*$k, 205*$k, 70*$k)
    }
  } else {
    $red = [System.Drawing.Color]::FromArgb(200, 25, 30)
    $pen = New-Object System.Drawing.Pen $red, ([Math]::Max(1.5, 14*$k))
    $g.DrawEllipse($pen, 10*$k, 10*$k, 236*$k, 236*$k)
    if ($s -ge 32) { $pen2 = New-Object System.Drawing.Pen $red, ([Math]::Max(1, 6*$k)); $g.DrawEllipse($pen2, 34*$k, 34*$k, 188*$k, 188*$k) }
    $star = New-Object System.Drawing.Drawing2D.GraphicsPath
    $c = 128*$k; $cy = if ($s -ge 48) { 92*$k } else { 128*$k }; $big = if ($s -ge 48) { 44*$k } else { 70*$k }
    $pts = for ($i = 0; $i -lt 10; $i++) { $r = if ($i % 2) { $big * 0.4 } else { $big }; $a = -[Math]::PI/2 + $i*[Math]::PI/5; [System.Drawing.PointF]::new($c + $r*[Math]::Cos($a), $cy + $r*[Math]::Sin($a)) }
    $star.AddPolygon([System.Drawing.PointF[]]$pts); $g.FillPath((New-Object System.Drawing.SolidBrush $red), $star)
    if ($s -ge 48) {
      $font = New-Object System.Drawing.Font 'Arial', (30*$k), ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)
      $fmt = New-Object System.Drawing.StringFormat; $fmt.Alignment = 'Center'; $fmt.LineAlignment = 'Center'
      $g.DrawString('СОСЛАН', $font, (New-Object System.Drawing.SolidBrush $red), [System.Drawing.RectangleF]::new(30*$k, 140*$k, 196*$k, 56*$k), $fmt)
    }
  }
  $g.Dispose(); $bmp
}

foreach ($kind in $targets) {
  $sizes = 16, 32, 48, 256
  $pngs = foreach ($s in $sizes) { $b = New-Frame $kind $s; $ms = New-Object System.IO.MemoryStream; $b.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png); $b.Dispose(); , $ms.ToArray() }
  $out = New-Object System.IO.MemoryStream; $w = New-Object System.IO.BinaryWriter $out
  $w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
  $offset = 6 + 16 * $sizes.Count
  for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $d = if ($s -ge 256) { 0 } else { $s }
    $w.Write([byte]$d); $w.Write([byte]$d); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]$pngs[$i].Length); $w.Write([uint32]$offset)
    $offset += $pngs[$i].Length
  }
  foreach ($p in $pngs) { $w.Write($p) }
  [System.IO.File]::WriteAllBytes((Join-Path $dir "$kind.ico"), $out.ToArray())
  [System.IO.File]::WriteAllBytes((Join-Path $dir "$kind-256.png"), $pngs[-1])
}
