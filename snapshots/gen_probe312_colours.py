"""Generate the b312 colour and anchor-size probes.

The colour table asks one question per row: what notation does the computed surface print a colour
in (CSS Color 4 §16 and CSS Color 5 §3.4's mixing table say it is the authored function, not sRGB),
and which relative-color forms the grammar refuses (a colorspace name is required inside color(),
and a hue method only travels with a cylindrical space). The anchor-size table asks the same of
anchor-size() inside a math expression, where the reference engine also re-orders the terms.
"""
import json
import os

COLOURS = [
    ("color", "lab(50% 0 0)"), ("color", "lab(50 80 70)"), ("color", "lch(50% 0 0)"),
    ("color", "oklab(0.5 0 0)"), ("color", "oklch(50% 0.2 30deg)"),
    ("color", "color(display-p3 1 0 0)"), ("color", "color(srgb 1 0 0 / none)"),
    ("color", "color(rec2020 0.1 0.2 0.3)"), ("color", "color(xyz 0.1 0.2 0.3)"),
    ("color", "color(xyz-d50 0.1 0.2 0.3)"), ("color", "hsl(120 50% 50%)"),
    ("color", "hwb(120 20% 30%)"), ("color", "lab(50% none none)"),
    ("color", "lch(50% 0 none)"), ("color", "color(srgb 1 0 0 / 50%)"),
    ("color", "rgb(none 2 3)"), ("color", "hsl(none 50% 50%)"),
    ("color", "color-mix(in srgb, red, blue)"), ("color", "color-mix(in oklab, red, blue)"),
    ("color", "color-mix(in hsl, red, blue)"), ("color", "color-mix(in lch, purple 50%, plum)"),
    ("color", "color-mix(in oklch shorter hue, red, blue)"),
    ("color", "color-mix(in srgb, red 50%, blue 50%)"),
    ("color", "color-mix(in oklab, red 30%, blue)"),
    ("color", "color-mix(red, blue)"), ("color", "color-mix(in srgb, currentcolor, blue)"),
    ("color", "lab(from red 50% a b)"), ("color", "lch(from red l c h / 50%)"),
    ("color", "oklab(from hsl(120 50% 50%) l a b)"),
    ("color", "color(from color(display-p3 1 1 0) srgb r g b)"),
    ("color", "color(from red srgb r g b)"), ("color", "color(from red srgb r g calc(b + 0.5))"),
    ("color", "hsl(from red h s l)"), ("color", "rgb(from red g b r)"),
    ("color", "alpha(from red / 50%)"), ("color", "color(from red oklab l a b)"),
    ("color", "lab(from lab(50 1 1) none a b)"), ("color", "lch(from lch(50 1 1deg) l c h)"),
    ("color", "color-mix(in lab, red, blue)"), ("color", "color-mix(in xyz, red, blue)"),
]

ANCHOR = [
    ("width", "anchor-size(width)"), ("width", "anchor-size(self-inline)"),
    ("width", "anchor-size(self-block)"), ("width", "anchor-size(--a width)"),
    ("width", "anchor-size(--a self-end)"), ("width", "anchor-size(width, 10px)"),
    ("width", "calc(anchor-size(width) + 10px)"), ("width", "calc(10px + anchor-size(width))"),
    ("width", "calc(anchor-size(width) * 2)"), ("width", "calc(10px + 1em)"),
    ("width", "calc(1em + 10px)"), ("width", "calc(1px + 2% + 3em)"),
    ("width", "calc(anchor-size(width) + anchor-size(height))"),
    ("width", "calc(anchor-size(self-inline) + 10px)"),
    ("width", "calc(10px + anchor-size(--a width))"),
    ("width", "calc(anchor-size(width))"), ("width", "anchor-size()"),
    ("width", "anchor-size(self-inline, 10px)"), ("height", "anchor-size(height)"),
    ("width", "min(anchor-size(width), 100px)"),
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
with open(os.path.join(HERE, "_probe_b312_colours.html"), "w", encoding="utf-8") as handle:
    handle.write(page("b312 colours", COLOURS))
with open(os.path.join(HERE, "_probe_b312_anchor.html"), "w", encoding="utf-8") as handle:
    handle.write(page("b312 anchor", ANCHOR))
print("colours:", len(COLOURS), "anchor:", len(ANCHOR))
