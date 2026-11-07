"""Generate the b326 transform-family edge forms.

The transform-origin depth fix (b325) left the neighbouring seats unmeasured:
whether perspective-origin takes the same three-component depth, whether the
individual transforms accept the unitless zero their sibling does, and which
multi-value transform() functions the grammar takes. One fresh element per row
(measured in b283: a reused host answers a dropped declaration with the previous
row's value).
"""
import json
import os

CASES = [
    ("perspective-origin", "center"), ("perspective-origin", "50% 50%"),
    ("perspective-origin", "left top"), ("perspective-origin", "10px 20px"),
    ("perspective-origin", "center center 10px"), ("perspective-origin", "left top 0"),
    ("perspective-origin", "right 10px bottom 20px"), ("perspective-origin", "bogus"),
    ("transform", "none"), ("transform", "translate(10px)"), ("transform", "translate(10px, 20px)"),
    ("transform", "scale(2)"), ("transform", "scale(2, 3)"), ("transform", "rotate(45deg)"),
    ("transform", "matrix(1, 0, 0, 1, 10, 20)"), ("transform", "translateX(10px) translateY(5px)"),
    ("transform", "translate(10px) rotate(45deg)"), ("transform", "bogus"),
    ("transform", "translate()"), ("transform", "scale()"),
    ("translate", "none"), ("translate", "10px"), ("translate", "10px 20px"),
    ("translate", "10px 20px 30px"), ("translate", "0"), ("translate", "50% 50%"),
    ("scale", "none"), ("scale", "2"), ("scale", "2 3"), ("scale", "0"), ("scale", "0 3"),
    ("rotate", "none"), ("rotate", "0deg"), ("rotate", "x 0deg"), ("rotate", "45"),
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
<html><head><meta charset="utf-8"><title>b326 transform family</title></head>
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
with open(os.path.join(HERE, "_probe_b326_transform.html"), "w", encoding="utf-8") as handle:
    handle.write(page())
print("cases:", len(CASES))
