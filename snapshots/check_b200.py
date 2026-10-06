"""Report the decoration line (presence + colour) for every b200 case box.

A decoration line is the row whose dark pixels run contiguously across the text
width; glyph rows always have gaps, so the contiguity test separates them.
"""
import sys
from PIL import Image

path = sys.argv[1]
img = Image.open(path).convert("RGB")
px = img.load()

# id -> (content top, content bottom) from --dumplayout, and the line the
# reference engine draws there ('' means the authored color must be invisible).
CASES = [
    ("a1 auto-kw", 127, 163, (200, 30, 30)),
    ("a2 no-decl", 165, 201, (200, 30, 30)),
    ("a3 initial", 203, 239, (200, 30, 30)),
    ("a4 unset", 241, 277, (200, 30, 30)),
    ("a5 currentcolor", 279, 315, (200, 30, 30)),
    ("b1 transparent", 441, 477, None),
    ("b2 rgba-zero", 479, 515, None),
    ("b3 hsla-zero", 517, 553, None),
    ("c1 blue", 679, 715, (0, 0, 255)),
    ("c2 shorthand-color", 717, 753, (0, 128, 255)),
    ("c3 longhand-after-shorthand", 755, 791, (200, 30, 30)),
    ("d1 overline-transparent", 917, 953, None),
    ("d2 through-transparent", 955, 991, None),
    ("d3 through-blue", 993, 1029, (0, 0, 255)),
    ("d4 wavy-transparent", 1031, 1067, None),
    ("d5 double-transparent", 1069, 1105, None),
]


def find_lines(y0, y1, x0=8, x1=400):
    out = []
    for y in range(y0, min(y1, img.height)):
        dark = [x for x in range(x0, x1) if min(px[x, y]) < 210]
        if len(dark) < 40:
            continue
        # contiguous across the whole span => decoration line, not glyph ink
        if dark[-1] - dark[0] + 1 - len(dark) <= 6:
            out.append((y, dark[0], dark[-1], px[dark[len(dark) // 2], y]))
    return out


fail = 0
for name, y0, y1, expect in CASES:
    rows = find_lines(y0, y1)
    got = rows[0][3] if rows else None
    if expect is None:
        ok = not rows
    else:
        ok = got == expect
    fail += 0 if ok else 1
    print(f"{name:30s} expect={'none' if expect is None else expect} "
          f"got={[(r[0], r[3]) for r in rows] or 'NONE'} {'OK' if ok else 'MISMATCH'}")
print(f"\n{len(CASES) - fail}/{len(CASES)} match the reference engine")
sys.exit(1 if fail else 0)
