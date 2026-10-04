"""Report the ink bounding box of a PNG so a scale mismatch is obvious."""
import struct, sys, zlib


def load(path):
    d = open(path, "rb").read()
    pos = 8
    w = h = bit = color = 0
    idat = b""
    while pos < len(d):
        ln = struct.unpack(">I", d[pos:pos + 4])[0]
        typ = d[pos + 4:pos + 8]
        data = d[pos + 8:pos + 8 + ln]
        if typ == b"IHDR":
            w, h, bit, color = struct.unpack(">IIBB", data[:10])
        elif typ == b"IDAT":
            idat += data
        pos += 12 + ln
    raw = zlib.decompress(idat)
    ch = {0: 1, 2: 3, 4: 2, 6: 4}[color]
    stride = w * ch
    rows = []
    prev = bytearray(stride)
    i = 0
    for y in range(h):
        ft = raw[i]
        i += 1
        line = bytearray(raw[i:i + stride])
        i += stride
        for x in range(stride):
            a = line[x - ch] if x >= ch else 0
            b = prev[x]
            c = prev[x - ch] if x >= ch else 0
            if ft == 1:
                line[x] = (line[x] + a) & 255
            elif ft == 2:
                line[x] = (line[x] + b) & 255
            elif ft == 3:
                line[x] = (line[x] + ((a + b) >> 1)) & 255
            elif ft == 4:
                p = a + b - c
                pa, pb, pc = abs(p - a), abs(p - b), abs(p - c)
                pr = a if (pa <= pb and pa <= pc) else (b if pb <= pc else c)
                line[x] = (line[x] + pr) & 255
        rows.append(bytes(line))
        prev = line
    return w, h, ch, rows


def ink(path):
    w, h, ch, rows = load(path)
    minx, miny, maxx, maxy = w, h, -1, -1
    for y, row in enumerate(rows):
        for x in range(w):
            o = x * ch
            r, g, b = row[o], row[o + 1] if ch > 1 else row[o], row[o + 2] if ch > 2 else row[o]
            if r < 200 or g < 200 or b < 200:
                if x < minx: minx = x
                if x > maxx: maxx = x
                if y < miny: miny = y
                if y > maxy: maxy = y
    print(f"{path}: {w}x{h} ink=({minx},{miny})-({maxx},{maxy}) size={maxx-minx+1}x{maxy-miny+1}")


for p in sys.argv[1:]:
    ink(p)
