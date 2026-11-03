"""Generate the b309 follow-up probe: the word sets the first sweep found open.

The rows are candidates a fix would add, measured before any of them is written into the table —
the rule that keeps a grammar arm from being a guess.
"""
import json
import os

CASES = [
    # CSS Anchor Positioning 1 §5.2: the ordering set, physical and logical.
    ("position-try-order", "normal"), ("position-try-order", "most-width"),
    ("position-try-order", "most-height"), ("position-try-order", "most-inline-size"),
    ("position-try-order", "most-block-size"), ("position-try-order", "least-width"),
    ("position-try-order", "least-height"), ("position-try-order", "least-inline-size"),
    ("position-try-order", "least-block-size"), ("position-try-order", "MOST-WIDTH"),
    ("position-try-order", "most-inline-size most-block-size"),
    ("position-try-order", "most-width most-height"),
    # SVG 2 §11.3.6: the baseline set, the two the sweep found missing plus the rest.
    ("dominant-baseline", "TEXT-BEFORE-EDGE"), ("dominant-baseline", "text-before-edge"),
    ("dominant-baseline", "text-after-edge"), ("dominant-baseline", "reset-size"),
    ("dominant-baseline", "no-change"), ("dominant-baseline", "use-script"),
    ("dominant-baseline", "auto"), ("dominant-baseline", "middle"),
    ("dominant-baseline", "central"), ("dominant-baseline", "baseline"),
    ("dominant-baseline", "before-edge"), ("dominant-baseline", "after-edge"),
    # Chrome's own seat: what a region of the window is for.
    ("-webkit-app-region", "no-drag"), ("-webkit-app-region", "drag"),
    ("-webkit-app-region", "NO-DRAG"), ("-webkit-app-region", "none"),
    ("-webkit-app-region", "auto"), ("-webkit-app-region", "no-drag drag"),
    ("-webkit-app-region", "foo"),
    # CSS Fonts 4 §8.6: the metric a ratio is measured from.
    ("font-size-adjust", "ex-height 0.5"), ("font-size-adjust", "cap-height 0.8"),
    ("font-size-adjust", "ch-width 0.5"), ("font-size-adjust", "ic-height 0.5"),
    ("font-size-adjust", "bu-scale 0.5"), ("font-size-adjust", "ex-height 0"),
    ("font-size-adjust", "cap-height 0"), ("font-size-adjust", "ex-height -1"),
    ("font-size-adjust", "EX-HEIGHT 0.5"), ("font-size-adjust", "cap-height calc(0.5)"),
    ("font-size-adjust", "cap-height 50%"), ("font-size-adjust", "0.5 cap-height"),
    ("font-size-adjust", "cap-height from-font"),
    ("font-size-adjust", "ex-height calc(1px/0)"),
]


def page():
    literal = "[" + ",".join(f'[{json.dumps(p)},{json.dumps(v)}]' for p, v in CASES) + "]"
    read = """  if (cases[i][1] !== '~') el.style.setProperty(cases[i][0], cases[i][1]);
  rows.push(i + '|' + cases[i][0] + '|' + cases[i][1]
      + '|sup=' + (cases[i][1] === '~' ? '-' : (CSS.supports(cases[i][0], cases[i][1]) ? 1 : 0))
      + '|echo=' + el.style.getPropertyValue(cases[i][0])
      + '|comp=' + getComputedStyle(el).getPropertyValue(cases[i][0]));"""
    inheritance = """
// The same three questions on a CHILD of the box the page wrote on: a property that inherits
// answers the parent's value, and one that does not answers its initial.
const childRows = [];
for (const [name, value] of [['-webkit-app-region', 'drag'],
                             ['dominant-baseline', 'text-before-edge'],
                             ['position-try-order', 'most-inline-size'],
                             ['font-size-adjust', 'cap-height 0.8']]) {
  const parent = document.createElement('div');
  parent.style.setProperty(name, value);
  const child = document.createElement('div');
  parent.appendChild(child);
  host.appendChild(parent);
  childRows.push('child|' + name + '|echo=' + parent.style.getPropertyValue(name)
      + '|childComp=' + getComputedStyle(child).getPropertyValue(name));
}
rows = rows.concat(childRows);
"""
    return """<!doctype html>
<html><head><meta charset="utf-8"><title>b309 sets sweep</title></head>
<body>
<div id="host"></div>
<script>
// sets
function build() {
const host = document.createElement('div');
document.body.appendChild(host);
let rows = [];
const cases = %s;
for (let i = 0; i < cases.length; i++) {
const el = document.createElement('div');
host.appendChild(el);
%s
}
%s
document.body.insertAdjacentHTML('beforeend',
    '<pre id="record" style="font:10px/1.35 monospace;white-space:pre-wrap">'
    + rows.join('\\n') + '</pre>');
console.log(rows.join('\\n'));
host.remove();
}
build();
</script>
</body></html>
""" % (literal, read, inheritance)


HERE = os.path.dirname(os.path.abspath(__file__))
with open(os.path.join(HERE, "_probe_b309_sets.html"), "w", encoding="utf-8") as handle:
    handle.write(page())
print("cases:", len(CASES))
