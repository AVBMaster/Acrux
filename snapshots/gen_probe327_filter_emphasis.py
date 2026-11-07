"""Generate the b327 sweep: the filter functions value by value, and the
text-emphasis / mask forms the earlier sweeps only touched once or twice.

Each filter function gets its range edges — the exact zero, one, a negative
(that drops the whole declaration, measured), a percentage, an omitted argument
and a number out of range — because the clamp and the refusal are per function.
The text-emphasis and mask longhands get the forms their shorthand does not
spell out. One fresh element per row (measured in b283: a reused host answers a
dropped declaration with the previous row's value).
"""
import json
import os

CASES = [
    # Each fraction function at its edges: the clamp at one, the refusal below zero.
    ("filter", "blur(0px)"), ("filter", "blur(1px)"), ("filter", "blur(-1px)"),
    ("filter", "brightness(0)"), ("filter", "brightness(1)"), ("filter", "brightness(-1)"),
    ("filter", "contrast(0)"), ("filter", "contrast(2)"), ("filter", "contrast(-1)"),
    ("filter", "grayscale(0)"), ("filter", "grayscale(1)"), ("filter", "grayscale(2)"),
    ("filter", "grayscale(-1)"), ("filter", "invert(0)"), ("filter", "invert(1)"),
    ("filter", "invert(2)"), ("filter", "opacity(0)"), ("filter", "opacity(1)"),
    ("filter", "opacity(2)"), ("filter", "sepia(0)"), ("filter", "sepia(1)"),
    ("filter", "sepia(2)"), ("filter", "saturate(0)"), ("filter", "saturate(2)"),
    ("filter", "saturate(-1)"),
    # Percentages of each, and an omitted argument.
    ("filter", "opacity(50%)"), ("filter", "opacity(-10%)"), ("filter", "brightness(50%)"),
    ("filter", "contrast(200%)"), ("filter", "blur(10%)"),
    ("filter", "hue-rotate(0deg)"), ("filter", "hue-rotate(-90deg)"),
    ("filter", "hue-rotate(90deg)"), ("filter", "hue-rotate(1turn)"),
    ("filter", "drop-shadow(1px 2px)"), ("filter", "drop-shadow(1px 2px 3px)"),
    ("filter", "drop-shadow(1px 2px red)"), ("filter", "drop-shadow(red 1px 2px 3px)"),
    ("filter", "drop-shadow(1px 2px 3px 4px)"),
    ("filter", "brightness(1) blur(2px)"), ("filter", "blur(2px) brightness(1)"),
    ("filter", "BOGUS(1)"), ("filter", "blur(2px) bogus(1)"),
    ("backdrop-filter", "opacity(2)"), ("backdrop-filter", "opacity(50%)"),
    ("backdrop-filter", "blur(-1px)"), ("backdrop-filter", "brightness(-1)"),
    # text-emphasis, only three rows in earlier sweeps.
    ("text-emphasis-style", "none"), ("text-emphasis-style", "filled"),
    ("text-emphasis-style", "open"), ("text-emphasis-style", "dot"),
    ("text-emphasis-style", "circle"), ("text-emphasis-style", "double-circle"),
    ("text-emphasis-style", "triangle"), ("text-emphasis-style", "sesame"),
    ("text-emphasis-style", "filled dot"), ("text-emphasis-style", "open circle"),
    ("text-emphasis-style", "dot dot"), ("text-emphasis-style", "bogus"),
    ("text-emphasis-position", "over"), ("text-emphasis-position", "under"),
    ("text-emphasis-position", "right"), ("text-emphasis-position", "left"),
    ("text-emphasis-position", "over right"), ("text-emphasis-position", "under left"),
    ("text-emphasis-position", "right over"), ("text-emphasis-position", "bogus"),
    ("text-emphasis-color", "red"), ("text-emphasis-color", "currentcolor"),
    ("text-emphasis-color", "red blue"), ("text-emphasis-color", "bogus"),
    ("text-emphasis", "none"), ("text-emphasis", "dot"), ("text-emphasis", "filled sesame"),
    ("text-emphasis", "circle red"), ("text-emphasis", "red dot"),
    ("text-emphasis", "open triangle blue"), ("text-emphasis", "bogus"),
    ("text-emphasis", "none red"), ("text-emphasis", "dot dot red"),
    # mask longhands the shorthand does not spell out.
    ("mask-image", "none"), ("mask-image", "linear-gradient(red, blue)"),
    ("mask-image", "url(a.png)"), ("mask-image", "red"), ("mask-image", "bogus"),
    ("mask-image", "linear-gradient(red, blue), url(a.png)"),
    ("mask", "none"), ("mask", "linear-gradient(red, blue)"),
    ("mask", "url(a.png) 10px 20px / 50% no-repeat"), ("mask", "bogus"),
    ("mask", "none, none"), ("mask", "url(a.png) center / cover"),
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
<html><head><meta charset="utf-8"><title>b327 sweep</title></head>
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
// textContent, not HTML: a value carrying '<' is read as a start tag and vanishes.
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
with open(os.path.join(HERE, "_probe_b327_filter_emphasis.html"), "w", encoding="utf-8") as handle:
    handle.write(page())
print("cases:", len(CASES))
