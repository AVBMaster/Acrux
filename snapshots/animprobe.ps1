# Cross-check the probe grid of an animation test page against the reference.
#
#   powershell -File snapshots/animprobe.ps1 -Batch 4
#   powershell -File snapshots/animprobe.ps1 -Batch 4 -Skip 3
#
# Each page lays its swatches out on an absolute grid with a fixed pitch, so the
# probe coordinates are the declared ones and the same --pixels read applies to
# both renderings. Text antialiasing is deliberately avoided: every probe sits
# near a swatch corner, away from the label.
#
# The expected fill for each swatch is listed in the page's own legend, so a
# mismatch here means the engine and the reference disagree about the animation,
# not about font rendering.
param(
  [Parameter(Mandatory = $true)][int]$Batch,
  [int[]]$Skip = @(),
  [int]$ColPitch = 110,
  [int]$RowPitch = 80,
  [int]$OriginX = 20,
  [int]$OriginY = 15,
  [int]$Count = 0,
  # 'point' reads one colour per swatch (right for paint-only properties).
  # 'extent' reads the bounding box of the painted swatch (right for properties
  # that move or resize the box, which a single fixed point cannot distinguish).
  [ValidateSet('point', 'extent')][string]$Mode = 'point',
  [int]$CellW = 90,
  [int]$CellH = 60,
  # Skia composites an opacity layer through an 8-bit alpha, so a result that
  # lands on a .5 boundary (opacity 0.5, 0.25, ...) can differ from a float-alpha
  # compositor by exactly one level. That is a quantisation artefact, not a
  # disagreement about the animation, so a delta of 1 is reported separately.
  [int]$Quantise = 1
)

$tag = '{0:d2}' -f $Batch
$ours = "snapshots/out/anim-$tag.png"
$ref  = "snapshots/out/edge-anim-$tag.png"

if (-not (Test-Path $ours)) { Write-Output "missing $ours (run animregen.ps1)"; exit 1 }
if (-not (Test-Path $ref))  { Write-Output "missing $ref (run animref.ps1)";  exit 1 }

# The grid is read from the page's own --anim dump so the count is never guessed.
$txt = "snapshots/out/anim-$tag.txt"
if (-not (Test-Path $txt)) { Write-Output "missing $txt (run animregen.ps1)"; exit 1 }
if ($Count -le 0) {
  $Count = (Select-String -Path $txt -Pattern '^\[el\]').Count
}

$pass = 0; $near = 0; $fail = 0

# Bounding box of the non-white pixels in a window, as "L,T R,B".
function BoundingBox($image, $x0, $y0, $x1, $y1) {
  $lines = dotnet run --project UpBrowser --no-build -- --rows $image $x0 $x1 $y0 $y1 2>&1
  $minX = 99999; $maxX = -1; $minY = 99999; $maxY = -1
  foreach ($l in $lines) {
    if ($l -match '^\[row\] y=(-?\d+) first=(-?\d+) last=(-?\d+)') {
      $ry = [int]$Matches[1]; $rf = [int]$Matches[2]; $rl = [int]$Matches[3]
      if ($rf -lt 0) { continue }
      if ($rf -lt $minX) { $minX = $rf }
      if ($rl -gt $maxX) { $maxX = $rl }
      if ($ry -lt $minY) { $minY = $ry }
      if ($ry -gt $maxY) { $maxY = $ry }
    }
  }
  if ($maxX -lt 0) { return "empty" }
  return "$minX,$minY - $maxX,$maxY"
}
for ($i = 1; $i -le $Count; $i++) {
  if ($Skip -contains $i) { continue }
  $col = ($i - 1) % 4
  $row = [Math]::Floor(($i - 1) / 4)
  $x = $OriginX + $ColPitch * $col
  $y = $OriginY + $RowPitch * $row

  if ($Mode -eq 'extent') {
    # Bounding box of the non-white pixels inside the swatch's cell. A box that
    # moved or resized is only visible in its extent, not in one fixed sample.
    # The window is the swatch's own grid cell widened just enough to catch a box
    # that moved, and capped short of the next cell so a neighbour's edge — or a
    # box that legitimately moved into the gap — cannot be mistaken for ours.
    $x0 = $OriginX - 8 + $ColPitch * $col
    $y0 = $OriginY - 10 + $RowPitch * $row
    $x1 = $x0 + $ColPitch - 4
    $y1 = $y0 + $CellH + 20
    $a = BoundingBox $ours  $x0 $y0 $x1 $y1
    $b = BoundingBox $ref   $x0 $y0 $x1 $y1
    if ($a -eq $b -and $a -ne '') {
      $pass++; Write-Output ("  swatch {0,2}  extent {1}  MATCH" -f $i, $a)
    } else {
      $fail++; Write-Output ("  swatch {0,2}  ours={1}  ref={2}  ** DIFF **" -f $i, $a, $b)
    }
    continue
  }

  $a = (dotnet run --project UpBrowser --no-build -- --pixels $ours $y $x $x 2>&1) -join ''
  $b = (dotnet run --project UpBrowser --no-build -- --pixels $ref  $y $x $x 2>&1) -join ''
  $ra = ($a -replace '.*rgb=', '')
  $rb = ($b -replace '.*rgb=', '')

  if ($ra -eq $rb -and $ra -ne '') {
    $pass++; Write-Output ("  swatch {0,2} ({1,3},{2,3})  rgb{3}  MATCH" -f $i, $x, $y, $ra)
    continue
  }

  $pa = [int[]](($ra -replace '[()]','') -split ',')
  $pb = [int[]](($rb -replace '[()]','') -split ',')
  $delta = 0
  if ($pa.Count -eq 3 -and $pb.Count -eq 3) {
    for ($c = 0; $c -lt 3; $c++) { $delta = [Math]::Max($delta, [Math]::Abs($pa[$c] - $pb[$c])) }
  } else { $delta = 255 }

  if ($delta -le $Quantise) {
    $near++; Write-Output ("  swatch {0,2} ({1,3},{2,3})  ours={3}  ref={4}  ={5} (8-bit alpha quantisation)" -f $i, $x, $y, $ra, $rb, $delta)
  } else {
    $fail++; Write-Output ("  swatch {0,2} ({1,3},{2,3})  ours={3}  ref={4}  d={5}  ** DIFF **" -f $i, $x, $y, $ra, $rb, $delta)
  }
}
Write-Output ("b${tag}: $pass exact, $near within +/-${Quantise}, $fail differ")

# Batch 12 is the events page: its animations run on a live clock, so the
# reference's own capture instant decides what the grid shows. The reference is
# captured under --virtual-time-budget, which stops the document timeline at
# whatever point its budget ran out, not at the instant this page was captured
# at. Its grid is therefore informational, and the assertion for that batch is
# the event log in the page's --anim dump, not the colours.
if ($tag -eq '12') {
  Write-Output "      (batch 12 runs on a live clock: the grid is informational, the event log is the assertion)"
}
