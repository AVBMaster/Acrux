"""Generate the b312 anchor-grammar probe: where anchor-size() is taken, and what it takes.

CSS Anchor Positioning 1 §5.1 allows anchor-size() wherever a <length-percentage> is allowed, but the
reference engine answers differently in a shorthand's list than in a single longhand, so the first
half of the table asks property by property. The second half asks the function's own argument
grammar: a name and a keyword in either order, at most one of each, and a fallback only after a comma.
"""
import json
import os

CASES = [
    ("margin", "anchor-size(width)"), ("padding-left", "anchor-size(width)"),
    ("top", "anchor-size(width)"), ("line-height", "anchor-size(width)"),
    ("border-width", "anchor-size(width)"), ("gap", "anchor-size(width)"),
    ("font-size", "anchor-size(width)"), ("translate", "anchor-size(width)"),
    ("inset-block-start", "anchor-size(width)"), ("max-width", "anchor-size(width)"),
    ("anchor-size(10px)", "auto"),
    ("width", "anchor-size(10px)"), ("width", "anchor-size(width --a)"),
    ("width", "anchor-size(self-inline self-block)"),
    ("width", "anchor-size(--a --b width)"), ("width", "anchor-size(width, 10px, 20px)"),
    ("width", "anchor-size(width width width)"), ("width", "anchor-size(--)"),
    ("width", "anchor-size(inline, 10px)"), ("width", "anchor-size(block, 1em)"),
    ("width", "calc(anchor-size(width) + 1s)"),
    ("width", "calc(anchor-size(width) * anchor-size(width))"),
    ("width", "calc(anchor-size(width) + 10%)"),
    ("width", "calc(anchor-size(width) + 1px + 2px)"),
    ("width", "calc(100% - anchor-size(width))"),
    ("width", "calc(anchor-size(width) / 2)"),
    ("width", "calc(2px - anchor-size(width))"),
    ("width", "minmax(anchor-size(width), 10px)"),
    ("flex-basis", "anchor-size(width)"), ("outline-offset", "anchor-size(width)"),
]


def page(title, cases):
    literal = "[" + ",".join(f'[{json.dumps(p)},{json.dumps(v)}]' for p, v in CASES) + "]"
    read = """  el.style.setProperty(cases[i][0], cases[i][1]);
  rows.push(i + '|' + cases[i][0] + '|' + cases[i][1]
      + '|sup=' + (CSS.supports(cases[i][0], cases[i][1]) ? 1 : 0)
      + '|echo=' + el.style.getPropertyValue(cases[i][0])
      + '|comp=' + getComputedStyle(el).getPropertyValue(cases[i][0]));"""
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
with open(os.path.join(HERE, "_probe_b312_anchor3.html"), "w", encoding="utf-8") as handle:
    handle.write(page("b312 anchor 3", CASES))
print("cases:", len(CASES))
