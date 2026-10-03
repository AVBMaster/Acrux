Add-Type -AssemblyName System.Drawing
foreach ($n in $args) {
  $p = "snapshots/out/ref-b$n.png"
  if (Test-Path $p) {
    $i = [System.Drawing.Bitmap]::FromFile((Resolve-Path $p))
    Write-Output ("b$n " + $i.Width + "x" + $i.Height)
    $i.Dispose()
  } else {
    Write-Output "b$n missing"
  }
}
