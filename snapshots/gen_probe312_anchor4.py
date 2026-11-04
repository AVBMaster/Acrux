"""b312: what an anchor-size() is worth when the engine has no anchor to read.

CSS Anchor Positioning 1 §5.1.1 gives an anchor-size() two ways out: the fallback the page wrote
after the comma, and 'invalid at computed-value time' when there is none. The reference engine has
anchors and this engine has none, so every case here is the no-anchor branch of the rule and the
computed column says which of the two ways out the engine took.
"""
import os

CASES = [
    ("margin-left", "anchor-size(width)"),
    ("margin-left", "calc(anchor-size(width) + 10px)"),
    ("margin-left", "anchor-size(width, 10px)"),
    ("margin-left", "calc(anchor-size(width, 10px) + 10px)"),
    ("margin-left", "anchor-size(--nope width, 10px)"),
    ("margin-left", "anchor-size(--nope, 10px)"),
    ("width", "anchor-size(height)"),
    ("width", "calc(anchor-size(inline) + 1em)"),
    ("width", "calc(10px + anchor-size(self-inline, 30px))"),
    ("top", "anchor-size(width)"),
    ("top", "anchor-size(width, 5px)"),
    ("padding-left", "anchor-size(width, 10px)"),
    ("font-size", "anchor-size(width, 10px)"),
    ("margin-block-start", "anchor-size(block)"),
    ("width", "anchor-size()"),
    ("width", "anchor-size(, 20px)"),
    ("height", "calc(anchor-size(width) * 2)"),
    ("min-width", "anchor-size(width, 8px)"),
    ("max-height", "anchor-size(height, 12px)"),
    ("margin", "calc(anchor-size(width) + 10px)"),
    ("left", "calc(anchor-size(self-block) + 1px)"),
    ("margin-left", "calc(anchor-size(width) + anchor-size(height, 4px))"),
]

rows_js = "[" + ",".join('["%s","%s"]' % c for c in CASES) + "]"

html = """<!doctype html>
<html><head><meta charset="utf-8"><title>b312 anchor4</title></head>
<body>
<div id="box" style="width:200px;font-size:16px;position:relative"></div>
<script>
// b312_anchor4
function build() {{
const box = document.getElementById('box');
const rows = [];
const cases = {cases};
for (let i = 0; i < cases.length; i++) {{
const el = document.createElement('div');
el.style.position = 'absolute';
box.appendChild(el);
  el.style.setProperty(cases[i][0], cases[i][1]);
  const read = getComputedStyle(el).getPropertyValue(cases[i][0]);
  rows.push(i + '|' + cases[i][0] + '|' + cases[i][1]
      + '|sup=' + (CSS.supports(cases[i][0], cases[i][1]) ? 1 : 0)
      + '|echo=' + el.style.getPropertyValue(cases[i][0])
      + '|comp=' + read);
    el.remove();
}}
document.body.insertAdjacentHTML('beforeend',
    '<pre id="record" style="font:10px/1.35 monospace;white-space:pre-wrap">'
    + rows.join('\\n') + '</pre>');
console.log(rows.join('\\n'));
}}
build();
</script>
</body></html>
"""

out = os.path.join(os.path.dirname(os.path.abspath(__file__)), "_probe_b312_anchor4.html")
with open(out, "w") as f:
    f.write(html.format(cases=rows_js))
print(out, len(CASES), "rows")
