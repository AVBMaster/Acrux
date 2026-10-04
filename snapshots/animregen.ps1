# Regenerate the CSS animation snapshot suite and diff it against the reference.
#
#   powershell -File snapshots/animregen.ps1                # all batches
#   powershell -File snapshots/animregen.ps1 -Batches 1,2
#
# For each case this writes:
#   snapshots/out/anim-NN.png        the deterministic frame
#   snapshots/out/anim-NN.txt        numeric dump of every animated element
#   snapshots/out/anim-NN-diff.png   difference map against the Edge reference
#
# The viewport per case is sized to the probe grid, so the probe coordinates are
# the declared ones and the reference screenshot lines up pixel for pixel.
param(
  [int[]]$Batches = @(1,2,3,4,5,6,7,8,9,10,11,12,13,14),
  [int]$Tolerance = 0
)

$Cases = @{
  1  = @{ File = "anim-01-keyframes-basics.html";   W = 460; H = 480; T = 0 }
  2  = @{ File = "anim-02-transform.html";           W = 460; H = 520; T = 0 }
  3  = @{ File = "anim-03-easing.html";              W = 460; H = 480; T = 0 }
  4  = @{ File = "anim-04-steps.html";               W = 460; H = 520; T = 0 }
  5  = @{ File = "anim-05-delay-fill.html";          W = 460; H = 480; T = 0 }
  6  = @{ File = "anim-06-iteration.html";           W = 460; H = 520; T = 0 }
  7  = @{ File = "anim-07-color.html";               W = 460; H = 480; T = 0 }
  8  = @{ File = "anim-08-length-layout.html";       W = 460; H = 520; T = 0 }
  9  = @{ File = "anim-09-multi-animation.html";     W = 460; H = 480; T = 0 }
  10 = @{ File = "anim-10-transition.html";          W = 460; H = 520; T = 0 }
  11 = @{ File = "anim-11-shadow-filter.html";        W = 460; H = 480; T = 0 }
  12 = @{ File = "anim-12-events.html";              W = 460; H = 700; T = 2500 }
  13 = @{ File = "anim-13-discrete.html";            W = 460; H = 480; T = 0 }
  14 = @{ File = "anim-14-keyframe-easing.html";     W = 460; H = 480; T = 0 }
}

foreach ($n in $Batches) {
  $c = $Cases[[int]$n]
  if ($null -eq $c) { Write-Output "skip b$n (no case)"; continue }

  $tag = '{0:d2}' -f [int]$n
  $src = "snapshots/$($c.File)"
  $new = "snapshots/out/anim-$tag.png"
  $txt = "snapshots/out/anim-$tag.txt"
  $ref = "snapshots/out/edge-anim-$tag.png"

  if (-not (Test-Path $src)) { Write-Output "skip b$n (missing $src)"; continue }

  # PNG: the deterministic frame at the page's pinned timeline instant.
  dotnet run --project Acrux --no-build -- --snapshot $src $new $c.W $c.H 1 $c.T 2>&1 |
    Where-Object { $_ -match '^\[snapshot\]' } | ForEach-Object { Write-Output $_ }

  # Numeric dump: every animated element's computed values, so the PNG can be
  # cross-checked against exact numbers instead of eyeballed colour.
  dotnet run --project Acrux --no-build -- --anim $src $c.W $c.H $c.T 2>&1 |
    Out-File -Encoding utf8 $txt

  if (Test-Path $ref) {
    dotnet run --project Acrux --no-build -- --diff $ref $new "snapshots/out/anim-$tag-diff.png" $Tolerance 2>&1 |
      Where-Object { $_ -match '^\[diff\]' } | ForEach-Object { Write-Output ("  b$n " + $_) }
  } else {
    Write-Output "  b$n no reference yet (run snapshots/animref.ps1)"
  }
}
