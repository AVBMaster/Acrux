param([string]$Path = "snapshots/out/ref-b4.png")
Add-Type -AssemblyName System.Drawing
$i = [System.Drawing.Bitmap]::FromFile((Resolve-Path $Path))
Write-Output ("{0}: {1}x{2}" -f $Path, $i.Width, $i.Height)
$i.Dispose()
