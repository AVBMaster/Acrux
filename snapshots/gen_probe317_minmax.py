"""Generate the b317 minmax probe: where a track function's arguments stop, and where a slice's
'fill' stops.

The edges table said 'minmax(1fr, auto)' is refused while 'minmax(auto, 1fr)' stands, and that
'border-image-slice: 1 fill 2' is refused while 'fill 1' stands. This table asks each family's
boundary directly — which minimum a max of 'auto' may sit on, whether a flex may be a maximum at
all, and which seat in the list the word 'fill' may hold — plus the negative a calc() writes into a
column width. One fresh element per row, five columns.
"""
import json
import os

CASES = [
    # CSS Grid 1 §7.2.3: minmax()'s two arguments are not the same grammar.
    ("grid-auto-rows", "minmax(1fr, auto)"), ("grid-auto-rows", "minmax(auto, auto)"),
    ("grid-auto-rows", "minmax(min-content, auto)"),
    ("grid-auto-rows", "minmax(auto, min-content)"),
    ("grid-auto-rows", "minmax(1fr, 1fr)"), ("grid-auto-rows", "minmax(10px, 1fr)"),
    ("grid-auto-rows", "minmax(1fr, 10px)"), ("grid-auto-rows", "minmax(1fr, 2fr)"),
    ("grid-auto-rows", "minmax(50%, auto)"), ("grid-auto-rows", "minmax(auto, 50%)"),
    ("grid-auto-rows", "minmax(auto, fit-content(100px))"),
    ("grid-auto-rows", "minmax(auto, max-content)"),
    ("grid-auto-rows", "minmax(max-content, auto)"),
    ("grid-auto-rows", "minmax(auto, calc(10px))"),
    ("grid-auto-rows", "minmax(calc(-1px), auto)"),
    ("grid-auto-rows", "minmax(1fr)"), ("grid-auto-rows", "minmax(1fr,)"),
    ("grid-auto-rows", "minmax(1fr, 2fr, 3fr)"),
    ("grid-template-columns", "minmax(1fr, auto)"),
    ("grid-template-rows", "minmax(auto, 1fr)"),
    ("grid-auto-columns", "minmax(max-content, 1fr)"),
    ("grid-template-columns", "100px minmax(1fr, auto)"),
    ("grid-template-columns", "minmax(auto, auto)"),
    ("grid-template-columns", "repeat(2, minmax(1fr, auto))"),
    ("grid-template-columns", "repeat(2, minmax(auto, 1fr))"),
    ("grid-template-rows", "minmax(auto, minmax(1px, 2px))"),
    ("grid-template-rows", "minmax(minmax(1px, 2px), auto)"),
    # CSS Backgrounds 3 §5.3: 'fill' is a flag, so it holds one end of the list or nothing.
    ("border-image-slice", "1 2 fill"), ("border-image-slice", "1 fill 2"),
    ("border-image-slice", "fill 1 2"), ("border-image-slice", "1 fill"),
    ("border-image-slice", "1 2 3 4 fill"), ("border-image-slice", "fill 1 2 3 4"),
    ("border-image-slice", "1 2 fill 3"), ("border-image-slice", "1 fill 2 fill"),
    ("border-image-slice", "fill fill"), ("border-image-slice", "FILL 1"),
    ("border-image-slice", "10% fill"), ("border-image-slice", "fill 10%"),
    ("border-image-slice", "1 2 3 4 5 fill"),
    # CSS Backgrounds 3 §5.4/§5.5: the other box parts have no flag at all.
    ("border-image-outset", "1 2 3"), ("border-image-outset", "fill 1"),
    ("border-image-width", "1 fill 2"),
    # CSS Multi-column 1 §6.1: a calc() can write a negative where the grammar refuses one.
    ("column-width", "calc(-1px)"), ("column-width", "calc(-100px)"),
    ("column-width", "calc(0px - 2px)"), ("column-width", "min(-1px, -2px)"),
    ("column-width", "-1px"), ("column-width", "auto"),
    ("columns", "calc(-1px)"), ("columns", "100px calc(-1px)"),
    ("column-gap", "calc(-1px)"), ("row-gap", "calc(-1px)"),
    ("outline-offset", "calc(-1px)"), ("border-spacing", "calc(-1px)"),
    ("word-spacing", "calc(-1px)"), ("letter-spacing", "calc(-1px)"),
    # The same width written in the unit the container has not yet priced.
    ("column-width", "calc(50% - 20px)"), ("column-width", "calc(-50%)"),
    ("column-width", "-2em"), ("column-width", "calc(1px - 2px)"),
    ("columns", "auto calc(-1px)"), ("columns", "calc(-1px) 3"),
    # Is a share of the container a column width at all?
    ("column-width", "50%"), ("column-width", "calc(50%)"), ("column-width", "100px 50%"),
    ("columns", "50%"), ("columns", "3 50%"),
    ("column-gap", "50%"), ("row-gap", "50%"), ("border-spacing", "50%"),
    ("column-gap", "-1px"), ("row-gap", "-1px"), ("border-spacing", "-1px"),
    ("column-gap", "calc(50% - 20px)"),
]


def page():
    literal = "[" + ",".join(f'[{json.dumps(p)},{json.dumps(v)}]' for p, v in CASES) + "]"
    read = """  if (cases[i][1] !== '~') el.style.setProperty(cases[i][0], cases[i][1]);
  rows.push(i + '|' + cases[i][0] + '|' + cases[i][1]
      + '|sup=' + (cases[i][1] === '~' ? '-' : (CSS.supports(cases[i][0], cases[i][1]) ? 1 : 0))
      + '|n=' + el.style.length
      + '|echo=' + el.style.getPropertyValue(cases[i][0])
      + '|cssText=' + el.style.cssText
      + '|comp=' + getComputedStyle(el).getPropertyValue(cases[i][0]));"""
    template = """<!doctype html>
<html><head><meta charset="utf-8"><title>b317 minmax</title></head>
<body>
<div id="host"></div>
<script>
// minmax
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
document.getElementById('record').textContent = rows.join('\\n');
console.log(rows.join('\\n'));
host.remove();
}}
document.body.appendChild(document.createElement('pre')).id = 'record';
document.getElementById('record').setAttribute('style',
    'font:10px/1.35 monospace;white-space:pre-wrap');
build();
</script>
</body></html>
"""
    return template.format(cases=literal, read=read)


out = os.path.join(os.path.dirname(os.path.abspath(__file__)), "_probe_b317_minmax.html")
with open(out, "w") as fh:
    fh.write(page())
print(out, len(CASES), "cases")
