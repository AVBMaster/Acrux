param(
    [Parameter(Mandatory = $true)][string]$Png,
    [Parameter(Mandatory = $true)][int]$Y,
    [int]$X0 = -1,
    [int]$X1 = -1,
    [switch]$V,
    [int]$Col = 0
)
# Scan a horizontal (default) or vertical (-V, using -Col as the x) line of a PNG
# and print the colour runs, so painted fragment edges can be read off directly.
Add-Type -AssemblyName System.Drawing
$bmp = New-Object System.Drawing.Bitmap($Png)
if ($X0 -lt 0) { $X0 = 0 }
if ($X1 -lt 0) { $X1 = $bmp.Width - 1 }
$end = if ($V) { $bmp.Height - 1 } else { $bmp.Width - 1 }
if ($X1 -gt $end) { $X1 = $end }
$prev = $null
$runStart = $X0
for ($i = $X0; $i -le $X1; $i++) {
    if ($V) { $c = $bmp.GetPixel($Col, $i) } else { $c = $bmp.GetPixel($i, $Y) }
    $key = '{0},{1},{2}' -f $c.R, $c.G, $c.B
    if ($null -ne $prev -and $key -ne $prev) {
        '{0}-{1}: {2}' -f $runStart, ($i - 1), $prev
        $runStart = $i
    }
    $prev = $key
}
'{0}-{1}: {2}' -f $runStart, $X1, $prev
$bmp.Dispose()
