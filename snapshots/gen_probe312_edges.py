"""Generate the b312 follow-up probes: what <color()> and anchor-size() actually take.

The colour table separates the two questions the first table left open — whether a colorspace name
is required inside color(from …), and which channel keywords that function accepts — and the anchor
table asks how a math expression that holds an anchor-size() term is echoed, since the reference
engine re-orders the terms there.
"""
import json
import os

COLOURS2 = [
    ("color", "color(from red srgb 1 0 0)"), ("color", "color(from red oklab 0.5 0 0)"),
    ("color", "color(from red display-p3 r g b)"), ("color", "color(from red lch l c h)"),
    ("color", "color(from red srgb r g b / 50%)"), ("color", "color(from red srgb 1 g b)"),
    ("color", "color(from red srgb r g)"), ("color", "color(from red srgb)"),
    ("color", "color(red srgb r g b)"), ("color", "color(srgb from red r g b)"),
    ("color", "color-mix(in srgb longer hue, red, blue)"),
    ("color", "color-mix(in oklab longer hue, red, blue)"),
    ("color", "color-mix(in hsl longer hue, red, blue)"),
    ("color", "color-mix(in oklch increasing hue, red 50%, blue)"),
    ("color", "color-mix(in lab longer hue, red, blue)"),
    ("color", "color-mix(in xyz shorter hue, red, blue)"),
    ("color", "lab(50% 80.8198 69.8997)"), ("color", "lab(50% +80 -70)"),
    ("color", "lch(50% 0 0.4turn)"), ("color", "oklch(0.5 0.2 30)"),
    ("color", "color(srgb-linear 1 0 0)"), ("color", "color(a98-rgb 1 0 0)"),
    ("color", "color(prophoto-rgb 1 0 0)"), ("color", "color(xyz-d65 0.1 0.2 0.3)"),
    ("color", "color(--my-space 1 2 3)"), ("color", "lab(50 80 70 / none)"),
    ("color", "lab(50 80 70 / 50%)"), ("color", "lab(none 80 70)"),
    ("background-color", "lab(50 80 70)"), ("border-top-color", "oklch(50% 0.2 30deg)"),
    ("color", "lab(from #ff0000 50% a b)"), ("color", "lab(from currentcolor 50% a b)"),
    ("color", "rgb(from red none b r)"), ("color", "hsl(from red calc(h + 90) s l)"),
    ("color", "color-mix(in srgb, red 30%, blue 30%)"),
    ("color", "color-mix(in oklch, red, blue 25%)"),
]

ANCHOR2 = [
    ("width", "calc(anchor-size(width) + 1em)"), ("width", "calc(1em + anchor-size(width))"),
    ("width", "calc(anchor-size(width) + 10px + 1em)"),
    ("width", "calc(anchor-size(width) - 10px)"), ("width", "calc(10px - anchor-size(width))"),
    ("width", "calc(anchor-size(width) + anchor-size(width))"),
    ("width", "calc(2 * anchor-size(width))"),
    ("width", "calc(anchor-size(width))"), ("width", "anchor-size(width, 10px)"),
    ("width", "anchor-size(self-inline, 10px)"), ("width", "anchor-size(width width)"),
    ("width", "anchor-size(width 10px)"), ("width", "anchor-size(--a, 10px)"),
    ("width", "anchor-size(--a width, 10px)"), ("width", "anchor-size(self-end)"),
    ("width", "anchor-size(inline)"), ("width", "anchor-size(block)"),
    ("width", "anchor-size(top)"), ("width", "anchor-size(left)"),
    ("margin-left", "anchor-size(width)"), ("padding", "anchor-size(width)"),
    ("height", "calc(anchor-size(height) + 10px)"),
    ("width", "min(anchor-size(width), 100px)"), ("width", "max(10px, anchor-size(width))"),
    ("width", "clamp(10px, anchor-size(width), 100px)"),
    ("width", "calc(anchor-size(width) + 10px + anchor-size(height))"),
]


def page(title, cases):
    literal = "[" + ",".join(f'[{json.dumps(p)},{json.dumps(v)}]' for p, v in cases) + "]"
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
with open(os.path.join(HERE, "_probe_b312_colours2.html"), "w", encoding="utf-8") as handle:
    handle.write(page("b312 colours 2", COLOURS2))
with open(os.path.join(HERE, "_probe_b312_anchor2.html"), "w", encoding="utf-8") as handle:
    handle.write(page("b312 anchor 2", ANCHOR2))
print("colours2:", len(COLOURS2), "anchor2:", len(ANCHOR2))
