"""Report the y-bands where two PNGs differ, so a stale ref can be attributed."""
import struct, sys, zlib


def load(path):
    d = open(path, "rb").read()
    pos, idat = 8, b""
    w = h = bit = color = 0
    while pos < len(d):
        ln = struct.unpack(">I", d[pos:pos + 4])[0]
        typ, data = d[pos + 4:pos + 8], d[pos + 8:pos + 8 + ln]
        if typ == b"IHDR":
            w, h, bit, color = struct.unpack(">IIBB", data[:10])
        elif typ == b"IDAT":
            idat += data
        pos += 12 + ln
    raw = zlib.decompress(idat)
    ch = {0: 1, 2: 3, 4: 2, 6: 4}[color]
    stride = w * ch
    rows, prev = [], bytearray(stride)
    i = 0
    for _ in range(h):
        ft = raw[i]; i += 1
        line = bytearray(raw[i:i + stride]); i += stride
        for x in range(stride):
            a = line[x - ch] if x >= ch else 0
            b = prev[x]
            c = prev[x - ch] if x >= ch else 0
            if ft == 1: line[x] = (line[x] + a) & 255
            elif ft == 2: line[x] = (line[x] + b) & 255
            elif ft == 3: line[x] = (line[x] + ((a + b) >> 1)) & 255
            elif ft == 4:
                p = a + b - c
                pa, pb, pc = abs(p - a), abs(p - b), abs(p - c)
                pr = a if (pa <= pb and pa <= pc) else (b if pb <= pc else c)
                line[x] = (line[x] + pr) & 255
        rows.append(bytes(line)); prev = line
    return w, h, ch, rows


if __name__ == "__main__":
    a, b = sys.argv[1], sys.argv[2]
    wa, ha, ca, ra = load(a)
    wb, hb, cb, rb = load(b)
    h = min(ha, hb)
    counts = []
    for y in range(h):
        n = 0
        sa, sb = ra[y], rb[y]
        for x in range(min(wa, wb)):
            o1, o2 = x * ca, x * cb
            if sa[o1] != sb[o2] or sa[o1 + 1] != sb[o2 + 1] or sa[o1 + 2] != sb[o2 + 2]:
                n += 1
        counts.append(n)

    # collapse consecutive dirty rows into bands
    bands, start = [], None
    for y, n in enumerate(counts):
        if n > 3 and start is None:
            start = y
        elif n <= 3 and start is not None:
            bands.append((start, y - 1))
            start = None
    if start is not None:
        bands.append((start, h - 1))
    print(f"{len(counts)} rows, {sum(1 for c in counts if c > 3)} dirty")
    for lo, hi in bands[:40]:
        print(f"  band y={lo}..{hi} ({hi - lo + 1}px) max={max(counts[lo:hi + 1])}")
