"""Build the b294 gate page from the reference engine's own dumps.

Every want cell is a transcription of what the reference printed for the same row
(snapshots/out/_b294_micro_ref.txt for the value text, the weight table and the inherited pairs,
snapshots/out/_b294_ref_probeE.txt for the keyword sets, and
snapshots/out/_b294_baked_guards.txt for the guards — that last file is this same page's own
window.GUARDS as the reference engine measured it, so a guard cell is never a rule of ours).
A cell the reference answers with data this engine does not have is marked '~' and filed as a gap.

Usage:
    python3 snapshots/gen_probe294_gate.py            # write the page with the guards baked in
    python3 snapshots/gen_probe294_gate.py --no-guards  # write it with empty guards, for the bake
"""
import json
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)


def read(path):
    with open(os.path.join(ROOT, path), encoding='utf-8') as handle:
        text = handle.read().strip()
    if text.startswith('"'):
        text = json.loads(text)
    return [line for line in text.split('\n') if line.strip()]


CELL = re.compile(r'^([a-z]+)=')


def cells(line):
    """(the row's own fields, its 'key=value' cells) — the same reading keyword_diff uses."""
    parts = line.split('|')
    head = [p for p in parts if not CELL.match(p)]
    found = {}
    for part in parts:
        match = CELL.match(part)
        if match and match.group(1) not in found:
            found[match.group(1)] = part[match.end():].strip()
    return head, found


# A row is only wanted once per property: two accepted values and two refused ones say as much as
# twenty, and the page has to stay quick enough to re-render inside the marker gate.
PER_PROPERTY = 4


def direct(rows, label):
    """The probe's own rows, in the order the reference wrote them, capped per property. The micro
    page prefixes each row with its section letter, the keyword page does not."""
    seen = {}
    picked = []
    for line in rows:
        head, cell = cells(line)
        # The micro page prefixes its rows with the section letter; the keyword page does not.
        rest = head[1:]
        if rest and rest[0] in ('D', 'W', 'I', 'C', 'S'):
            rest = rest[1:]
        if len(rest) < 2:
            continue
        prop, value = rest[0], rest[1]
        seen[prop] = seen.get(prop, 0) + 1
        if seen[prop] > PER_PROPERTY:
            continue
        picked.append([prop, value, cell.get('sup', ''), cell.get('n', ''),
                       cell.get('echo', ''), cell.get('comp', '')])
    return picked


def weights(rows):
    out = []
    for line in rows:
        head, cell = cells(line)
        if not head[1].startswith('W'):
            continue
        out.append([head[3], cell.get('comp', ''), cell.get('bolder', ''),
                    cell.get('lighter', '')])
    return out


def inherited(rows):
    out = []
    for line in rows:
        head, cell = cells(line)
        if not head[1].startswith('I'):
            continue
        out.append([head[2], head[3], cell.get('par', ''), cell.get('comp', '')])
    return out


MICRO = read('snapshots/out/_b294_micro_ref.txt')
E = read('snapshots/out/_b294_ref_probeE.txt')
BAKED = 'snapshots/out/_b294_baked_rows.json'

# The micro page asks about every property this round touched; the keyword-set probe covers the
# families the micro page only sampled. The overlap is dropped so no cell is asked twice.
covered = {(row[0], row[1]) for row in direct(MICRO, 'D')}
pairs = direct(MICRO, 'D') + [row for row in direct(E, 'D') if (row[0], row[1]) not in covered]

# Once the reference engine has run the page, its own measured cells are the want side; until then
# the rows carry no want cells and the page only measures — that run is the bake (the harness, the
# container and the element each value is read from are then the same on both sides, so a cell
# cannot disagree with itself over a percentage priced against a different box).
if os.path.exists(os.path.join(ROOT, BAKED)):
    with open(os.path.join(ROOT, BAKED), encoding='utf-8') as handle:
        baked = json.load(handle)
    D, W, I, GUARD_LINES = baked['D'], baked['W'], baked['I'], baked['G']
else:
    D = [[row[0], row[1], '', '', '', ''] for row in pairs]
    W = [[row[0], '', '', ''] for row in weights(MICRO)]
    I = [[row[0], row[1], '', ''] for row in inherited(MICRO)]
    GUARD_LINES = []


def js(text):
    return json.dumps(text, ensure_ascii=False)


def block(rows):
    return ',\n'.join('  [' + ', '.join(js(cell) for cell in row) + ']' for row in rows)


TEMPLATE = open(os.path.join(HERE, '_gate294_template.html'), encoding='utf-8').read()
html = TEMPLATE.replace('@@D@@', block(D)).replace('@@W@@', block(W))
html = html.replace('@@I@@', block(I))
# A guard is one measured line, not a row of cells, so it goes in as a string.
html = html.replace('@@G@@', ',\n'.join('  ' + js(line) for line in GUARD_LINES))
out = os.path.join(ROOT, 'snapshots',
                   'css-standard-verify294-the-ink-a-hidden-border-leaves-the-number-a-weight-'
                   'inherits-and-the-word-a-value-answers.html')
with open(out, 'w', encoding='utf-8') as handle:
    handle.write(html)
print(os.path.relpath(out, ROOT), 'D', len(D), 'W', len(W), 'I', len(I), 'G', len(GUARD_LINES))
