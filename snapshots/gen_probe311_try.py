"""Generate the b311 position-try probes: the fallback list, and the shorthand that wraps it.

A fallback is a run of flips OR one position area, never both, and the list is comma-separated; the
area half has to read the same way here as it does on 'position-area', which is what the first table
measures. The second table is the list itself: two items, 'none' sharing a list with nothing, and a
flip written twice.
"""
import json
import os

TRY = [
    ("position-try-fallbacks", "top left"), ("position-try-fallbacks", "left top"),
    ("position-try-fallbacks", "top center"),
    ("position-try-fallbacks", "block-start inline-end"),
    ("position-try-fallbacks", "inline-start block-end"),
    ("position-try-fallbacks", "span-all start"), ("position-try-fallbacks", "start span-all"),
    ("position-try-fallbacks", "top span-all"),
    ("position-try-fallbacks", "flip-start top left"),
    ("position-try-fallbacks", "flip-block span-all center"),
    ("position-try-fallbacks", "none"), ("position-try-fallbacks", "center center"),
    ("position-try-fallbacks", "self-start center"),
    ("position-try-fallbacks", "top self-start"),
    ("position-try", "top left"), ("position-try", "most-width inline-start block-end"),
    ("position-try", "flip-block left top"),
    ("position-area", "top left"), ("position-area", "block-start inline-start"),
]

TRY2 = [
    ("position-try-fallbacks", "flip-start, flip-block"),
    ("position-try-fallbacks", "left top, flip-inline"),
    ("position-try-fallbacks", "flip-inline, top left"),
    ("position-try-fallbacks", "none, center"),
    ("position-try-fallbacks", "center, center"),
    ("position-try-fallbacks", "top left, block-start inline-end"),
    ("position-try-fallbacks", "flip-start flip-inline"),
    ("position-try-fallbacks", "insert-inline, left top"),
    ("position-try", "none, top left"),
    ("position-try", "most-height left top, --t"),
    ("position-try", "flip-start, flip-block"),
    ("position-try-fallbacks", "span-top span-left"),
    ("position-try-fallbacks", "block-start span-all"),
    ("position-try-fallbacks", "self-block-start center"),
]


def page(title, cases):
    literal = "[" + ",".join(f'[{json.dumps(p)},{json.dumps(v)}]' for p, v in cases) + "]"
    read = """  el.style.setProperty(cases[i][0], cases[i][1]);
  rows.push(i + '|' + cases[i][0] + '|' + cases[i][1]
      + '|sup=' + (CSS.supports(cases[i][0], cases[i][1]) ? 1 : 0)
      + '|echo=' + el.style.getPropertyValue(cases[i][0])
      + '|comp=' + getComputedStyle(el).getPropertyValue(cases[i][0]));"""
    return """<!doctype html>
<html><head><meta charset="utf-8"><title>%s</title></head>
<body>
<div id="host"></div>
<script>
// %s
function build() {
const host = document.createElement('div');
document.body.appendChild(host);
const rows = [];
const cases = %s;
for (let i = 0; i < cases.length; i++) {
const el = document.createElement('div');
host.appendChild(el);
%s
}
document.body.insertAdjacentHTML('beforeend',
    '<pre id="record" style="font:10px/1.35 monospace;white-space:pre-wrap">'
    + rows.join('\\n') + '</pre>');
console.log(rows.join('\\n'));
host.remove();
}
build();
</script>
</body></html>
""" % (title, title.replace(' ', '_'), literal, read)


HERE = os.path.dirname(os.path.abspath(__file__))
for name, cases in [("try", TRY), ("try2", TRY2)]:
    with open(os.path.join(HERE, f"_probe_b311_{name}.html"), "w", encoding="utf-8") as handle:
        handle.write(page(f"b311 {name}", cases))
print("try:", len(TRY), "try2:", len(TRY2))
