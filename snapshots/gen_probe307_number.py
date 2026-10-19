#!/usr/bin/env python3
"""Write the b307 probe page and the script that measures its reference dump.

Two rules about how a number is written, measured in the same seat. The first is #307: CSS Syntax 3
§4.3.12 lets a number token run to its decimal point, but no reference engine reads a value there —
'12.', '12.px', '12.s' and '12.deg' are each dropped while '12', '12px', '12s' and '12deg' stand. The
second is what a length may be spelled as at all: a bare number says nothing about what it measures,
so 'width: 12' and 'margin-left: 12' fall, while the bare '0' CSS 2.1 kept still stands and the
seats whose whole value IS a number — 'line-height', 'zoom', 'tab-size', an SVG stroke — take it.
The rows are grouped by that difference, and each is read through the grammar, the echo and the
computed value.
"""
import os

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

# (property, value, group) — the group is printed so a diff says which rule a row belongs to.
CASES = [
    # A number that ends on its decimal point.
    ("margin-left", "12.px", "point"),
    ("margin-left", "123456789.px", "point"),
    ("margin-left", ".px", "point"),
    ("margin-left", "12.5px", "point"),
    ("margin-left", "12..5px", "point"),
    ("margin-left", "12.px 4px", "point"),
    ("width", "12.", "point"),
    ("opacity", "12.", "point"),
    ("opacity", ".5", "point"),
    ("line-height", "12.", "point"),
    ("z-index", "12.", "point"),
    ("flex-grow", "12.", "point"),
    ("margin-left", "calc(12.px + 1px)", "point"),
    ("margin-left", "calc(12px + 1.)", "point"),
    ("margin-left", "min(12.px, 4px)", "point"),
    ("transition-duration", "12.s", "point"),
    ("transition-delay", "12.s", "point"),
    ("border-width", "12.px", "point"),
    ("font-size", "12.px", "point"),
    ("letter-spacing", "12.px", "point"),
    ("margin-left", "12.PX", "point"),
    ("margin-left", "+12.px", "point"),
    ("margin-left", "-12.px", "point"),
    ("margin-left", "12.e2px", "point"),
    ("rotate", "12.deg", "point"),
    ("margin-left", "12.5.px", "point"),
    # A bare number where the property measures in lengths.
    ("width", "12", "bare"),
    ("margin-left", "12", "bare"),
    ("font-size", "12", "bare"),
    ("border-width", "12", "bare"),
    ("line-height", "12", "bare"),
    ("flex-grow", "12px", "bare"),
    ("width", "12.5", "bare"),
    ("padding-top", "12", "bare"),
    ("top", "12", "bare"),
    ("stroke-width", "12", "bare"),
    ("letter-spacing", "12", "bare"),
    ("word-spacing", "12", "bare"),
    ("min-width", "12", "bare"),
    ("outline-width", "12", "bare"),
    ("row-gap", "12", "bare"),
    ("text-indent", "12", "bare"),
    ("vertical-align", "12", "bare"),
    ("height", "12.5", "bare"),
    ("column-width", "12", "bare"),
    ("gap", "12", "bare"),
    ("flex-basis", "12", "bare"),
    ("border-radius", "12", "bare"),
    ("translate", "12", "bare"),
    ("inset-inline-start", "12", "bare"),
    ("shape-margin", "12", "bare"),
    ("scroll-padding-left", "12", "bare"),
    ("perspective", "12", "bare"),
    ("border-spacing", "12", "bare"),
    ("inset", "12", "bare"),
    # The bare zero, and the seats where a number IS the value.
    ("width", "0", "zero"),
    ("margin-top", "0", "zero"),
    ("top", "0", "zero"),
    ("font-size", "0", "zero"),
    ("flex-basis", "0", "zero"),
    ("zoom", "12", "zero"),
    ("tab-size", "4", "zero"),
    ("border-image-width", "2", "zero"),
    ("stroke-dashoffset", "12", "zero"),
    ("margin", "1px 2px 3px 4px", "shape"),
    ("padding", "1px 2px 3px", "shape"),
    ("inset", "1px 2px", "shape"),
    ("margin-left", "1px 2px", "shape"),
    ("width", "calc(2 * 1px)", "shape"),
    ("margin-left", "calc(1 + 1px)", "shape"),
    ("rotate", "x 12deg", "shape"),
    ("column-width", "auto", "shape"),
    ("gap", "12px", "shape"),
]

GROUPS = [c[2] for c in CASES]
LITERAL = "[[" + "],[".join(f'"{p}","{v}","{g}"' for p, v, g in CASES) + "]]"

READ = ("  el.style.setProperty(cases[i][0], cases[i][1]);\n"
        "  rows.push(i + '|' + cases[i][2] + '|' + cases[i][0] + '|' + cases[i][1]\n"
        "      + '|sup=' + (CSS.supports(cases[i][0], cases[i][1]) ? 1 : 0)\n"
        "      + '|echo=' + el.style.getPropertyValue(cases[i][0])\n"
        "      + '|comp=' + getComputedStyle(el).getPropertyValue(cases[i][0]));")

PAGE = """<!doctype html>
<html><head><meta charset="utf-8"><title>b307 number spelling probe</title></head>
<body>
<div id="host"></div>
<script>
// How a number is written, and what a length is allowed to be written as.
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
""" % (LITERAL, READ)

REFERENCE = """() => {
const host = document.createElement('div');
document.body.appendChild(host);
const rows = [];
const cases = %s;
for (let i = 0; i < cases.length; i++) {
  const el = document.createElement('div');
  host.appendChild(el);
%s
}
host.remove();
return rows.join('\\n');
}""" % (LITERAL, READ)


def main():
    with open(os.path.join(ROOT, "snapshots", "_probe_b307_number.html"), "w", encoding="utf-8") as f:
        f.write(PAGE)
    with open(os.path.join(ROOT, "snapshots", "_b307_number_ref_script.js"), "w", encoding="utf-8") as f:
        f.write(REFERENCE)
    print(f"{len(CASES)} rows ({', '.join(f'{g}:{GROUPS.count(g)}' for g in dict.fromkeys(GROUPS))})"
          " -> snapshots/_probe_b307_number.html + _b307_number_ref_script.js")


if __name__ == "__main__":
    main()
