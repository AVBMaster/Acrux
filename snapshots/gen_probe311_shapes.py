"""Generate the b311 shape probes: the arguments a basic shape takes, and the box each property names.

The first table asks whether a bare number is a length anywhere inside a shape (CSS Values 4 §10.3
makes the unitless spelling the zero's own); the other two walk the geometry boxes, which are not the
same set on 'clip-path', 'shape-outside' and 'offset-path'.
"""
import json
import os

SHAPE = [
    ("offset-path", "xywh(1 2 3 4)"), ("offset-path", "circle(10)"),
    ("offset-path", "circle(0)"), ("offset-path", "ellipse(10 20)"),
    ("offset-path", "inset(1)"), ("offset-path", "inset(0 round 1)"),
    ("offset-path", "inset(10px round 5)"), ("offset-path", "xywh(0 0 10px 10px)"),
    ("clip-path", "inset(1)"), ("clip-path", "circle(50%)"), ("clip-path", "circle(10)"),
    ("clip-path", "polygon(50% 0, 0 100%, 100% 100%)"),
    ("object-view-box", "inset(1)"), ("object-view-box", "inset(0)"),
    ("object-view-box", "xywh(0 0 10px 10px)"), ("object-view-box", "xywh(1 2 3 4)"),
    ("offset-distance", "50%"), ("offset-distance", "10px"),
]

BOXES = [
    ("clip-path", "margin-box"), ("clip-path", "border-box"), ("clip-path", "padding-box"),
    ("clip-path", "content-box"), ("clip-path", "fill-box"), ("clip-path", "stroke-box"),
    ("clip-path", "view-box"), ("clip-path", "border-box circle(50%)"),
    ("clip-path", "circle(50%) border-box"), ("clip-path", "margin-box inset(10px)"),
    ("clip-path", "inset(10px margin-box)"), ("clip-path", "border-box padding-box"),
    ("clip-path", "circle(50%) at 25% 25% border-box"),
    ("shape-outside", "margin-box"), ("shape-outside", "border-box"),
    ("shape-outside", "fill-box"), ("shape-outside", "circle(50%) border-box"),
    ("shape-outside", "border-box circle(50%)"),
    ("offset-path", "margin-box"), ("offset-path", "content-box"),
    ("offset-path", "padding-box circle(50%)"),
    ("object-view-box", "margin-box"), ("object-view-box", "fill-box"),
    ("mask-border", "url(a.png) 10 fill stretch"),
]

BOXES2 = [
    ("shape-outside", "circle(50%) fill-box"), ("shape-outside", "fill-box circle(50%)"),
    ("shape-outside", "circle(50%) margin-box"), ("shape-outside", "margin-box circle(50%)"),
    ("shape-outside", "content-box"), ("shape-outside", "padding-box"),
    ("shape-outside", "circle(50%) padding-box"), ("shape-outside", "view-box"),
    ("clip-path", "circle(50%) margin-box"), ("clip-path", "circle(50%) fill-box"),
    ("clip-path", "margin-box circle(50%)"), ("clip-path", "url(a.png) border-box"),
    ("clip-path", "border-box url(a.png)"),
    ("clip-path", "polygon(0 0, 10px 10px) content-box"),
    ("mask", "circle(50%) border-box"), ("shape-outside", "none"), ("clip-path", "MARGIN-BOX"),
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
for name, cases in [("shape", SHAPE), ("boxes", BOXES), ("boxes2", BOXES2)]:
    with open(os.path.join(HERE, f"_probe_b311_{name}.html"), "w", encoding="utf-8") as handle:
        handle.write(page(f"b311 {name}", cases))
print("shape:", len(SHAPE), "boxes:", len(BOXES), "boxes2:", len(BOXES2))
