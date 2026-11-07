"""Generate the b324 sweep: value forms of properties the earlier sweeps only
touched once or not at all.

The inset physical sides, the object-* pair, the mask-size/mode/composite forms
and the individual transforms each had at most one row across every earlier
sweep. This table gives each a value matrix — the legal form, a variant
spelling, a wrong one, a list, a negative and a bogus word — so each seat's four
surfaces can be compared against the reference engine. One fresh element per row
(measured in b283: a reused host answers a dropped declaration with the previous
row's value).
"""
import json
import os

CASES = [
    # The four physical insets, which no earlier sweep gave a matrix.
    ("top", "10px"), ("top", "50%"), ("top", "auto"), ("top", "-10px"), ("top", "bogus"),
    ("right", "10px"), ("right", "auto"), ("right", "0"), ("right", "bogus"),
    ("bottom", "10px"), ("bottom", "50%"), ("bottom", "auto"),
    ("left", "10px"), ("left", "auto"), ("left", "calc(1px + 2px)"),
    # object-fit / object-position, only 'object-fit: fill' was measured before.
    ("object-fit", "fill"), ("object-fit", "contain"), ("object-fit", "cover"),
    ("object-fit", "none"), ("object-fit", "scale-down"), ("object-fit", "bogus"),
    ("object-fit", "contain cover"),
    ("object-position", "50% 50%"), ("object-position", "left top"),
    ("object-position", "right bottom"), ("object-position", "10px 20px"),
    ("object-position", "center"), ("object-position", "50%"), ("object-position", "bogus"),
    ("object-position", "10px"), ("object-position", "left 10px bottom"),
    # mask-size / mask-mode / mask-composite, only one row each before.
    ("mask-size", "auto"), ("mask-size", "cover"), ("mask-size", "contain"),
    ("mask-size", "50% 50%"), ("mask-size", "10px"), ("mask-size", "10px 20px"),
    ("mask-size", "bogus"), ("mask-size", "cover cover"),
    ("mask-mode", "match-source"), ("mask-mode", "alpha"), ("mask-mode", "luminance"),
    ("mask-mode", "alpha luminance"), ("mask-mode", "bogus"),
    ("mask-composite", "add"), ("mask-composite", "subtract"), ("mask-composite", "intersect"),
    ("mask-composite", "exclude"), ("mask-composite", "add subtract"), ("mask-composite", "bogus"),
    # The individual transforms, which the sweeps touched only as a family.
    ("translate", "none"), ("translate", "10px"), ("translate", "10px 20px"),
    ("translate", "10px 20px 30px"), ("translate", "50%"), ("translate", "bogus"),
    ("rotate", "none"), ("rotate", "45deg"), ("rotate", "1turn"), ("rotate", "x 45deg"),
    ("rotate", "z 45deg"), ("rotate", "45"), ("rotate", "bogus"),
    ("scale", "none"), ("scale", "2"), ("scale", "50%"), ("scale", "2 3"),
    ("scale", "-1"), ("scale", "bogus"),
    ("transform-origin", "center"), ("transform-origin", "50% 50%"),
    ("transform-origin", "left top"), ("transform-origin", "10px 20px"),
    ("transform-origin", "bogus"), ("transform-origin", "center center center"),
    ("transform-box", "content-box"), ("transform-box", "border-box"),
    ("transform-box", "fill-box"), ("transform-box", "stroke-box"),
    ("transform-box", "view-box"), ("transform-box", "bogus"),
    ("transform-style", "flat"), ("transform-style", "preserve-3d"),
    ("transform-style", "preserve-flat"), ("transform-style", "bogus"),
    # image-rendering / image-orientation, one row each before.
    ("image-rendering", "auto"), ("image-rendering", "pixelated"),
    ("image-rendering", "crisp-edges"), ("image-rendering", "smooth"),
    ("image-rendering", "bogus"),
    ("image-orientation", "from-image"), ("image-orientation", "none"),
    ("image-orientation", "90deg"), ("image-orientation", "flip"),
    ("image-orientation", "bogus"),
]


def page():
    literal = "[" + ",".join(f'[{json.dumps(p)},{json.dumps(v)}]' for p, v in CASES) + "]"
    read = """  if (cases[i][1] !== '~') el.style.setProperty(cases[i][0], cases[i][1]);
  rows.push(i + '|' + cases[i][0] + '|' + cases[i][1]
      + '|sup=' + (cases[i][1] === '~' ? '-' : (CSS.supports(cases[i][0], cases[i][1]) ? 1 : 0))
      + '|n=' + el.style.length
      + '|echo=' + el.style.getPropertyValue(cases[i][0])
      + '|comp=' + getComputedStyle(el).getPropertyValue(cases[i][0]));"""
    template = """<!doctype html>
<html><head><meta charset="utf-8"><title>b324 sweep</title></head>
<body>
<div id="host"></div>
<script>
// sweep
function build() {{
const host = document.createElement('div');
document.body.appendChild(host);
const rows = [];
const cases = {cases};
for (let i = 0; i < cases.length; i++) {{
const el = document.createElement('div');
host.appendChild(el);
{read}
    el.remove();
}}
document.body.insertAdjacentHTML('beforeend',
    '<pre id="record" style="font:10px/1.35 monospace;white-space:pre-wrap"></pre>');
// textContent, not HTML: a value carrying '<' is read as a start tag and vanishes.
document.getElementById('record').textContent = rows.join('\\n');
console.log(rows.join('\\n'));
host.remove();
}}
build();
</script>
</body></html>
"""
    return template.format(cases=literal, read=read)


HERE = os.path.dirname(os.path.abspath(__file__))
with open(os.path.join(HERE, "_probe_b324_sweep.html"), "w", encoding="utf-8") as handle:
    handle.write(page())
print("cases:", len(CASES))
