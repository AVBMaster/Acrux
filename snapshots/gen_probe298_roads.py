#!/usr/bin/env python3
"""Write the b298 roads probe page and the script that measures its reference dump.

#306 is one rule about a comment's whitespace, but a declaration can reach the engine by three roads:
written through the CSSOM, authored in a 'style' attribute, or carried in a stylesheet. Each road has
its own parser in this engine, and only the first of them ever saw the comment intact — the other two
run through a tokenizer that drops a comment without leaving its separation behind, so
'margin: 1px/*c*/2px' arrives as one token, '1px2px'. The rows therefore ask the same question of all
three roads and print the echo and the computed value of each.
"""
import os

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

CASES = [
    ("width", "calc(1px/*c*/+ 2px)"),
    ("width", "calc(1px+/*c*/2px)"),
    ("width", "calc(1px+2px)"),
    ("width", "calc(1px /*c*/ + 2px)"),
    ("width", "calc(1px/*c*/ + 2px)"),
    ("height", "calc(1px -/*c*/ 2px)"),
    ("margin", "1px/*c*/2px"),
    ("margin", "1px/*c*/2px/*c*/3px 4px"),
    ("padding", "1px/*c*/2px"),
    ("clip", "rect(0px/*c*/0px 0px 0px)"),
    ("background-position", "10px/*c*/20px"),
    ("width", "calc(1px/*c*/+ 2px) !important"),
    ("--x", "calc(1px/*c*/+ 2px)"),
    ("--y", "1px/*c*/2px"),
    ("width", "calc(var(--x)/*c*/+ 1px)"),
    ("grid-template-columns", "1px/*c*/2px"),
]

LITERAL = "[[" + "],[".join(f'"{p}","{v}"' for p, v in CASES) + "]]"

# Road 1 — the CSSOM. Road 2 — the attribute the cascade mirrors. Road 3 — a stylesheet the element
# matches, read through its own rule's block and through the computed style.
READ = """  el.style.setProperty(cases[i][0], cases[i][1]);
  var a = document.createElement('div'); host.appendChild(a);
  a.setAttribute('style', cases[i][0] + ':' + cases[i][1]);
  var s = document.createElement('style');
  s.textContent = '#c' + i + '{' + cases[i][0] + ':' + cases[i][1] + '}';
  document.head.appendChild(s);
  var c = document.createElement('div'); c.id = 'c' + i; host.appendChild(c);
  var sheetText = '';
  for (var k = 0; k < document.styleSheets.length; k++) {
    var sh = document.styleSheets[k];
    if (!sh.ownerNode || sh.ownerNode !== s) continue;
    for (var m = 0; m < sh.cssRules.length; m++)
      sheetText = sh.cssRules[m].style.getPropertyValue(cases[i][0]);
  }
  rows.push(i + '|' + cases[i][0] + '|' + cases[i][1]
      + '|cssom=' + el.style.getPropertyValue(cases[i][0])
      + '|attr=' + a.style.getPropertyValue(cases[i][0])
      + '|rule=' + sheetText
      + '|compA=' + getComputedStyle(a).getPropertyValue(cases[i][0])
      + '|compC=' + getComputedStyle(c).getPropertyValue(cases[i][0]));"""

PAGE = """<!doctype html>
<html><head><meta charset="utf-8"><title>b298 roads probe</title></head>
<body>
<div id="host"></div>
<script>
// A comment separates the tokens it stands between (CSS Syntax 3 §4) but is never the whitespace a
// math operator demands (CSS Values 4 §10.3). Three roads carry one declaration into this engine, and
// all three have to give the same answer.
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
    with open(os.path.join(ROOT, "snapshots", "_probe_b298_roads.html"), "w", encoding="utf-8") as f:
        f.write(PAGE)
    with open(os.path.join(ROOT, "snapshots", "_b298_roads_ref_script.js"), "w", encoding="utf-8") as f:
        f.write(REFERENCE)
    print(f"{len(CASES)} rows -> snapshots/_probe_b298_roads.html + _b298_roads_ref_script.js")


if __name__ == "__main__":
    main()
