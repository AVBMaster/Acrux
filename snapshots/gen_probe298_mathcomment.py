#!/usr/bin/env python3
"""Write the b298 probe page and the script that measures its reference dump.

The rows ask one question in as many shapes as it can be asked: can a comment be the whitespace that
CSS Values 4 §10.3 demands on both sides of a math operator? The reference engine says no on both
sides of '+' and '-' and yes to every other job a comment does — a token boundary between two
lengths, a space beside a comma, an edge of a value. Each row is read through the grammar
('sup='), the style attribute's echo and the computed surface, because the same text has to be
refused the same way at all three.
"""
import os

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

CASES = [
    ("width", "calc(1px/*c*/+ 2px)"),
    ("width", "calc(1px +/*c*/2px)"),
    ("width", "calc(1px /*c*/ + 2px)"),
    ("width", "calc(/*c*/1px + 2px)"),
    ("width", "calc(1px + 2px/*c*/)"),
    ("width", "calc(1px+/*c*/2px)"),
    ("width", "calc(1px -/*c*/ 2px)"),
    ("width", "calc(1px/*c*/- 2px)"),
    ("width", "calc(1px /*c*/+ 2px)"),
    ("width", "calc(1px/*x*/ - 2px)"),
    ("width", "calc( 1px /*c*/ -/*c*/ 2px )"),
    ("width", "min(1px/*c*/,2px)"),
    ("width", "clamp(1px/*c*/,2px,3px)"),
    ("width", "calc(100%/*c*/- 10px)"),
    ("width", "calc(1px*/*c*/2)"),
    ("width", "calc(1px +/*c*/ 2px)"),
    ("width", "calc(1px/*c*/ +/*c*/ 2px)"),
    ("width", "calc(1px + 2px)"),
    ("width", "calc(1px+2px)"),
    ("height", "calc(1px/*c*/- 2px)"),
    ("top", "calc(1px/*c*/+ 2px)"),
    ("left", "calc(1px +/*c*/2px)"),
    ("padding-left", "calc(1px/*c*/+ 1px)"),
    ("margin-left", "calc(1px/*c*/+ 1px)"),
    ("flex-basis", "calc(1px/*c*/+ 2px)"),
    ("font-size", "calc(1em/*c*/+ 2px)"),
    ("line-height", "calc(1/*c*/+ 1)"),
    ("transform", "translateX(calc(1px/*c*/+ 2px))"),
    ("width", "calc(1px + calc(2px/*c*/+ 3px))"),
    ("margin", "1px/*c*/2px"),
    ("margin", "1px/*c*/2px/*c*/3px 4px"),
    ("padding", "1px/*c*/2px"),
    ("border-spacing", "1px/*c*/2px"),
    ("clip", "rect(0px/*c*/0px 0px 0px)"),
    ("box-shadow", "1px/*c*/2px 3px rgb(0 0 0)"),
    ("background-position", "10px/*c*/20px"),
    ("grid-template-columns", "1px/*c*/2px"),
    ("width", "calc(1px/*c*/+ 2px) !important"),
    ("--x", "calc(1px/*c*/+ 2px)"),
    ("width", "calc(var(--x)/*c*/+ 1px)"),
]

LITERAL = "[[" + "],[".join(f'"{p}","{v}"' for p, v in CASES) + "]]"

READ = ("  el.style.setProperty(cases[i][0], cases[i][1]);\n"
        "  rows.push(i + '|' + cases[i][0] + '|' + cases[i][1]\n"
        "      + '|sup=' + (CSS.supports(cases[i][0], cases[i][1]) ? 1 : 0)\n"
        "      + '|echo=' + el.style.getPropertyValue(cases[i][0])\n"
        "      + '|comp=' + getComputedStyle(el).getPropertyValue(cases[i][0]));")

PAGE = """<!doctype html>
<html><head><meta charset="utf-8"><title>b298 math comment probe</title></head>
<body>
<div id="host"></div>
<script>
// A comment is a token boundary (CSS Syntax 3 §4) and never an operator's whitespace
// (CSS Values 4 §10.3). The rows keep the two apart: where a '+' or '-' stands on a comment alone
// the declaration is no value at all, and where the comment only separates two terms, sits beside
// real space, or hangs off the end of a value, everything reads as it always did.
function build() {
const host = document.createElement('div');
document.body.appendChild(host);
const rows = [];
const cases = %s;
for (let i = 0; i < cases.length; i++) {
// One fresh element per row: a refused declaration has to read back as nothing, not as whatever the
// previous row left in the same block.
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
}""" % (LITERAL, READ.replace("  el.style", "  el.style"))


def main():
    with open(os.path.join(ROOT, "snapshots", "_probe_b298_mathcomment.html"), "w", encoding="utf-8") as f:
        f.write(PAGE)
    with open(os.path.join(ROOT, "snapshots", "_b298_mathcomment_ref_script.js"), "w", encoding="utf-8") as f:
        f.write(REFERENCE)
    print(f"{len(CASES)} rows -> snapshots/_probe_b298_mathcomment.html + _b298_mathcomment_ref_script.js")


if __name__ == "__main__":
    main()
