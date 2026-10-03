param([string]$A, [string]$B, [int]$Step = 25)
Add-Type -AssemblyName System.Drawing
$ia = [System.Drawing.Bitmap]::FromFile((Resolve-Path $A))
$ib = [System.Drawing.Bitmap]::FromFile((Resolve-Path $B))
$buckets = @{}
for ($y = 0; $y -lt [Math]::Min($ia.Height, $ib.Height); $y++) {
  $c = 0
  for ($x = 0; $x -lt [Math]::Min($ia.Width, $ib.Width); $x++) {
    $p1 = $ia.GetPixel($x, $y); $p2 = $ib.GetPixel($x, $y)
    if (([Math]::Abs($p1.R - $p2.R) + [Math]::Abs($p1.G - $p2.G) + [Math]::Abs($p1.B - $p2.B)) -gt 12) { $c++ }
  }
  if ($c -gt 0) {
    $k = [int][Math]::Floor($y / $Step) * $Step
    $buckets["$k"] = $buckets["$k"] + $c
  }
}
foreach ($k in ($buckets.Keys | Sort-Object { [int]$_ })) {
  Write-Output ("y {0}-{1}: {2}" -f $k, ([int]$k + $Step - 1), $buckets[$k])
}
