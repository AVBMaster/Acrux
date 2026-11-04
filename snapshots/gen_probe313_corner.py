"""b313: the page that shows what the new value models are worth on screen.

The value model of 'corner-shape' and 'calc-size()' is measured in the probe tables; this page asks
the two questions only a picture and a rect can answer: does a corner the page gave a shape still
paint (the engine has no shape consumer yet, so it must paint the round corner it painted before),
and what width does a calc-size() expression actually produce (the layout has no 'size' pricing
yet, so the box must still take the width it would have had without the declaration). The reference
engine's rows are the truth both gaps are measured against.
"""
import os

BOXES = [
    ("c1", "corner-shape: round", "border-radius: 24px; background: #ff8080"),
    ("c2", "corner-shape: scoop", "border-radius: 24px; background: #80ff80"),
    ("c3", "corner-shape: notch", "border-radius: 24px; background: #8080ff"),
    ("c4", "corner-shape: superellipse(2)", "border-radius: 24px; background: #ffff80"),
    ("w1", "width: calc-size(auto, size + 20px)", "height: 20px; background: #ff80ff"),
    ("w2", "width: calc-size(auto, 100% - 20px)", "height: 20px; background: #80ffff"),
    ("w3", "width: calc-size(100px, size)", "height: 20px; background: #c0c0c0"),
    ("w4", "width: 100px", "height: 20px; background: #ff8040"),
]

CASES = ["corner-shape", "corner-top-left-shape", "corner-shape: round scoop"]

html = """<!doctype html>
<html><head><meta charset="utf-8"><title>b313 corner and calc-size</title>
<style>
  body {{ margin: 0; font: 12px/1.3 monospace; }}
  .row {{ position: relative; height: 30px; }}
  .box {{ position: absolute; left: 0; top: 3px; height: 24px; width: 120px; }}
  #wrap {{ width: 200px; }}
  #wrap > div {{ height: 20px; }}
</style></head>
<body>
{rows}
<div id="wrap"></div>
<script>
// b313_corner_calcsize
function build() {{
const rows = [];
const specs = {specs};
for (let i = 0; i < specs.length; i++) {{
const el = document.createElement('div');
document.body.appendChild(el);
  el.style.setProperty(specs[i][0], specs[i][1]);
  rows.push('v' + i + '|' + specs[i][0] + '|sup=' + (CSS.supports(specs[i][0], specs[i][1]) ? 1 : 0)
      + '|echo=' + el.style.getPropertyValue(specs[i][0])
      + '|comp=' + getComputedStyle(el).getPropertyValue(specs[i][0]));
  el.remove();
}}
const ids = {ids};
for (let i = 0; i < ids.length; i++) {{
const el = document.getElementById(ids[i]);
const r = el.getBoundingClientRect();
rows.push(ids[i] + '|w=' + (Math.round(r.width * 100) / 100)
    + '|h=' + (Math.round(r.height * 100) / 100)
    + '|comp=' + getComputedStyle(el).width);
}}
document.body.insertAdjacentHTML('beforeend',
    '<pre id="record" style="font:10px/1.35 monospace;white-space:pre-wrap"></pre>');
// textContent, not HTML: a value carrying '<' is otherwise read as a tag and vanishes.
document.getElementById('record').textContent = rows.join('\\n');
console.log(rows.join('\\n'));
}}
build();
</script></body></html>
"""

rows = "\n".join(
    f'<div class="row"><div class="box" id="{bid}" style="{decl}; {extra}"></div></div>'
    for bid, decl, extra in [(b[0], b[1], b[2]) for b in BOXES])
sizes = "\n".join(f'<div id="{b[0]}x" style="{b[1]}"></div>' for b in BOXES[4:])
specs = "[" + ",".join(f'["{p}","{v}"]' for p, v in
    [("corner-shape", "scoop"), ("corner-top-left-shape", "notch"),
     ("corner-shape", "superellipse(2)"), ("corner-shape", "uncurved"),
     # The shorthand's computed value is its four longhands' own values, so a corner written on the
     # longhand has to show in the shorthand beside it (CSS Rounding 1 §4).
     ("corner-top-right-shape", "bevel"), ("corner-shape", "round"),
     ("width", "calc-size(auto, size + 20px)"), ("width", "calc-size(auto, 100% - 20px)"),
     ("width", "calc-size(100px, size)"), ("line-height", "calc-size(auto, size)")]) + "]"
ids = "[" + ",".join(f'"{b[0]}"' for b in BOXES) + "]"

out = os.path.join(os.path.dirname(os.path.abspath(__file__)), "_b313_corner_calcsize.html")
with open(out, "w") as f:
    f.write(html.format(rows=rows, sizes=sizes, specs=specs, ids=ids))
print(out)
