"""Generate the b314 edges probe: the boundary of the four families the sweep found open.

The sweep said which families differ; this table asks where the boundary of each one is, because a
value model is only finished when the collapsed spellings, the implied keywords and the refusals
agree too. One fresh element per row, three columns.
"""
import json
import os

CASES = [
    # CSS Shapes 2 §4 and Motion Path 1 §3.2: the rectangle's own collapse, and a polygon.
    ("object-view-box", "inset(0%)"), ("object-view-box", "inset(0 0 0 0)"),
    ("object-view-box", "inset(10px 10px 10px 10px)"),
    ("object-view-box", "inset(10px 20px 10px 20px)"),
    ("object-view-box", "inset(10px 20px 20px 10px)"),
    ("object-view-box", "xywh(10px 10px 80px 80px)"),
    ("object-view-box", "xywh(0% 0% 100% 100%)"),
    ("object-view-box", "xywh(0 0 100% 100%)"),
    ("object-view-box", "xywh(10px 10px 0 0)"),
    ("clip-path", "inset(0 0 0 0)"), ("clip-path", "inset(10px 10px 10px 10px round 4px)"),
    ("clip-path", "polygon(0 0, 10px 10px, 0 20px)"),
    ("clip-path", "polygon(0 0 10px 10px)"),
    ("clip-path", "polygon evenodd(0 0, 10px 10px, 0 20px)"),
    ("shape-outside", "inset(10px 10px 10px 10px)"),
    ("shape-outside", "polygon(0 0, 10px 10px, 0 20px)"),
    ("offset-path", "polygon(0 0, 10px 10px, 0 20px)"),
    ("offset-path", "polygon(0 0, 10px)"),
    ("offset-path", "polygon evenodd(0 0, 10px 10px, 0 20px)"),
    ("offset-path", "polygon(0 0, 10px 10px, 0 20px) content-box"),
    ("offset-path", "inset(0 0 0 0)"),
    ("offset-path", "circle(50% at 50% 50%)"),
    ("offset-path", "path('M 0 0 L 10 10')"),
    ("offset-path", "url(#a)"),
    ("offset-path", "ray(45deg)"), ("offset-path", "ray(45deg contain)"),
    ("offset-path", "ray(45deg cover)"),
    ("offset-path", "ray(45deg closest-side contain)"),
    ("offset-path", "ray(45deg farthest-side contain)"),
    ("offset-path", "ray(45deg closest-side cover)"),
    ("offset-path", "ray(45deg farthest-side cover)"),
    ("offset-path", "ray(45deg closest-corner contain)"),
    ("offset-path", "ray(45deg contain 20px)"),
    ("offset-path", "ray(45deg 20px contain)"),
    ("offset-path", "ray(45deg with-origin contain)"),
    ("offset-path", "ray(45deg contain reverse)"),
    ("offset-path", "ray(45deg contain contain)"),
    ("offset-path", "ray(0)"),
    # CSS Painting 3 §3.1: what paint() is allowed to carry.
    ("background-image", "paint(my-worklet)"),
    ("background-image", "paint(my-worklet, 10px)"),
    ("background-image", "paint(my-worklet var(--x))"),
    ("background-image", "paint()"),
    ("background-image", "PAINT(my-worklet)"),
    ("mask-image", "paint(my-worklet, 10px)"),
    # CSS Animations 2 §6: the trigger's own words.
    ("animation-trigger", "none"), ("animation-trigger", "auto"),
    ("animation-trigger", "normal"), ("animation-trigger", "--t"),
    ("animation-trigger", "click"), ("animation-trigger", "none, click"),
    ("~", "animation-trigger"),
    # CSS Images 4 §6.2 and the offset family's computed spelling.
    ("~", "object-view-box"), ("~", "math-shift"), ("~", "offset-path"),
    ("math-shift", "normal"), ("math-shift", "compact"),
    ("math-shift", "superscript"), ("math-shift", "SUBSCRIPT"),
    ("math-style", "COMPACT"),
]


def page():
    literal = "[" + ",".join(f'[{json.dumps(p)},{json.dumps(v)}]' for p, v in CASES) + "]"
    read = """  if (cases[i][1] !== '~') el.style.setProperty(cases[i][0], cases[i][1]);
  rows.push(i + '|' + cases[i][0] + '|' + cases[i][1]
      + '|sup=' + (cases[i][1] === '~' ? '-' : (CSS.supports(cases[i][0], cases[i][1]) ? 1 : 0))
      + '|echo=' + el.style.getPropertyValue(cases[i][0])
      + '|comp=' + getComputedStyle(el).getPropertyValue(cases[i][0]));"""
    template = """<!doctype html>
<html><head><meta charset="utf-8"><title>b314 edges</title></head>
<body>
<div id="host"></div>
<script>
// edges
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
with open(os.path.join(HERE, "_probe_b314_edges.html"), "w", encoding="utf-8") as handle:
    handle.write(page())
print("cases:", len(CASES))
