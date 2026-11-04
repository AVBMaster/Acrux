"""Generate the b311 follow-up probe: the edges of the three families the sweep opened, plus the
transform-origin rows measured on a box with a size of its own (the shared webview has a 0x0
viewport, which turns every percentage origin into an artifact).
"""
import json
import os

CASES = [
    # CSS Images 4 §6.2: the rectangle a replaced element shows through its view box.
    ("object-view-box", "none"), ("object-view-box", "view-box"),
    ("object-view-box", "xywh(0 0 100% 100%)"), ("object-view-box", "xywh(10px 20px 30px 40px)"),
    ("object-view-box", "xywh(10% 20% 30% 40%)"), ("object-view-box", "xywh(1 2 3 4)"),
    ("object-view-box", "xywh(0 0 50% 50%)"), ("object-view-box", "xywh(10px 20px)"),
    ("object-view-box", "xywh(10px)"), ("object-view-box", "xywh(10px 20px 30px)"),
    ("object-view-box", "xywh(10px 20px 30px 40px 50px)"),
    ("object-view-box", "xywh(0 0 0 0)"), ("object-view-box", "xywh(-10px 0 10px 10px)"),
    ("object-view-box", "xywh(0 0 10px 10px / 5px 5px)"),
    ("object-view-box", "xywh(0 0 10px 10px / 5px)"),
    ("object-view-box", "xywh(auto auto 10px 10px)"),
    ("object-view-box", "xywh(0 0 calc(10px + 5px) 10px)"),
    ("object-view-box", "xywh()"), ("object-view-box", "inset(10px)"),
    ("object-view-box", "circle(10px)"), ("object-view-box", "XYWH(0 0 10px 10px)"),
    # CSS Anchor Positioning 1 §5.2: the area, and what the computed surface calls each side.
    ("position-area", "block-start"), ("position-area", "block-end"),
    ("position-area", "inline-start"), ("position-area", "inline-end"),
    ("position-area", "top"), ("position-area", "left"), ("position-area", "right"),
    ("position-area", "bottom"), ("position-area", "center"),
    ("position-area", "span-block"), ("position-area", "span-inline"),
    ("position-area", "span-start"), ("position-area", "span-end"),
    ("position-area", "self-start"), ("position-area", "self-end"),
    ("position-area", "block-start inline-start"), ("position-area", "top center"),
    ("position-area", "left left"), ("position-area", "anchor-center"),
    ("position-area", "block-start block-end"), ("position-area", "auto"),
    ("position-area", "top span-left"),
]


def page():
    literal = "[" + ",".join(f'[{json.dumps(p)},{json.dumps(v)}]' for p, v in CASES) + "]"
    read = """  if (cases[i][1] !== '~') el.style.setProperty(cases[i][0], cases[i][1]);
  rows.push(i + '|' + cases[i][0] + '|' + cases[i][1]
      + '|sup=' + (cases[i][1] === '~' ? '-' : (CSS.supports(cases[i][0], cases[i][1]) ? 1 : 0))
      + '|echo=' + el.style.getPropertyValue(cases[i][0])
      + '|comp=' + getComputedStyle(el).getPropertyValue(cases[i][0]));"""
    # The origin rows on a box with a size of its own, so a percentage answers in real pixels.
    origin = """
const sized = [];
const host2 = document.createElement('div');
host2.style.width = '200px';
host2.style.height = '100px';
document.body.appendChild(host2);
for (const v of ['center', '50% 50%', 'left top', 'right bottom', 'top right', '10px 20px',
                 'left 10px', '10px center', '20% 30%', 'CENTER', '50%', '1in 1in']) {
  const el = document.createElement('div');
  host2.appendChild(el);
  el.style.transformOrigin = v;
  const el2 = document.createElement('div');
  host2.appendChild(el2);
  el2.style.perspectiveOrigin = v;
  const cs = getComputedStyle(el), cs2 = getComputedStyle(el2);
  sized.push('origin|' + v + '|transform=' + cs.transformOrigin + '|perspective='
    + cs2.perspectiveOrigin);
}
host2.remove();
rows = rows.concat(sized);"""
    return """<!doctype html>
<html><head><meta charset="utf-8"><title>b311 edges</title></head>
<body>
<div id="host"></div>
<script>
// edges
function build() {
const host = document.createElement('div');
document.body.appendChild(host);
let rows = [];
const cases = %s;
for (let i = 0; i < cases.length; i++) {
const el = document.createElement('div');
host.appendChild(el);
%s
}
%s
document.body.insertAdjacentHTML('beforeend',
    '<pre id="record" style="font:10px/1.35 monospace;white-space:pre-wrap">'
    + rows.join('\\n') + '</pre>');
console.log(rows.join('\\n'));
host.remove();
}
build();
</script>
</body></html>
""" % (literal, read, origin)


HERE = os.path.dirname(os.path.abspath(__file__))
with open(os.path.join(HERE, "_probe_b311_edges.html"), "w", encoding="utf-8") as handle:
    handle.write(page())
print("cases:", len(CASES))
