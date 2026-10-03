Add-Type -AssemblyName System.Drawing
function PD($a,$b) {
  $ia=[System.Drawing.Bitmap]::FromFile($a); $ib=[System.Drawing.Bitmap]::FromFile($b)
  $n=0; for($y=0;$y -lt $ia.Height;$y++){ for($x=0;$x -lt $ia.Width;$x++){
    $p=$ia.GetPixel($x,$y); $q=$ib.GetPixel($x,$y)
    if([Math]::Abs($p.R-$q.R)+[Math]::Abs($p.G-$q.G)+[Math]::Abs($p.B-$q.B) -gt 12){$n++} } }
  $ia.Dispose(); $ib.Dispose(); return $n
}
$f = @($args)
for($i=0;$i -lt $f.Count;$i++){ for($j=$i+1;$j -lt $f.Count;$j++){ Write-Output ("$($f[$i]) vs $($f[$j]) : " + (PD $f[$i] $f[$j])) } }
