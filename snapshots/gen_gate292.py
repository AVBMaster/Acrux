"""Build the b292 gate page from the reference engine's own dumps.

Every want cell is a transcription of what the reference printed for the same page
(snapshots/out/_b292_ref_probeA.txt, _b292_ref_probeB.txt and the guard lines of
_probe_b292_guards.html as the reference answered them); nothing here is derived from a rule. The two
cells the reference answers with data this engine does not have are marked '~' and filed as gaps.
"""
import json

GAP_CELLS = {
    # (property, value): which cell index is '~' and why
    ('font-size-adjust', 'from-font'): (3, 'the font aspect ratio'),
}

def parse(path):
    rows = []
    for line in json.load(open(path)).split('\n'):
        # The door rows are a table of their own (S), not one declaration per case.
        if not line.strip() or line.startswith('D|'):
            continue
        parts = line.split('|')
        cells = {}
        for p in parts[3:]:
            if '=' in p:
                k, v = p.split('=', 1)
                cells[k] = v
        rows.append((parts[1], parts[2], cells))
    return rows

e_rows = parse('snapshots/out/_b292_ref_probeA.txt') + parse('snapshots/out/_b292_ref_probeB.txt')
E = []
for prop, value, c in e_rows:
    cells = [c.get('n', ''), c.get('names', ''), c.get('echo', ''), c.get('comp', ''),
             c.get('sup', ''), c.get('text', '')]
    if (prop, value) in GAP_CELLS:
        index, _ = GAP_CELLS[(prop, value)]
        cells[index] = '~'
    E.append([prop, value] + cells)

S = [line for line in json.load(open('snapshots/out/_b292_ref_probeB.txt')).split('\n')
     if line.startswith('D|')]

G = ['g1|boxUnmoved=60.0,20.0|outlineSpans=68.0|offsetBox=80.0',
     'g2|lifted=10.0|dropped=10.0|width=20.0|sideBySide=0.0,0.0',
     'g3|borderBox=100.0,40.0|contentBox=130.0,70.0',
     'g4|cellOuter=60.0,16.0|gap=0.0']


def js(text):
    return json.dumps(text, ensure_ascii=False)


e_body = ',\n'.join('  [' + ', '.join(js(cell) for cell in row) + ']' for row in E)
s_body = ',\n'.join('  ' + js(row) for row in S)
g_body = ',\n'.join('  ' + js(row) for row in G)

TEMPLATE = open('snapshots/_gate292_template.html').read()
html = TEMPLATE.replace('@@E@@', e_body).replace('@@S@@', s_body).replace('@@G@@', g_body)
out = ('snapshots/css-standard-verify292-the-property-families-with-no-grammar-of-their-own-and-'
       'what-a-legacy-spelling-answers.html')
open(out, 'w').write(html)
print('rows E', len(E), 'S', len(S), 'G', len(G), 'bytes', len(html), '->', out)
