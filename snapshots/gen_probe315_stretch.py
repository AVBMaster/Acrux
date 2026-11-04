"""Generate the b315 edges3 probe: what 'stretch' in the block axis is actually worth.

The computed surface prices a sizing keyword at the box the layout made, so the layout has to know
what the keyword means before the surface can say it. These rows ask that in a box whose height the
two engines can both measure: with a margin, with padding, under 'box-sizing: border-box', in a
parent whose height is not definite, and under the legacy spelling. The rows are geometry, not
value text, so the page prints rectangles.
"""
import json
import os

CSS = [
    ("s1", "height: stretch"),
    ("s2", "height: stretch; margin: 10px"),
    ("s3", "height: stretch; padding: 5px"),
    ("s4", "height: stretch; box-sizing: border-box; border: 4px solid black"),
    ("s5", "height: -webkit-fill-available"),
    ("s6", "block-size: stretch"),
    ("s7", "height: stretch; min-height: 20px"),
    ("s8", "height: stretch; max-height: 40px"),
    ("s9", "height: stretch; margin-top: 30px; margin-bottom: 20px"),
    ("i1", "height: stretch"),
    ("w1", "width: stretch"),
    ("w2", "width: stretch; margin: 10px"),
    ("w3", "width: stretch; box-sizing: border-box; padding: 8px"),
    # The same question without the keyword: which box the computed surface prints when the page
    # asked for a border box. These four rows decide whether 'stretch' has a rule of its own or
    # whether the box-sizing answer is the general one.
    ("b1", "width: 100px; box-sizing: border-box; padding: 8px"),
    ("b2", "height: 60px; box-sizing: border-box; padding: 5px; border: 5px solid black"),
    ("b3", "width: 50%; box-sizing: border-box; padding: 8px"),
    ("b4", "box-sizing: border-box; padding: 8px"),
]

TEMPLATE = """<!doctype html>
<html><head><meta charset="utf-8"><title>b315 edges3</title>
<style>
  body {{ margin: 0; font: 12px monospace; }}
  /* A definite 300x100 host for the nine stretch rows, and an auto-height one for the row that
     asks what stretch is worth when the space is not definite. */
  #host {{ position: absolute; left: 0; top: 0; width: 300px; height: 100px; }}
  #auto {{ position: absolute; left: 0; top: 120px; width: 300px; }}
</style></head>
<body>
<div id="host">{host}</div>
{auto}
<script>
// edges3
function build() {{
const rows = [];
const ids = {ids};
for (let i = 0; i < ids.length; i++) {{
  const el = document.getElementById(ids[i]);
  const r = el.getBoundingClientRect();
  rows.push(ids[i] + '|rect=' + Math.round(r.x) + ',' + Math.round(r.y) + ' '
      + Math.round(r.width * 100) / 100 + 'x' + Math.round(r.height * 100) / 100
      + '|comp=' + getComputedStyle(el).getPropertyValue('height')
      + '|w=' + getComputedStyle(el).getPropertyValue('width'));
}}
document.body.insertAdjacentHTML('beforeend',
    '<pre id="record" style="position:absolute;left:10px;top:220px;font:10px/1.35 monospace;white-space:pre-wrap"></pre>');
// textContent, not HTML: a value carrying '<' is read as a start tag and vanishes.
document.getElementById('record').textContent = rows.join('\\n');
console.log(rows.join('\\n'));
}}
build();
</script></body></html>
"""


def page():
    host = "".join(
        f'<div id="{name}" style="{css}; background:#ff8080"></div>'
        for name, css in CSS if name not in ("i1",))
    auto = '<div id="i1" style="height: stretch; background:#80ff80"></div>'
    ids = json.dumps([name for name, _ in CSS])
    return TEMPLATE.format(host=host, auto=auto, ids=ids)


HERE = os.path.dirname(os.path.abspath(__file__))
with open(os.path.join(HERE, "_b315_stretch_geometry.html"), "w", encoding="utf-8") as handle:
    handle.write(page())
print("rows:", len(CSS))
