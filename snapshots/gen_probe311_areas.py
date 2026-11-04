"""Generate the b311 area probes: the pairs and the single words of <position-area>.

The first table reads a pair in both orders so the serialiser's slot order is measured, not assumed;
the second walks the word sets one at a time, including the words the draft does not have
('span-x', 'span-y', 'self-center') and the pairs that cross two productions.
"""
import json
import os

AREAS = [
    ("position-area", "top center"), ("position-area", "center top"),
    ("position-area", "left center"), ("position-area", "center left"),
    ("position-area", "top span-left"), ("position-area", "span-left top"),
    ("position-area", "span-left center"), ("position-area", "bottom span-right"),
    ("position-area", "self-start center"), ("position-area", "center self-start"),
    ("position-area", "top self-start"), ("position-area", "block-start center"),
    ("position-area", "center block-start"), ("position-area", "block-start top"),
    ("position-area", "left top"), ("position-area", "top left"),
    ("position-area", "right bottom"), ("position-area", "block-start inline-end"),
    ("position-area", "inline-start block-end"), ("position-area", "block-end inline-start"),
    ("position-area", "inline-end inline-start"), ("position-area", "span-start center"),
    ("position-area", "span-top span-left"),
]

AREA_WORDS = [
    "start", "end", "span-start", "span-end", "span-all", "span-x", "span-y", "x-start",
    "y-end", "span-x-start", "self-center", "span-self-start", "block-start self-start",
    "top self-center", "left span-start", "span-start span-end", "span-start top",
    "center center", "top top", "left right", "inline-start left", "span-left span-right",
    "bottom center", "center center center", "span-bottom", "span-right",
    "block-start span-left", "inline-end center", "start center", "top start",
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
with open(os.path.join(HERE, "_probe_b311_areas.html"), "w", encoding="utf-8") as handle:
    handle.write(page("b311 areas", AREAS))
with open(os.path.join(HERE, "_probe_b311_areawords.html"), "w", encoding="utf-8") as handle:
    handle.write(page("b311 area words", [("position-area", word) for word in AREA_WORDS]))
print("areas:", len(AREAS), "words:", len(AREA_WORDS))
