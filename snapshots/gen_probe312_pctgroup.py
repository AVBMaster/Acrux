"""b312: a percentage that shares a sum with a group the page folded into it.

CSS Values 4 §10.2 lets a math expression hold a share of the box beside lengths, and the layout owns
the price of that share. Every case here puts the share in front of a sum the value text has already
merged into more than one term — 'calc(100% - (1em + 20px))' is 'calc(100% - 1em - 20px)' by §10.13 —
so the question is whether the layout prices the whole rest of the sum or only the term it can see.
Each sum is asked twice: on a static block, whose share is of its containing block, and on an
absolutely positioned box, whose share is of the positioned ancestor — the two paths price it
separately. The container is 400px and the font is 16px in both engines, so the widths are directly
comparable.
"""
import os
import re

# (how the box is placed, the width it is given)
CASES = [
    ("static", "calc(100% - 10px)"),
    ("static", "calc(100% - 1em - 20px)"),
    ("static", "calc(100% - (1em + 20px))"),
    ("static", "calc(100% - (20px + 1em))"),
    ("static", "calc(1em + 20px - 100%)"),
    ("static", "calc(100% - 1em + 20px)"),
    ("static", "calc(100% * 2 - 40px)"),
    ("static", "calc(50% + (1em + 20px))"),
    ("static", "calc(100% - max(1em, 20px))"),
    ("static", "calc(100% - min(1em, 20px))"),
    ("static", "calc(100% / 2 + 1em)"),
    ("static", "calc(100% - (1rem + 2ex))"),
    ("abs", "calc(100% - 10px)"),
    ("abs", "calc(100% - (1em + 20px))"),
    ("abs", "calc(100% - 1em - 20px)"),
    ("abs", "calc(50% + (1em + 20px))"),
    ("abs", "50%"),
    # The lengths the out-of-flow branch reads through the same helper: a font unit, a viewport
    # unit and a share of the viewport inside a sum. The reference engine's viewport is its own
    # window, so only the em row is directly comparable — the vw rows say whether the engine priced
    # the share at all or left it at zero.
    ("abs", "2em"),
    ("abs", "calc(2em + 20px)"),
    ("abs", "50vw"),
    ("abs", "calc(50vw - 100px)"),
]

html = """<!doctype html>
<html><head><meta charset="utf-8"><title>b312 pctgroup</title>
<style>
  body {{ margin: 0; font: 16px/1 monospace; }}
  #wrap, #area {{ width: 400px; position: relative; }}
  #wrap > div {{ height: 4px; background: #ff8080; }}
  #area > div {{ position: absolute; left: 0; top: 0; height: 4px; background: #ff8080; }}
  #area {{ height: 4px; margin-top: 8px; }}
</style></head>
<body>
<div id="wrap">
{boxes}
</div>
<div id="area"></div>
<script>
// b312_pctgroup
function build() {{
const rows = [];
const cases = {cases};
for (let i = 0; i < cases.length; i++) {{
const el = document.createElement('div');
const host = cases[i][0] == 'abs' ? document.getElementById('area')
                                   : document.getElementById('wrap');
host.appendChild(el);
  el.style.width = cases[i][1];
  const r = el.getBoundingClientRect();
  rows.push(i + '|width|' + cases[i][1]
      + '|sup=' + (CSS.supports('width', cases[i][1]) ? 1 : 0)
      + '|echo=' + el.style.width
      + '|comp=' + getComputedStyle(el).width
      + '|w=' + (Math.round(r.width * 100) / 100));
    el.remove();
}}
document.body.insertAdjacentHTML('beforeend',
    '<pre id="record" style="font:10px/1.35 monospace;white-space:pre-wrap">'
    + rows.join('\\n') + '</pre>');
console.log(rows.join('\\n'));
}}
build();
</script></body></html>
"""

cases_js = "[" + ",".join(f'["{mode}","{value}"]' for mode, value in CASES) + "]"
# The painted boxes carry the static cases; the absolutely positioned ones are measured only, so a
# page that paints and a page that reports cannot disagree about which boxes the rows describe.
boxes = "\n".join(f'<div style="width: {value}"></div>'
                  for mode, value in CASES if mode == "static")
out = os.path.join(os.path.dirname(os.path.abspath(__file__)), "_probe_b312_pctgroup.html")
with open(out, "w") as f:
    f.write(html.format(cases=cases_js, boxes=boxes))
print(out, len(CASES), "rows")
