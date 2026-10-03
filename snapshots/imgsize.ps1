param()
Add-Type -AssemblyName System.Drawing
foreach ($n in 4,5,6,7,8,9,10,11,12,13) {
  $f = "snapshots/out/ref-b$n.png"
  if (Test-Path $f) {
    $i = [System.Drawing.Bitmap]::FromFile((Resolve-Path $f))
    Write-Output ("b{0}: {1}x{2}" -f $n, $i.Width, $i.Height)
    $i.Dispose()
  } else {
    Write-Output ("b{0}: missing" -f $n)
  }
}
