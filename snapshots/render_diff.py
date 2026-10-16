"""Report where a fresh render differs from a baked baseline, row by row.

Usage: python snapshots/render_diff.py <baseline.png> <fresh.png> [y0 y1]
Prints the rows that differ, the columns they span, and the colour pairs that changed — which is
how a drift is traced to the declaration that caused it.
"""
import struct
import sys
import zlib


def load(path):
    data = open(path, 'rb').read()
    pos = 8
    width = height = 0
    idat = b''
    while pos < len(data):
        length = struct.unpack('>I', data[pos:pos + 4])[0]
        kind = data[pos + 4:pos + 8]
        if kind == b'IHDR':
            width, height = struct.unpack('>II', data[pos + 8:pos + 16])
        if kind == b'IDAT':
            idat += data[pos + 8:pos + 8 + length]
        pos += 12 + length
    raw = zlib.decompress(idat)
    stride = width * 4 + 1
    out = bytearray()
    previous = bytearray(width * 4)
    for y in range(height):
        filter_type = raw[y * stride]
        line = bytearray(raw[y * stride + 1:(y + 1) * stride])
        if filter_type == 1:
            for i in range(4, len(line)):
                line[i] = (line[i] + line[i - 4]) & 0xFF
        elif filter_type == 2:
            for i in range(len(line)):
                line[i] = (line[i] + previous[i]) & 0xFF
        elif filter_type == 3:
            for i in range(len(line)):
                left = line[i - 4] if i >= 4 else 0
                line[i] = (line[i] + ((left + previous[i]) >> 1)) & 0xFF
        elif filter_type == 4:
            for i in range(len(line)):
                a = line[i - 4] if i >= 4 else 0
                b = previous[i]
                c = previous[i - 4] if i >= 4 else 0
                p = a + b - c
                pa, pb, pc = abs(p - a), abs(p - b), abs(p - c)
                pred = a if (pa <= pb and pa <= pc) else (b if pb <= pc else c)
                line[i] = (line[i] + pred) & 0xFF
        out += line
        previous = line
    return width, height, bytes(out)


def main():
    if len(sys.argv) < 3:
        print(__doc__)
        return 2
    base, fresh = sys.argv[1], sys.argv[2]
    w1, h1, a = load(base)
    w2, h2, b = load(fresh)
    print('dims', w1, h1, 'vs', w2, h2)
    top = int(sys.argv[3]) if len(sys.argv) > 4 else 0
    bottom = int(sys.argv[4]) if len(sys.argv) > 4 else min(h1, h2)
    rows = []
    pairs = {}
    for y in range(top, min(h1, h2, bottom)):
        o1, o2 = y * w1 * 4, y * w2 * 4
        for x in range(min(w1, w2)):
            p1 = a[o1 + x * 4:o1 + x * 4 + 4]
            p2 = b[o2 + x * 4:o2 + x * 4 + 4]
            if p1 != p2:
                rows.append(y)
                pairs[(tuple(p1[:3]), tuple(p2[:3]))] = pairs.get(
                    (tuple(p1[:3]), tuple(p2[:3])), 0) + 1
    if not rows:
        print('no differing pixels')
        return 0
    print('differing pixels', len(rows), 'rows', min(rows), 'to', max(rows),
          'columns', 'spanned')
    for pair, count in sorted(pairs.items(), key=lambda kv: -kv[1])[:8]:
        print('  baseline', pair[0], '-> fresh', pair[1], 'x', count)
    return 0


if __name__ == '__main__':
    sys.exit(main())
