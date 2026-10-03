param([int]$N)
Add-Type -AssemblyName System.Drawing
$a=[System.Drawing.Bitmap]::FromFile("$PSScriptRoot/out/ref-b$N.png")
$b=[System.Drawing.Bitmap]::FromFile("$PSScriptRoot/out/new-b$N.png")
$by=@{}
for($y=0;$y -lt [Math]::Min($a.Height,$b.Height);$y++){
  for($x=0;$x -lt [Math]::Min($a.Width,$b.Width);$x++){
    $pa=$a.GetPixel($x,$y); $pb=$b.GetPixel($x,$y)
    if($pa.R -ne $pb.R -or $pa.G -ne $pb.G -or $pa.B -ne $pb.B){
      if(-not $by.ContainsKey($y)){$by[$y]=New-Object System.Collections.ArrayList}
      [void]$by[$y].Add($x)
    }
  }
}
foreach($k in ($by.Keys|Sort-Object)){
  $l=$by[$k]
  $min=($l|Measure-Object -Minimum).Minimum
  $max=($l|Measure-Object -Maximum).Maximum
  Write-Host ("row {0}: n={1} x={2}..{3}" -f $k,$l.Count,$min,$max)
}
$a.Dispose();$b.Dispose()
