"""Generate the b312 math probes.

The first table asks where anchor-size() is taken at all (CSS Anchor Positioning 1 §5.1 lists the
sizes, the insets and the margins, and the reference engine refuses every other property) and how a
term the engine cannot work out yet is printed inside a sum. The second asks the same of a product
inside a sum, and of 'minmax()' — which is a track-list function and no length.
"""
import json
import os

MATH = [
    ("width", "minmax(10px, 20px)"), ("width", "minmax(auto, 10px)"),
    ("height", "minmax(1px, 2px)"), ("left", "minmax(1px, 2px)"),
    ("grid-template-columns", "minmax(10px, 20px)"),
    ("width", "calc(10px / 2)"), ("width", "calc(20px / 3)"),
    ("width", "calc(anchor-size(width) / 2)"),
    ("width", "calc(anchor-size(width) / anchor-size(width))"),
    ("width", "calc(1px / anchor-size(width))"),
    ("width", "calc(anchor-size(width) * 2 / 4)"),
    ("width", "calc(10px + anchor-size(width) / 2)"),
    ("width", "calc(2 * anchor-size(width))"),
    ("width", "anchor-size(width)"),
    ("padding", "anchor-size(width)"),
    ("margin", "calc(anchor-size(width) + 10px)"),
    ("top", "calc(anchor-size(self-inline) - 5px)"),
]

MATH2 = [
    ("width", "calc(10px + 2 * 1em)"), ("width", "calc(2 * 1em + 10px)"),
    ("width", "calc(10px + 1em * 2)"), ("width", "calc(10px + var(--x) * 2)"),
    ("width", "calc(2 * var(--x) + 10px)"), ("width", "calc(10px + var(--x) + var(--y))"),
    ("width", "calc(var(--x) / 2 + 10px)"), ("width", "calc(10px + (1em + 2px))"),
    ("width", "calc(10px - (1em + 2px))"), ("width", "calc(anchor-size(width) * 2)"),
    ("width", "calc(anchor-size(width) * 2 + 10px)"),
    ("width", "width"), ("width", "minmax"), ("width", "repeat"),
]

UNWRAP = [
    "calc(10px)", "calc(anchor-size(width))", "calc(10px + 20px)", "calc(min(1px, 2px))",
    "calc(1px * 2)", "calc((10px))", "calc(var(--x))", "calc(anchor-size(width) * 1)",
    "calc(10px + anchor-size(width))", "calc(100% )", "calc(2 * (1px + 2px))",
]


def page(title, rows_src, with_computed):
    literal = "[" + ",".join(
        f'[{json.dumps(p)},{json.dumps(v)}]' for p, v in rows_src) + "]"
    computed = ("\n      + '|comp=' + getComputedStyle(el).getPropertyValue(cases[i][0])"
                if with_computed else "")
    read = """  el.style.setProperty(cases[i][0], cases[i][1]);
  rows.push(i + '|' + cases[i][0] + '|' + cases[i][1]
      + '|sup=' + (CSS.supports(cases[i][0], cases[i][1]) ? 1 : 0)
      + '|echo=' + el.style.getPropertyValue(cases[i][0])%s);""" % computed
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
    el.remove();
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
with open(os.path.join(HERE, "_probe_b312_math.html"), "w", encoding="utf-8") as handle:
    handle.write(page("b312 math", MATH, True))
with open(os.path.join(HERE, "_probe_b312_math2.html"), "w", encoding="utf-8") as handle:
    handle.write(page("b312 math2", MATH2, False))
with open(os.path.join(HERE, "_probe_b312_unwrap.html"), "w", encoding="utf-8") as handle:
    handle.write(page("b312 unwrap",
                      [("width", value) for value in UNWRAP], True))
print("math:", len(MATH), "math2:", len(MATH2), "unwrap:", len(UNWRAP))
