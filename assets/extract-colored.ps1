# Cuts the coloured part (e.g. brown moustache + pipe) out of a picture: grey lines and white background
# become transparent; result is trimmed, centred on a square canvas and saved as PNG.
# Usage: extract-colored.ps1 <in.jpg|png> <out.png> [-Size 512]
param([Parameter(Mandatory)][string]$In, [Parameter(Mandatory)][string]$Out, [int]$Size = 512)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$src = [System.Drawing.Bitmap]::FromFile((Resolve-Path $In))
$w = $src.Width; $h = $src.Height
$mask = New-Object System.Drawing.Bitmap $w, $h, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$minX = $w; $minY = $h; $maxX = 0; $maxY = 0
for ($y = 0; $y -lt $h; $y++) {
  for ($x = 0; $x -lt $w; $x++) {
    $c = $src.GetPixel($x, $y)
    $max = [Math]::Max($c.R, [Math]::Max($c.G, $c.B)); $min = [Math]::Min($c.R, [Math]::Min($c.G, $c.B))
    $sat = if ($max -eq 0) { 0 } else { ($max - $min) / $max }
    # coloured enough, and not a near-white JPEG tint
    $a = [Math]::Max(0, [Math]::Min(1, ($sat - 0.18) / 0.14)) * [Math]::Max(0, [Math]::Min(1, (245 - $max) / 40))
    if ($a -gt 0.02) {
      $mask.SetPixel($x, $y, [System.Drawing.Color]::FromArgb([int](255 * $a), $c.R, $c.G, $c.B))
      if ($a -gt 0.5) { if ($x -lt $minX) { $minX = $x }; if ($x -gt $maxX) { $maxX = $x }; if ($y -lt $minY) { $minY = $y }; if ($y -gt $maxY) { $maxY = $y } }
    }
  }
}
$src.Dispose()
$bw = $maxX - $minX + 1; $bh = $maxY - $minY + 1
$side = [int]([Math]::Max($bw, $bh) * 1.08)
$outBmp = New-Object System.Drawing.Bitmap $Size, $Size
$g = [System.Drawing.Graphics]::FromImage($outBmp)
$g.InterpolationMode = 'HighQualityBicubic'; $g.SmoothingMode = 'AntiAlias'
$scale = $Size / $side
$dw = $bw * $scale; $dh = $bh * $scale
$g.DrawImage($mask, [System.Drawing.RectangleF]::new(($Size - $dw) / 2, ($Size - $dh) / 2, $dw, $dh), [System.Drawing.RectangleF]::new($minX, $minY, $bw, $bh), 'Pixel')
$g.Dispose(); $mask.Dispose()
$outBmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png); $outBmp.Dispose()
"crop ${bw}x${bh} at ($minX,$minY) -> $Out"
