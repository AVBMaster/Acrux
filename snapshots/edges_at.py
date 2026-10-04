"""List the y positions where a column's colour changes, to compare box edges
between a reference snapshot and the current render without eyeballing pixels."""
import os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from diff_bands import load

path, xs = sys.argv[1], [int(v) for v in sys.argv[2:]]
w, h, ch, rows = load(path)
for x in xs:
    prev = None
    edges = []
    for y in range(h):
        o = x * ch
        px = (rows[y][o], rows[y][o + 1], rows[y][o + 2])
        if prev is not None and any(abs(a - b) > 24 for a, b in zip(px, prev)):
            edges.append((y, prev, px))
        prev = px
    print(f"x={x}: " + ", ".join(f"y={y}:{a}->{b}" for y, a, b in edges))
