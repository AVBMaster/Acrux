"""Crop a PNG to a rectangle and save it, so a tall reference can be inspected."""
import os, struct, sys, zlib
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from diff_bands import load


def write_png(path, w, h, ch, rows):
    raw = b"".join(b"\x00" + r for r in rows)
    comp = zlib.compress(raw, 6)
    def chunk(typ, data):
        return (struct.pack(">I", len(data)) + typ + data
                + struct.pack(">I", zlib.crc32(typ + data) & 0xffffffff))
    color_type = {1: 0, 2: 2, 3: 6, 4: 6}[ch]
    with open(path, "wb") as f:
        f.write(b"\x89PNG\r\n\x1a\n")
        f.write(chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, color_type, 0, 0, 0)))
        f.write(chunk(b"IDAT", comp))
        f.write(chunk(b"IEND", b""))


src, out, x0, y0, x1, y1 = sys.argv[1], sys.argv[2], *[int(v) for v in sys.argv[3:7]]
w, h, ch, rows = load(src)
x1, y1 = min(x1, w), min(y1, h)
crop = [r[x0 * ch:x1 * ch] for r in rows[y0:y1]]
write_png(out, x1 - x0, y1 - y0, ch, crop)
print(f"{out}: {x1 - x0}x{y1 - y0} from {src}")
