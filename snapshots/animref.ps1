# Capture the reference rendering of each CSS animation test page with Edge.
#
#   powershell -File snapshots/animref.ps1              # all cases
#   powershell -File snapshots/animref.ps1 -Batches 1,2
#
# The reference has to be taken at exactly the instant the Acrux capture
# pins itself to, which is why every probe in these pages is a paused animation
# with a negative delay: that is a fixed frame in every engine, so a screenshot
# taken at any wall-clock moment shows the same pixels.
param([int[]]$Batches = @(1,2,3,4,5,6,7,8,9,10,11,12,13,14))

$Edge = "C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe"
if (-not (Test-Path $Edge)) {
  $Edge = "C:\Program Files\Microsoft\Edge\Application\msedge.exe"
}
if (-not (Test-Path $Edge)) { Write-Output "edge not found"; exit 1 }

$Profile = Join-Path $env:TEMP "edge-anim-profile"

$Cases = @{
  1  = @{ File = "anim-01-keyframes-basics.html";   W = 460; H = 480 }
  2  = @{ File = "anim-02-transform.html";           W = 460; H = 520 }
  3  = @{ File = "anim-03-easing.html";              W = 460; H = 480 }
  4  = @{ File = "anim-04-steps.html";               W = 460; H = 520 }
  5  = @{ File = "anim-05-delay-fill.html";          W = 460; H = 480 }
  6  = @{ File = "anim-06-iteration.html";           W = 460; H = 520 }
  7  = @{ File = "anim-07-color.html";               W = 460; H = 480 }
  8  = @{ File = "anim-08-length-layout.html";       W = 460; H = 520 }
  9  = @{ File = "anim-09-multi-animation.html";     W = 460; H = 480 }
  10 = @{ File = "anim-10-transition.html";          W = 460; H = 520 }
  11 = @{ File = "anim-11-shadow-filter.html";        W = 460; H = 480 }
  12 = @{ File = "anim-12-events.html";              W = 460; H = 700 }
  13 = @{ File = "anim-13-discrete.html";            W = 460; H = 480 }
  14 = @{ File = "anim-14-keyframe-easing.html";     W = 460; H = 480 }
}

foreach ($n in $Batches) {
  $c = $Cases[[int]$n]
  if ($null -eq $c) { Write-Output "skip b$n (no case)"; continue }
  $src = "snapshots/$($c.File)"
  if (-not (Test-Path $src)) { Write-Output "skip b$n (missing $src)"; continue }

  $tag = '{0:d2}' -f [int]$n
  # Edge needs an absolute screenshot path; a relative one silently fails.
  $out = Join-Path (Resolve-Path "snapshots/out").Path "edge-anim-$tag.png"
  if (Test-Path $out) { Remove-Item $out -Force }
  $url = "file:///" + ((Resolve-Path $src).Path -replace '\\', '/')

  & $Edge --headless=new --no-sandbox --disable-gpu --user-data-dir="$Profile" `
      --hide-scrollbars --force-device-scale-factor=1 `
      --virtual-time-budget=2000 `
      --window-size="$($c.W),$($c.H)" --screenshot="$out" $url | Out-Null

  # The headless shell returns before the PNG is flushed, so poll for it.
  for ($i = 0; $i -lt 40 -and -not (Test-Path $out); $i++) { Start-Sleep -Milliseconds 250 }

  if (Test-Path $out) { Write-Output ("edge-anim-$tag.png  <-  " + $c.File) }
  else { Write-Output ("FAILED: " + $out) }
}
