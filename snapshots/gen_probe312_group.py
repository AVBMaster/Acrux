"""b312: what a parenthesised group inside a math expression is printed as.

CSS Values 4 §10.13 sorts the operands of a sum and parenthesises a product that stands inside it,
but it says nothing about a *sum* written inside a sum — so the question this page asks the reference
engine is whether '(1em + 2px)' keeps its parentheses when it sits behind a '+'. Every case here is
one where the two answers differ, and the 'sup=' column says whether the group is a value at all.
"""
import os

CASES = [
    ("width", "calc(10px + (1em + 2px))"),
    ("width", "calc((1em + 2px) + 10px)"),
    ("width", "calc(10px + (1vw + 1em + 2px))"),
    ("width", "calc(1vw + (1em + 2px))"),
    ("width", "calc(10px - (1vw + 1em + 2px))"),
    ("width", "calc(10px + (1em * 2))"),
    ("width", "calc((1vw + 1em) * 2)"),
    ("width", "calc(10px + ((1vw + 1em) * 2))"),
    ("width", "calc(10px * (1 + 2))"),
    ("width", "calc((1px))"),
    ("width", "calc(10px + (1em))"),
    ("width", "calc(10px + (max(1em, 2px)))"),
    ("width", "calc(10px + (var(--x) + 1px))"),
    ("width", "calc(10px + (-1em))"),
    ("width", "calc(10px + calc(1em + 2px))"),
    ("height", "calc(10px + (1em + 2px))"),
    ("margin-left", "calc(10px + (1em + 2px))"),
    ("line-height", "calc(1.5 + (1em + 2px))"),
    ("width", "calc(10px +(1em + 2px))"),
    ("width", "calc((1em) (2px))"),
]

cases_js = "[" + ",".join('["%s","%s"]' % (p, v) for p, v in CASES) + "]"

html = """<!doctype html>
<html><head><meta charset="utf-8"><title>b312 group</title></head>
<body>
<div id="host"></div>
<div id="fromSheet"></div>
<style>#fromSheet {{ width: calc(10px + (1em + 2px)); }}</style>
<script>
// b312_group
function build() {{
const host = document.createElement('div');
document.body.appendChild(host);
const rows = [];
const cases = {cases};
for (let i = 0; i < cases.length; i++) {{
const el = document.createElement('div');
host.appendChild(el);
  el.style.setProperty(cases[i][0], cases[i][1]);
  rows.push(i + '|' + cases[i][0] + '|' + cases[i][1]
      + '|sup=' + (CSS.supports(cases[i][0], cases[i][1]) ? 1 : 0)
      + '|echo=' + el.style.getPropertyValue(cases[i][0]));
    el.remove();
}}
document.body.insertAdjacentHTML('beforeend',
    '<pre id="record" style="font:10px/1.35 monospace;white-space:pre-wrap">'
    + rows.join('\\n') + '</pre>');
console.log(rows.join('\\n'));
host.remove();
}}
build();
</script>
</body></html>
"""

out = os.path.join(os.path.dirname(os.path.abspath(__file__)), "_probe_b312_group.html")
with open(out, "w") as f:
    f.write(html.format(cases=cases_js))
print(out, len(CASES), "rows")
