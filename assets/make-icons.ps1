# Generates assets\sibir.ico (moustache + pipe) and assets\stamp.ico (red seal) with System.Drawing.
# assets\custom-sibir.png / custom-stamp.png, if present, replace the drawings.
# Skips work unless -Force, or the script / a custom PNG is newer than the icon.
param([switch]$Force)
$ErrorActionPreference = 'Stop'
$dir = $PSScriptRoot
$targets = @('sibir', 'stamp')
function Stale([string]$kind) {
  $ico = Join-Path $dir "$kind.ico"; $custom = Join-Path $dir "custom-$kind.png"
  if (-not (Test-Path $ico)) { return $true }
  $t = (Get-Item $ico).LastWriteTime
  return ((Get-Item $PSCommandPath).LastWriteTime -gt $t) -or ((Test-Path $custom) -and (Get-Item $custom).LastWriteTime -gt $t)
}
if (-not $Force -and -not ($targets | Where-Object { Stale $_ })) { return }
Add-Type -AssemblyName System.Drawing

# Composition follows the user's sketch (assets\user-sibir-256.png): bent pipe lower-left, mouthpiece under a bushy moustache.
function Draw-Sibir($g, [double]$k) {
  function P([double]$x, [double]$y) { [System.Drawing.PointF]::new($x * $k, $y * $k) }
  $C = { param($a, $r, $gg, $b) [System.Drawing.Color]::FromArgb($a, $r, $gg, $b) }
  $detail = $k -ge 0.18   # 48px and up

  # --- pipe: stem first, then bowl, so the mouthpiece disappears under the moustache ---
  $stem = New-Object System.Drawing.Pen (& $C 255 72 38 16), (14*$k); $stem.StartCap = 'Round'; $stem.EndCap = 'Round'
  $g.DrawBezier($stem, (P 88 206), (P 116 204), (P 122 176), (P 121 150))
  $bit = New-Object System.Drawing.Pen (& $C 255 22 20 20), (11*$k); $bit.StartCap = 'Round'; $bit.EndCap = 'Round'
  $g.DrawLine($bit, (P 121 152), (P 123 126))
  if ($detail) {
    $brass = New-Object System.Drawing.Pen (& $C 255 214 170 60), (5*$k)
    $g.DrawLine($brass, (P 115 162), (P 128 160))
  }
  $bowl = New-Object System.Drawing.Drawing2D.GraphicsPath
  $bowl.AddLine((P 30 170), (P 100 170))
  $bowl.AddBezier((P 100 170), (P 103 202), (P 96 230), (P 66 236))
  $bowl.AddBezier((P 66 236), (P 38 232), (P 26 206), (P 30 170))
  $bowl.CloseFigure()
  $wood = New-Object System.Drawing.Drawing2D.LinearGradientBrush (P 28 0), (P 104 0), (& $C 255 150 86 42), (& $C 255 74 38 16)
  $g.FillPath($wood, $bowl)
  if ($detail) {   # grain + highlight
    $grain = New-Object System.Drawing.Pen (& $C 70 50 25 10), (2*$k)
    foreach ($y in 186, 200, 214) { $g.DrawBezier($grain, (P 36 $y), (P 55 ($y + 5)), (P 78 ($y - 4)), (P 96 ($y + 2))) }
    $shine = New-Object System.Drawing.Pen (& $C 110 255 235 200), (5*$k); $shine.StartCap = 'Round'; $shine.EndCap = 'Round'
    $g.DrawBezier($shine, (P 40 182), (P 38 200), (P 42 216), (P 52 226))
  }
  $g.FillEllipse((New-Object System.Drawing.SolidBrush (& $C 255 104 58 26)), 28*$k, 160*$k, 74*$k, 22*$k)   # rim
  $g.FillEllipse((New-Object System.Drawing.SolidBrush (& $C 255 34 18 10)), 36*$k, 164*$k, 58*$k, 14*$k)    # chamber
  if ($detail) { $g.FillEllipse((New-Object System.Drawing.SolidBrush (& $C 150 150 70 30)), 52*$k, 167*$k, 22*$k, 6*$k) }  # ember glow

  # --- moustache: wide, heavy, drooping tips, hair-tip fringe at the bottom ---
  $dark = New-Object System.Drawing.SolidBrush (& $C 255 26 22 20)
  foreach ($side in -1, 1) {
    function M([double]$dx, [double]$y) { [System.Drawing.PointF]::new((128 + $side * $dx) * $k, $y * $k) }
    $m = New-Object System.Drawing.Drawing2D.GraphicsPath
    $m.AddBezier((M -4 98), (M 18 84), (M 58 78), (M 90 90))          # top edge (overlaps centre: no seam)
    $m.AddBezier((M 90 90), (M 108 98), (M 118 120), (M 114 148))     # heavy cheek, drooping end
    $m.AddBezier((M 114 148), (M 106 146), (M 98 140), (M 92 132))    # rounded tip
    $m.AddBezier((M 92 132), (M 68 122), (M 34 130), (M -4 124))      # lower edge back past centre
    $m.CloseFigure()
    $g.FillPath($dark, $m)
    if ($detail) {
      # soft wavy lower edge instead of teeth
      foreach ($dx in 6, 20, 34, 48, 62, 76, 88) { $y = 124 + [Math]::Abs($dx - 40) * 0.04 + (($dx / 14) % 2) * 2; $g.FillEllipse($dark, (128 + $side * $dx - 8) * $k, ($y - 6) * $k, 16 * $k, 8 * $k) }
      $rnd = New-Object System.Random (11 + $side)
      $hair = New-Object System.Drawing.Pen (& $C 55 170 160 150), (1.3*$k)
      for ($i = 0; $i -lt 16; $i++) {   # strokes combed outward and down, varied
        $x0 = 4 + $i * 5.8 + $rnd.Next(3); $len = 18 + $rnd.Next(14)
        $g.DrawBezier($hair, (M $x0 (92 + $i * 0.4)), (M ($x0 + 5) (100 + $i * 0.3)), (M ($x0 + 9) (106 + $len * 0.4)), (M ($x0 + 12 + $i * 0.5) (100 + $len)))
      }
    }
  }
}

