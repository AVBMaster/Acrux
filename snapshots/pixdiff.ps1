param()
Add-Type -AssemblyName System.Drawing
function PD($a,$b) {
  if (-not ((Test-Path $a) -and (Test-Path $b))) { return "missing" }
  $ia=[System.Drawing.Bitmap]::FromFile($a)
  $ib=[System.Drawing.Bitmap]::FromFile($b)
  if ($ia.Width -ne $ib.Width -or $ia.Height -ne $ib.Height) { return ("SIZE {0}x{1} vs {2}x{3}" -f $ia.Width,$ia.Height,$ib.Width,$ib.Height) }
  $n = 0
  $f = @()
  for ($y=0; $y -lt $ia.Height; $y++) {
    for ($x=0; $x -lt $ia.Width; $x++) {
      $p1 = $ia.GetPixel($x,$y)
      $p2 = $ib.GetPixel($x,$y)
      if (([Math]::Abs($p1.R-$p2.R) + [Math]::Abs($p1.G-$p2.G) + [Math]::Abs($p1.B-$p2.B)) -gt 12) {
        $n++
        if ($f.Count -lt 4) { $f += ("{0},{1}" -f $x,$y) }
      }
    }
  }
  $ia.Dispose()
  $ib.Dispose()
  return ("diff={0} first={1}" -f $n, ($f -join " "))
}
foreach ($i in $args) {
  $old = "snapshots/out/ref-b$i.png"
  if ($i -eq "4") { $old = "snapshots/out/reg-b4-revert.png" }
  Write-Output ("b$i : " + (PD $old ("snapshots/out/new-b$i.png")))
}
