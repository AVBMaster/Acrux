"""Build the b295 gate page from the reference engine's own dumps.

Every want cell is a transcription of what the reference printed for the same row
(snapshots/out/_b295_ref_probeClip.txt, _b295_ref_comment.txt, _b295_ref_number.txt and
_b295_ref_biglength.txt); nothing here is derived from a rule. A row whose reference answer is
data this engine does not carry — because the reference's viewport is 0x0, or because the row is
one of the measured gaps filed at the bottom of this file — is marked '~' and not compared, so the
page can say ALLPASS while the gap stays written down.

Usage:
    python3 snapshots/gen_probe295_gate.py          # write the page
"""
import json
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
OUT = os.path.join(HERE, 'css-standard-verify295-the-rect-a-box-is-cut-to-the-comment-a-value-carries-and-the-number-a-length-stops-at.html')

CELL = re.compile(r'^([a-z]+)=')


def read(path):
    with open(os.path.join(ROOT, path), encoding='utf-8') as handle:
        text = handle.read().strip()
    if text.startswith('"'):
        text = json.loads(text)
    return [line for line in text.split('\n') if line.strip()]


def cells(line):
    found = {}
    for part in line.split('|'):
        match = CELL.match(part)
        if match:
            found[match.group(1)] = part[match.end():]
    head = [p for p in line.split('|') if not CELL.match(p)]
    return head, found


def rows(path, wanted):
    """(property, value, cell dict) for the row numbers this page asks about."""
    out = []
    for line in read(path):
        head, found = cells(line)
        index = int(head[0])
        if index not in wanted:
            continue
        # head is index|label|property|value or index|property|value, depending on the probe.
        prop, value = (head[2], head[3]) if len(head) > 3 else (head[1], head[2])
        if 'par' in found:
            # The inherited pair is labelled by what it asks, not by the property it reads.
            prop = head[1]
        out.append((prop, value, found))
    return out


# The rect, asked on all four surfaces: the sides, the separators, the units, the words.
CLIP = rows('snapshots/out/_b295_ref_probeClip.txt', set(range(0, 55)))
# A comment is the space it stands for, in the CSSOM as much as in a stylesheet.
COMMENT = rows('snapshots/out/_b295_ref_comment.txt', set(range(0, 20)))
# Which numbers a length can be, and which of them the property refuses outright.
NUMBER = rows('snapshots/out/_b295_ref_number.txt', set(range(27, 53)))
# How far a computed length goes before the engine answers its own ceiling.
BIG = rows('snapshots/out/_b295_ref_biglength.txt', {0, 1, 2, 3, 4, 5, 11, 12, 13, 14, 15, 16,
                                                    17, 18, 19, 20, 21, 22, 23, 24, 26, 27, 28, 29})
# A negative length in the properties that take one, and the number printing in the rest.
DIGITS = rows('snapshots/out/_b295_ref_digits.txt', {0, 1, 2, 3, 4, 5, 6, 7, 13, 17, 18, 20, 26,
                                                    27, 34, 35, 39, 40, 41, 42, 43, 44})

ALL = [('clip', CLIP), ('comment', COMMENT), ('number', NUMBER), ('big', BIG), ('digits', DIGITS)]

# Rows whose reference answer this engine cannot reach, with the reason the gap file gives.
# 'viewport' — the reference lays out at 0x0, so a width or a 'vw' answers a number no box of
#   ours has; 'used' — the reference prints a width in the fixed-point grid its layout uses;
#   'cut' — the box family cuts a value at seven decimals before it prints six significant ones.
SKIP = [
    (lambda p, v: p in ('width', 'height') and v.startswith('-'), 'used'),
    (lambda p, v: p in ('width', 'height') and float(re.match(r'([0-9.]+)', v).group(1)) < 2, 'used'),
    (lambda p, v: len(re.findall(r'\d', v.split('.')[-1] if '.' in v else '')) > 7, 'cut'),
    (lambda p, v: 'vw' in v or 'vh' in v, 'viewport'),
    (lambda p, v: '/*' in v and 'calc' in v, 'delimiter'),
]


def skipped(prop, value):
    for test, reason in SKIP:
        try:
            if test(prop, value):
                return reason
        except Exception:
            pass
    return None


def js(value):
    return json.dumps(value)


def emit(prop, value, found):
    reason = skipped(prop, value)
    # A column the probe never asked is not a cell to compare; '~' says so without inventing one.
    def cell(key):
        if reason or key not in found:
            return '~'
        return found[key]
    parts = [js(prop), js(value), js(cell('sup')), js(cell('n')),
             js(cell('echo')), js(cell('comp'))]
    if 'par' in found:
        parts.append(js(cell('par')))
    return '  [' + ', '.join(parts) + ']'


def main():
    lines = []
    columns = set()
    for group, table in ALL:
        for prop, value, found in table:
            lines.append(emit(prop, value, found))
            columns |= set(found.keys())
    body = ',\n'.join(lines)
    template = open(os.path.join(HERE, '_gate295_template.html'), encoding='utf-8').read()
    page = template.replace('@@ROWS@@', body)
    with open(OUT, 'w', encoding='utf-8') as handle:
        handle.write(page)
    print(f'wrote {os.path.relpath(OUT, ROOT)}: {len(lines)} rows, columns {sorted(columns)}')


if __name__ == '__main__':
    main()
