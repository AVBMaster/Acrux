param([string]$Src, [string]$Dst, [int]$X, [int]$Y, [int]$W = 200, [int]$H = 120, [int]$Scale = 3)
Add-Type -AssemblyName System.Drawing
$bmp = [System.Drawing.Bitmap]::FromFile((Resolve-Path $Src))
$x0 = [Math]::Max(0, $X); $y0 = [Math]::Max(0, $Y)
$x1 = [Math]::Min($bmp.Width, $x0 + $W); $y1 = [Math]::Min($bmp.Height, $y0 + $H)
$rw = [int]($x1 - $x0); $rh = [int]($y1 - $y0)
$rect = [System.Drawing.Rectangle]::new(0, 0, $rw, $rh)
$cropped = $bmp.Clone([System.Drawing.Rectangle]::new($x0, $y0, $rw, $rh), [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$out = [System.Drawing.Bitmap]::new($rw * [int]$Scale, $rh * [int]$Scale)
$g = [System.Drawing.Graphics]::FromImage($out)
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
$g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half
$g.DrawImage($cropped, [System.Drawing.Rectangle]::new(0, 0, $out.Width, $out.Height))
$g.Dispose()
$out.Save($Dst, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose(); $cropped.Dispose(); $out.Dispose()
Write-Output ("wrote " + $Dst)
