param([string]$A, [string]$B, [int]$Y0, [int]$Y1)
Add-Type -AssemblyName System.Drawing
foreach ($f in @($A, $B)) {
  $img = [System.Drawing.Bitmap]::FromFile((Resolve-Path $f))
  $min = 999999; $max = -1
  for ($y = $Y0; $y -le $Y1; $y++) {
    for ($x = 0; $x -lt $img.Width; $x++) {
      $p = $img.GetPixel($x, $y)
      if (($p.R + $p.G + $p.B) -lt 700) { if ($x -lt $min) { $min = $x }; if ($x -gt $max) { $max = $x } }
    }
  }
  $img.Dispose()
  Write-Output ("{0}: x {1}..{2}" -f $f, $min, $max)
}