function Draw-Stamp($g, [int]$s) {
  $k = $s / 256.0
  $red = [System.Drawing.Color]::FromArgb(200, 25, 30)
  $brush = New-Object System.Drawing.SolidBrush $red
  $g.DrawEllipse((New-Object System.Drawing.Pen $red, ([Math]::Max(1.5, 12*$k))), 8*$k, 8*$k, 240*$k, 240*$k)
  if ($s -ge 48) {
    $g.DrawEllipse((New-Object System.Drawing.Pen $red, (5*$k)), 56*$k, 56*$k, 144*$k, 144*$k)
    # Text around the rim, like a real seal: each glyph rotated to the circle tangent.
    $text = 'СОСЛАН • В СИБИРЬ • СОСЛАН • В СИБИРЬ • '
    $font = New-Object System.Drawing.Font 'Arial', (24*$k), ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)
    $fmt = New-Object System.Drawing.StringFormat; $fmt.Alignment = 'Center'; $fmt.LineAlignment = 'Center'
    $radius = 92*$k; $step = 360.0 / $text.Length
    for ($i = 0; $i -lt $text.Length; $i++) {
      $g.ResetTransform()
      $g.TranslateTransform(128*$k, 128*$k)
      $g.RotateTransform($i * $step)
      $g.DrawString([string]$text[$i], $font, $brush, [System.Drawing.PointF]::new(0, -$radius), $fmt)
    }
    $g.ResetTransform()
  }
  $big = if ($s -ge 48) { 62*$k } else { 86*$k }
  $pts = for ($i = 0; $i -lt 10; $i++) {
    $rad = if ($i % 2) { $big * 0.42 } else { $big }
    $a = -[Math]::PI / 2 + $i * [Math]::PI / 5
    [System.Drawing.PointF]::new(128*$k + $rad*[Math]::Cos($a), 132*$k + $rad*[Math]::Sin($a))
  }
  $star = New-Object System.Drawing.Drawing2D.GraphicsPath
  $star.AddPolygon([System.Drawing.PointF[]]$pts); $g.FillPath($brush, $star)
}

function New-Frame([string]$kind, [int]$s) {
  $bmp = New-Object System.Drawing.Bitmap $s, $s
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.SmoothingMode = 'AntiAlias'; $g.TextRenderingHint = 'AntiAliasGridFit'; $g.InterpolationMode = 'HighQualityBicubic'
  $custom = Join-Path $dir "custom-$kind.png"
  if (Test-Path $custom) { $img = [System.Drawing.Image]::FromFile($custom); $g.DrawImage($img, 0, 0, $s, $s); $img.Dispose() }
  elseif ($kind -eq 'sibir') { Draw-Sibir $g ($s / 256.0) }
  else { Draw-Stamp $g $s }
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
