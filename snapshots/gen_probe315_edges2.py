"""Generate the b315 edges2 probe: the boundary behind the boundary the edges table found.

The edges table said which pairs fold and which words the container family takes; this table asks
the cases it could not answer — a legacy repeat spelling on the background axis, an 'auto' that is
the second term rather than both, a container name list that mixes a dashed token with a plain one,
and a math function that is not a sum. One fresh element per row, four columns.
"""
import json
import os

CASES = [
    # CSS Backgrounds 3 §4.1.4: the repeat pair's legacy spellings, and the size pair's 'auto'.
    ("background-repeat", "repeat no-repeat"), ("background-repeat", "no-repeat repeat"),
    ("background-repeat", "repeat-y"), ("background-repeat", "round round"),
    ("background-repeat", "no-repeat space"), ("background-repeat", "space repeat"),
    ("background-repeat", "repeat-x space"),
    ("background-size", "10px auto"), ("background-size", "auto 10px"),
    ("background-size", "50% auto"), ("background-size", "auto auto"),
    ("background-size", "10px 10px"), ("background-size", "auto"),
    ("mask-size", "10px auto"), ("mask-size", "auto 10px"),
    ("mask-repeat", "repeat no-repeat"), ("mask-repeat", "no-repeat repeat"),
    # CSS Containment 3 §3.4: a name list that mixes the two ident spellings, and a type list.
    ("container-name", "--c cards"), ("container-name", "none --c"),
    ("container-name", "--c --c"), ("container-name", "--c none"),
    ("container-name", "--1c"), ("container-name", "-"),
    ("container-type", "size size"), ("container-type", "normal size"),
    ("container-type", "scroll-state size"), ("container-type", "inline-size scroll-state"),
    ("container-type", "size normal"), ("container-type", "size inline-size"),
    ("container", "--c / --c"), ("container", "normal --c"), ("container", "--c / none"),
    ("container", "--c --d / size"), ("container", "size inline-size"),
    ("container", "inline-size --c"), ("container", "--c / size / inline-size"),
    # CSS Values 4 §1.2 and Shapes 1 §4.5: the other math spellings of a number.
    ("interpolate-size", "allow-keywords numeric-only"),
    ("shape-image-threshold", "calc(0.5)"), ("shape-image-threshold", "clamp(0.1, 0.5, 1)"),
    ("shape-image-threshold", "max(-1, 0.5)"), ("shape-image-threshold", "calc(100%)"),
    ("shape-image-threshold", "calc(2%)"), ("shape-image-threshold", "calc(1px)"),
    ("shape-image-threshold", "calc(-1)"),
    # CSS Multi-column 1 §7.1: the count's own range, and which side the length sits on.
    ("columns", "1 auto"), ("columns", "auto 1"), ("columns", "3"), ("columns", "100"),
    ("columns", "2147483647"), ("columns", "2147483648"), ("columns", "0 auto"),
    ("column-count", "2147483648"),
    # CSS Padding/Margin 3 §4 and CSS Logical 1 §4: 'auto' on one side of a pair only.
    ("padding-inline", "10px auto"), ("padding-inline", "auto auto"),
    ("padding-inline", "0"), ("margin-inline", "10px auto"),
    ("inset-block", "10px auto"), ("inset-block", "auto 10px"), ("inset-block", "0 auto"),
    ("overscroll-behavior-inline", "auto contain"), ("overscroll-behavior", "contain none"),
    ("overscroll-behavior", "contain contain"), ("overscroll-behavior", "auto auto"),
    # CSS Containment 3 §4.4: the 'auto' flag said once for the pair.
    ("contain-intrinsic-size", "auto 100px auto"), ("contain-intrinsic-size", "100px auto"),
    ("contain-intrinsic-size", "auto 100px 200px 300px"),
    ("background-repeat", "~"), ("background-size", "~"), ("container-name", "~"),
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
<html><head><meta charset="utf-8"><title>b315 edges2</title></head>
<body>
<script>
// edges2
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
with open(os.path.join(HERE, "_probe_b315_edges2.html"), "w", encoding="utf-8") as handle:
    handle.write(page())
print("cases:", len(CASES))
