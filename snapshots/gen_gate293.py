"""Build the b293 gate page from the reference engine's own dumps.

Every want cell is a transcription of what the reference printed for the same page
(snapshots/out/_b293_ref_probeA.txt, _b293_ref_probeB.txt, _b293_ref_probeC.txt and
_b293_ref_guards.txt, each line as the reference answered it); nothing here is derived from a rule.
A cell the reference answers with data this engine does not have is marked '~' and filed as a gap.
"""
import json
import sys

GAP_CELLS = {
    # (property, value): which cells are '~' and which gap files them. The reference keeps a
    # 'color-mix()' in both the computed value and the cssText echo; this engine folds it to an
    # rgb() triple (#116).
    ('fill', 'color-mix(in srgb, red 50%, blue)'): ((2, 3, 5), '#116 the computed colour keeps its notation'),
}


def dump(path):
    return [r for r in json.load(open(path)).split('\n') if r.strip()]


def cells_of(line):
    out = {}
    for part in line.split('|')[3:]:
        if '=' in part:
            key, value = part.split('=', 1)
            out[key] = value
    return out


def declarations(paths):
    rows = []
    for path in paths:
        for line in dump(path):
            if not line.split('|')[0][0].isdigit():
                continue
            parts = line.split('|')
            cells = cells_of(line)
            want = [cells.get('n', ''), cells.get('names', ''), cells.get('echo', ''),
                    cells.get('comp', ''), cells.get('sup', ''), cells.get('text', '')]
            key = (parts[1], parts[2])
            if key in GAP_CELLS:
                indices, _ = GAP_CELLS[key]
                for index in indices:
                    want[index] = '~'
            rows.append([parts[1], parts[2]] + want)
    return rows


def inheritances(path):
    rows = []
    for line in dump(path):
        if not line.startswith('I'):
            continue
        parts = line.split('|')
        parent = parts[3].split('=', 1)[1]
        child = parts[4].split('=', 1)[1]
        rows.append([parts[1], parts[2], parent, child])
    return rows


def doors(paths):
    rows = []
    for path in paths:
        for line in dump(path):
            head = line.split('|')[0]
            if head.startswith('D|') or head.startswith('A'):
                rows.append(line)
    return rows


def guards(path):
    return [line for line in dump(path) if line.startswith('G') or line.startswith('C|')]


E = declarations(['snapshots/out/_b293_ref_probeA.txt', 'snapshots/out/_b293_ref_probeB.txt',
                  'snapshots/out/_b293_ref_probeC.txt'])
I = inheritances('snapshots/out/_b293_ref_probeC.txt')
A = doors(['snapshots/out/_b293_ref_probeB.txt', 'snapshots/out/_b293_ref_probeC.txt'])
G = guards('snapshots/out/_b293_ref_guards.txt')


def js(text):
    return json.dumps(text, ensure_ascii=False)


TEMPLATE = open('snapshots/_gate293_template.html').read()
html = TEMPLATE.replace('@@E@@', ',\n'.join('  [' + ', '.join(js(cell) for cell in row) + ']' for row in E))
html = html.replace('@@I@@', ',\n'.join('  [' + ', '.join(js(cell) for cell in row) + ']' for row in I))
html = html.replace('@@A@@', ',\n'.join('  ' + js(row) for row in A))
html = html.replace('@@G@@', ',\n'.join('  ' + js(row) for row in G))
out = ('snapshots/css-standard-verify293-the-paint-a-reference-carries-the-math-a-length-names-'
       'and-the-strings-a-page-escapes.html')
open(out, 'w', encoding='utf-8').write(html)
print(out, 'E', len(E), 'I', len(I), 'A', len(A), 'G', len(G),
      'gap cells', sum(1 for row in E if '~' in row))
sys.exit(0)
