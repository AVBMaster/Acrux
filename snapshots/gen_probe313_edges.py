"""Generate the b313 edges probe: the detail the first sweep left open.

The sweep found the families; this table asks each family's boundary, because a value model is only
finished when the collapsed forms, the case spellings and the refusals agree too. The record is
filled through textContent so a value carrying '<length-percentage>' reaches the row intact.
"""
import json
import os

CASES = [
    # CSS Rounding 1 §3.1, §4: the shorthand's own collapse and the longhands' names.
    ("corner-shape", "round round round round"), ("corner-shape", "round round"),
    ("corner-shape", "round square round square"), ("corner-shape", "round square square round"),
    ("corner-shape", "ROUND"), ("corner-shape", "superellipse(1)"),
    ("corner-shape", "superellipse(1e0)"), ("corner-shape", "superellipse(0.5)"),
    ("corner-shape", "superellipse(none)"), ("corner-shape", "superellipse(calc(1 + 1))"),
    ("corner-shape", "SUPERELLIPSE(2)"), ("corner-shape", "superellipse()"),
    ("corner-shape", "superellipse(1.5) square"), ("corner-shape", "superellipse(-infinity)"),
    ("corner-shape", "superellipse(1.5"), ("corner-shape", "round,"),
    ("corner-shape", "round scoop notch bevel square"),
    ("corner-start-end-shape", "scoop"), ("corner-end-start-shape", "notch"),
    ("corner-end-end-shape", "square"), ("corner-top-left-shape", "superellipse(3)"),
    ("corner-shape", "inherit"), ("~", "corner-top-left-shape"),
    # CSS Values 4 §11.1: what may stand in front of the comma, and what the sum may hold.
    ("width", "calc-size(<length-percentage>, 100%)"), ("height", "calc-size(stretch, size)"),
    ("height", "calc-size(fit-content(20px), size)"), ("width", "calc-size(auto, 100%)"),
    ("width", "calc-size(auto, size + size)"), ("width", "calc-size(auto, calc(size + 1px))"),
    ("width", "calc-size(auto, 10px + 20px)"), ("width", "calc-size(auto, max(size, 10px))"),
    ("width", "calc-size( auto , size + 10px )"), ("width", "calc-size(auto, size - 1em)"),
    ("width", "calc-size(auto, -size)"), ("width", "calc-size(auto, 2 * size)"),
    ("width", "calc-size(auto, size / 2)"), ("width", "calc-size(auto, 100% - size)"),
    ("width", "calc-size(auto, size + anchor-size(width))"),
    ("width", "calc-size(auto, size !important)"),
    ("min-height", "calc-size(auto, size)"), ("line-height", "calc-size(auto, size + 1px)"),
    ("width", "calc-size(10px, size)"), ("width", "calc-size(auto, size, size)"),
    # CSS Scroll-driven Animations 1 §3.2: the defaults a function does not print.
    ("animation-timeline", "scroll(root)"), ("animation-timeline", "scroll(inline)"),
    ("animation-timeline", "scroll(self block)"), ("animation-timeline", "scroll(nearest inline)"),
    ("animation-timeline", "scroll(root block)"), ("animation-timeline", "view()"),
    ("animation-timeline", "view(none auto)"), ("animation-timeline", "view(10px 20px)"),
    ("animation-timeline", "view(auto)"), ("animation-timeline", "view(cover)"),
    ("animation-timeline", "view(cover 10%)"), ("animation-timeline", "scroll(), view()"),
    ("animation-timeline", "scroll(nearest block, inline)"),
    ("animation-timeline", "SCROLL()"),
    # CSS Anchor Positioning 1 §4.3 and §7: the pair's own words and their order.
    ("position-visibility", "no-overflow anchors-visible"),
    ("position-visibility", "anchors-visible anchors-visible"),
    ("position-visibility", "AUTO"),
    ("position-visibility", "anchors-visible no-overflow auto"),
    # CSS Anchor Positioning 1 §5: which properties read an anchor's edge.
    ("top", "anchor(--a bottom)"), ("right", "anchor(center)"),
    ("inset-block-start", "anchor(--a start)"), ("padding-left", "anchor(--a left)"),
    ("top", "calc(anchor(--a top) + 10px)"), ("top", "anchor(--a start end)"),
    ("margin-top", "anchor(--a top)"), ("top", "anchor(10px)"),
    # CSS Overflow 3 §3.7: the single word the reference takes.
    ("text-overflow", "clip"), ("text-overflow", "ellipsis"), ("text-overflow", "CLIP"),
    ("text-overflow", "ellipsis clip"), ("text-overflow", '""'),
    ("text-overflow", "none"),
    # CSS Grid 2 §4.2: a subgrid's own line names.
    ("grid-template-columns", "subgrid [a] [b]"), ("grid-template-rows", "subgrid [x]"),
    ("grid-template-columns", "subgrid a"), ("grid-template-columns", "SUBGRID"),
    ("grid-template-columns", "subgrid, 10px"),
    # CSSOM View §7.3: what an inset answers when the box is positioned and the value is auto.
    ("~", "top"), ("~", "left"), ("~", "right"), ("~", "bottom"),
]


def page():
    literal = "[" + ",".join(f'[{json.dumps(p)},{json.dumps(v)}]' for p, v in CASES) + "]"
    read = """  if (cases[i][1] !== '~') el.style.setProperty(cases[i][0], cases[i][1]);
  rows.push(i + '|' + cases[i][0] + '|' + cases[i][1]
      + '|sup=' + (cases[i][1] === '~' ? '-' : (CSS.supports(cases[i][0], cases[i][1]) ? 1 : 0))
      + '|echo=' + el.style.getPropertyValue(cases[i][0])
      + '|comp=' + getComputedStyle(el).getPropertyValue(cases[i][0]));"""
    template = """<!doctype html>
<html><head><meta charset="utf-8"><title>b313 edges</title>
<style>#host > div {{ position: relative; }}</style></head>
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
// The record is filled through textContent: a value carrying '<length-percentage>' is otherwise
// parsed as a start tag and vanishes, which turns one row into another row.
document.getElementById('record').textContent = rows.join('\\n');
console.log(rows.join('\\n'));
host.remove();
}}
build();
</script>
</body></html>
"""
    return template.format(cases=literal, read=read)


out = os.path.join(os.path.dirname(os.path.abspath(__file__)), "_probe_b313_edges.html")
with open(out, "w", encoding="utf-8") as handle:
    handle.write(page())
print(out, len(CASES), "rows")
