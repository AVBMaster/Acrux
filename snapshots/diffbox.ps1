param([int]$N)
Add-Type -AssemblyName System.Drawing
$a = [System.Drawing.Bitmap]::FromFile((Resolve-Path "snapshots/out/ref-b$N.png"))
$b = [System.Drawing.Bitmap]::FromFile((Resolve-Path "snapshots/out/new-b$N.png"))
$minx = 1e9; $miny = 1e9; $maxx = -1; $maxy = -1; $n = 0
for ($y = 0; $y -lt $a.Height; $y++) {
  for ($x = 0; $x -lt $a.Width; $x++) {
    $p1 = $a.GetPixel($x,$y); $p2 = $b.GetPixel($x,$y)
    if (([Math]::Abs($p1.R-$p2.R) + [Math]::Abs($p1.G-$p2.G) + [Math]::Abs($p1.B-$p2.B)) -gt 12) {
      $n++
      if ($x -lt $minx) { $minx = $x }; if ($x -gt $maxx) { $maxx = $x }
      if ($y -lt $miny) { $miny = $y }; if ($y -gt $maxy) { $maxy = $y }
    }
  }
}
Write-Output ("b$N diff=$n box=[$minx,$miny - $maxx,$maxy]")
$cy = [int](($miny + $maxy) / 2)
for ($x = $minx; $x -le [Math]::Min($maxx, $minx + 12); $x += 2) {
  $p1 = $a.GetPixel($x,$cy); $p2 = $b.GetPixel($x,$cy)
  Write-Output ("x=$x y=$cy ref=(" + $p1.R + "," + $p1.G + "," + $p1.B + ") new=(" + $p2.R + "," + $p2.G + "," + $p2.B + ")")
}
$a.Dispose(); $b.Dispose()
