"""Bounding box of one paint colour, per horizontal band, in the current render.

Tiling and position checks need to know where a colour actually landed, not what
the display list says it should have looked like: a repeating background that
leaves a one-tile gap and one that fills the box produce the same ops. The page is
rendered with --snapshot and every pixel is compared against the colour, which is
reported as the y range it covers, the x range inside it and how many pixels of it
there are.

usage: color_bbox.py <page.html> <r,g,b> [width] [height]
"""
import os
import subprocess
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import _acrux_cli as cli
from PIL import Image

page = sys.argv[1]
want = tuple(int(v) for v in sys.argv[2].split(","))
w = int(sys.argv[3]) if len(sys.argv) > 3 else 240
h = int(sys.argv[4]) if len(sys.argv) > 4 else 600

out_png = os.path.join("snapshots", "out", "_bbox.png")
subprocess.run(cli.command(["--snapshot", page, out_png, w, h, "1.0"]),
               env=cli.environment(), check=True, capture_output=True)

img = Image.open(out_png).convert("RGB")
width, height = img.size
px = img.load()

bands, band = [], None
for y in range(height):
    xs = [x for x in range(width) if tuple(px[x, y][:3]) == want]
    if not xs:
        band = None
        continue
    lo, hi = min(xs), max(xs)
    if band is not None and y == band["y0"] + len(band["los"]):
        band["los"].append(lo)
        band["his"].append(hi)
        band["n"] += len(xs)
    else:
        band = {"y0": y, "n": len(xs), "los": [lo], "his": [hi]}
        bands.append(band)

for b in bands:
    print(f"y {b['y0']}-{b['y0'] + len(b['los']) - 1}: "
          f"x {min(b['los'])}-{max(b['his'])} n={b['n']}")
if not bands:
    print(f"no pixels of {want}")
print(f"image {width}x{height} at {out_png}")
