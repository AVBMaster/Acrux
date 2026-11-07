"""Generate the b328 P3 sweep: the alignment / justification family, the gap
pair, the place-* shorthands, and the grid item placement longhands — value by
value.

The align-* and justify-* longhands each get the box alignment keywords, the
baseline keywords, the safe/unsafe overflow modifiers, a two-word combination,
and a bogus word. The place-* shorthands get their two-value form and the single
value that sets both axes. grid-row/column/area get the span forms, the line
numbers, the named areas, the auto keyword, and the shapes the spec refuses. One
fresh element per row (measured in b283).
"""
import json
import os

CASES = [
    # align-content / justify-content: the box alignment keywords.
    ("align-content", "normal"), ("align-content", "stretch"), ("align-content", "center"),
    ("align-content", "start"), ("align-content", "end"), ("align-content", "flex-start"),
    ("align-content", "flex-end"), ("align-content", "baseline"), ("align-content", "first baseline"),
    ("align-content", "last baseline"), ("align-content", "space-between"),
    ("align-content", "space-around"), ("align-content", "space-evenly"),
    ("align-content", "safe center"), ("align-content", "unsafe start"),
    ("align-content", "center center"), ("align-content", "bogus"), ("align-content", "normal stretch"),
    ("justify-content", "normal"), ("justify-content", "stretch"), ("justify-content", "center"),
    ("justify-content", "start"), ("justify-content", "end"), ("justify-content", "left"),
    ("justify-content", "right"), ("justify-content", "flex-start"), ("justify-content", "flex-end"),
    ("justify-content", "space-between"), ("justify-content", "space-around"),
    ("justify-content", "space-evenly"), ("justify-content", "safe center"),
    ("justify-content", "baseline"), ("justify-content", "bogus"), ("justify-content", "center center"),
    # align-items / justify-items: self alignment.
    ("align-items", "normal"), ("align-items", "stretch"), ("align-items", "center"),
    ("align-items", "start"), ("align-items", "end"), ("align-items", "flex-start"),
    ("align-items", "baseline"), ("align-items", "first baseline"), ("align-items", "last baseline"),
    ("align-items", "safe center"), ("align-items", "center center"), ("align-items", "bogus"),
    ("justify-items", "normal"), ("justify-items", "stretch"), ("justify-items", "center"),
    ("justify-items", "start"), ("justify-items", "end"), ("justify-items", "left"),
    ("justify-items", "right"), ("justify-items", "baseline"), ("justify-items", "legacy"),
    ("justify-items", "legacy right"), ("justify-items", "safe center"), ("justify-items", "bogus"),
    # align-self / justify-self: the item's own alignment.
    ("align-self", "auto"), ("align-self", "normal"), ("align-self", "stretch"),
    ("align-self", "center"), ("align-self", "start"), ("align-self", "end"),
    ("align-self", "flex-start"), ("align-self", "baseline"), ("align-self", "first baseline"),
    ("align-self", "last baseline"), ("align-self", "safe center"), ("align-self", "bogus"),
    ("align-self", "center center"),
    ("justify-self", "auto"), ("justify-self", "normal"), ("justify-self", "stretch"),
    ("justify-self", "center"), ("justify-self", "start"), ("justify-self", "end"),
    ("justify-self", "left"), ("justify-self", "right"), ("justify-self", "baseline"),
    ("justify-self", "safe center"), ("justify-self", "bogus"), ("justify-self", "center center"),
    # The place-* shorthands: one value sets both axes, two set each.
    ("place-content", "center"), ("place-content", "center start"),
    ("place-content", "space-between center"), ("place-content", "normal"),
    ("place-content", "normal normal"), ("place-content", "bogus"),
    ("place-content", "center center center"),
    ("place-items", "center"), ("place-items", "center start"), ("place-items", "normal"),
    ("place-items", "baseline center"), ("place-items", "bogus"),
    ("place-items", "center center center"),
    ("place-self", "center"), ("place-self", "center start"), ("place-self", "auto"),
    ("place-self", "normal normal"), ("place-self", "bogus"),
    ("place-self", "center center center"),
    # gap / row-gap / column-gap: a length, a percentage, two values, a calc, a negative.
    ("gap", "10px"), ("gap", "10px 20px"), ("gap", "10%"), ("gap", "10% 20%"),
    ("gap", "0"), ("gap", "calc(10px + 2px)"), ("gap", "-10px"), ("gap", "normal"),
    ("gap", "bogus"), ("gap", "10px 20px 30px"),
    ("row-gap", "10px"), ("row-gap", "10%"), ("row-gap", "0"), ("row-gap", "-10px"),
    ("row-gap", "normal"), ("row-gap", "bogus"),
    ("column-gap", "10px"), ("column-gap", "10%"), ("column-gap", "0"), ("column-gap", "-10px"),
    ("column-gap", "normal"), ("column-gap", "bogus"),
    # grid-row / grid-column: line numbers, span, named lines, auto.
    ("grid-row", "auto"), ("grid-row", "1"), ("grid-row", "1 / 3"), ("grid-row", "2 / span 2"),
    ("grid-row", "span 2"), ("grid-row", "span 3"), ("grid-row", "-1"), ("grid-row", "a"),
    ("grid-row", "1 / a"), ("grid-row", "bogus"), ("grid-row", "span"), ("grid-row", "0"),
    ("grid-column", "auto"), ("grid-column", "1"), ("grid-column", "1 / 3"),
    ("grid-column", "2 / span 2"), ("grid-column", "span 2"), ("grid-column", "-1"),
    ("grid-column", "a"), ("grid-column", "bogus"), ("grid-column", "0"),
    ("grid-row-start", "auto"), ("grid-row-start", "1"), ("grid-row-start", "span 2"),
    ("grid-row-start", "a"), ("grid-row-start", "-1"), ("grid-row-start", "bogus"),
    ("grid-row-end", "auto"), ("grid-row-end", "3"), ("grid-row-end", "span 2"),
    ("grid-row-end", "a"), ("grid-row-end", "bogus"),
    ("grid-column-start", "auto"), ("grid-column-start", "2"), ("grid-column-start", "span 3"),
    ("grid-column-start", "a"), ("grid-column-start", "bogus"),
    ("grid-column-end", "auto"), ("grid-column-end", "4"), ("grid-column-end", "span 2"),
    ("grid-column-end", "a"), ("grid-column-end", "bogus"),
    # grid-area: a named area, four line numbers, the auto forms.
    ("grid-area", "a"), ("grid-area", "auto"), ("grid-area", "1 / 2 / 3 / 4"),
    ("grid-area", "1 / 2"), ("grid-area", "span 2"), ("grid-area", "bogus"),
    ("grid-area", "a / b / c / d"), ("grid-area", "1 / 2 / 3"),
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
<html><head><meta charset="utf-8"><title>b328 P3 sweep</title></head>
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
with open(os.path.join(HERE, "_probe_b328_align_grid.html"), "w", encoding="utf-8") as handle:
    handle.write(page())
print("cases:", len(CASES))