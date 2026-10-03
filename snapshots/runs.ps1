param([string]$Src, [int]$Y)
Add-Type -AssemblyName System.Drawing
$bmp = [System.Drawing.Bitmap]::new($Src)
function Name($c) {
    $r = [int]$c.R; $g = [int]$c.G; $b = [int]$c.B
    if ($r -gt 240 -and $g -gt 240 -and $b -lt 60) { return "yellow" }
    if ($r -gt 200 -and $g -lt 90 -and $b -lt 90) { return "red" }
    if ($r -lt 40 -and $g -lt 40 -and $b -lt 40) { return "black" }
    if ($r -gt 245 -and $g -gt 245 -and $b -gt 245) { return "white" }
    if ([Math]::Abs($r-221) -lt 8 -and [Math]::Abs($g-221) -lt 8 -and [Math]::Abs($b-221) -lt 8) { return "gray" }
    return "other($r,$g,$b)"
}
$prev = ""
$start = 0
for ($x = 0; $x -lt $bmp.Width; $x++) {
    $n = Name $bmp.GetPixel($x, $Y)
    if ($n -ne $prev) {
        if ($prev -ne "") { "{0}: {1}..{2} ({3})" -f $prev, $start, ($x-1), ($x-$start) }
        $prev = $n; $start = $x
    }
}
"{0}: {1}..{2} ({3})" -f $prev, $start, ($bmp.Width-1), ($bmp.Width-$start)
"size {0}x{1}" -f $bmp.Width, $bmp.Height
$bmp.Dispose()
