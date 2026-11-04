"""Generate the b311 sweep probe: the property families no round has swept yet.

One fresh element per row (the rule b291 measured), three columns: what the grammar answers, what
the style attribute echoes and what the computed surface answers.
"""
import json
import os

CASES = [
    # CSS Touch Action 1 §3: the pan and zoom set, alone and in a list.
    ("touch-action", "auto"), ("touch-action", "none"), ("touch-action", "pan-x"),
    ("touch-action", "pan-y"), ("touch-action", "pan-left"), ("touch-action", "pan-right"),
    ("touch-action", "pan-up"), ("touch-action", "pan-down"),
    ("touch-action", "pinch-zoom"), ("touch-action", "manipulation"),
    ("touch-action", "pan-x pan-y"), ("touch-action", "pan-x pinch-zoom"),
    ("touch-action", "pinch-zoom pan-x"), ("touch-action", "pan-x pan-x"),
    ("touch-action", "auto pan-x"), ("touch-action", "none pan-x"),
    ("touch-action", "manipulation pan-x"), ("touch-action", "PAN-X"),
    ("touch-action", "pan"), ("touch-action", "zoom"), ("touch-action", "drag"),
    ("touch-action", "double-tap-zoom"),
    # CSS Punctuation 1 §3: the hanging set, and the pair the draft adds.
    ("hanging-punctuation", "none"), ("hanging-punctuation", "first"),
    ("hanging-punctuation", "start"), ("hanging-punctuation", "end"),
    ("hanging-punctuation", "last"), ("hanging-punctuation", "allow-end"),
    ("hanging-punctuation", "force-end"), ("hanging-punctuation", "first last"),
    ("hanging-punctuation", "first allow-end"), ("hanging-punctuation", "allow-end force-end"),
    ("hanging-punctuation", "first last allow-end force-end"),
    ("hanging-punctuation", "FIRST"), ("hanging-punctuation", "auto"),
    # CSS Images 3 §5.5 and §5.6: the orientation and the resolution of a replaced image.
    ("image-orientation", "none"), ("image-orientation", "from-image"),
    ("image-orientation", "0deg"), ("image-orientation", "90deg"),
    ("image-orientation", "180deg"), ("image-orientation", "270deg"),
    ("image-orientation", "-90deg"), ("image-orientation", "45deg"),
    ("image-orientation", "0turn"), ("image-orientation", "100grad"),
    ("image-orientation", "flip"), ("image-orientation", "from-image 90deg"),
    ("image-resolution", "1dppx"), ("image-resolution", "from-image"),
    ("image-resolution", "2x"), ("image-resolution", "from-image snap"),
    ("image-resolution", "snap"), ("object-view-box", "none"),
    ("object-view-box", "view-box"), ("object-view-box", "xywh(0 0 100% 100%)"),
    ("object-view-box", "xywh(10px 20px 30px 40px)"), ("object-view-box", "inset(10px)"),
    ("object-view-box", "auto"),
    # CSS Masking 1 §5.6: the standard names of the border-image shape, on the mask.
    ("mask-border-source", "none"), ("mask-border-source", "url(a.png)"),
    ("mask-border-mode", "alpha"), ("mask-border-mode", "luminance"),
    ("mask-border-slice", "10"), ("mask-border-slice", "10 fill"),
    ("mask-border-slice", "10% 20%"), ("mask-border-width", "auto"),
    ("mask-border-width", "10px"), ("mask-border-width", "10px 20px 30px 40px"),
    ("mask-border-outset", "0"), ("mask-border-outset", "10px"),
    ("mask-border-repeat", "stretch"), ("mask-border-repeat", "round space"),
    ("mask-border", "url(a.png) 10"), ("mask-border", "none"),
    # The paint-server properties a filter or a gradient carries.
    ("stop-color", "red"), ("stop-color", "currentColor"), ("stop-color", "transparent"),
    ("stop-opacity", "0.5"), ("stop-opacity", "50%"), ("stop-opacity", "normal"),
    ("flood-color", "red"), ("flood-opacity", "0.5"),
    ("lighting-color", "red"), ("color-interpolation-filters", "linearRGB"),
    ("color-interpolation-filters", "auto"),
    # CSS Transforms 3 §3: the origin of the transform, in keywords, lengths and percentages.
    ("transform-origin", "center"), ("transform-origin", "left top"),
    ("transform-origin", "50% 50%"), ("transform-origin", "10px 20px 30px"),
    ("transform-origin", "left 10px"), ("transform-origin", "10px center"),
    ("transform-origin", "right bottom"), ("transform-origin", "top right"),
    ("transform-origin", "CENTER"), ("transform-origin", "1in 2in"),
    ("perspective-origin", "center"), ("perspective-origin", "left top"),
    ("perspective-origin", "10px 20px"), ("perspective-origin", "50%"),
    ("perspective", "none"), ("perspective", "0"), ("perspective", "0px"),
    ("perspective", "100px"), ("perspective", "-100px"), ("perspective", "50%"),
    ("perspective", "12"),
    # CSS Grid 3: the tracks' own alignment and the stacking auto-flow.
    ("align-tracks", "center"), ("align-tracks", "safe center"),
    ("align-tracks", "center center"), ("justify-tracks", "space-between"),
    ("justify-tracks", "unsafe end"), ("grid-auto-flow", "stack"),
    ("grid-auto-flow", "row stack"), ("grid-auto-flow", "column dense stack"),
    ("grid-template-areas", "none"), ("grid-template-areas", "\"a b\" \"c\""),
    ("grid-template-areas", "\"a\" \"a\""),
    # CSS Word Wrap 3 §2: the string an overflow break may insert.
    ("overflow-wrap", "normal"), ("overflow-wrap", "break-word"),
    ("overflow-wrap", "anywhere"), ("overflow-wrap", '"-"'),
    ("overflow-wrap", "'…' break-all"), ("overflow-wrap", "anywhere \"-\""),
    ("overflow-wrap", '"-" "–"'), ("overflow-wrap", "break-word \"-\""),
    # The logical sides of the older pair, and the column trio.
    ("clear", "inline-start"), ("clear", "inline-end"), ("clear", "left"),
    ("float", "inline-start"), ("float", "inline-end"), ("float", "left"),
    ("column-fill", "balance"), ("column-fill", "auto"), ("column-fill", "balance-all"),
    ("column-fill", "both"), ("column-span", "none"), ("column-span", "all"),
    ("column-span", "balance"),
    # Scroll anchoring and the newer anchor-position names.
    ("anchor-scroll", "auto"), ("anchor-scroll", "--a"), ("anchor-scroll", "default"),
    ("position-area", "top"), ("position-area", "span-block"),
    ("position-area", "center"), ("position-area", "block-start inline-end"),
    ("scroll-gutter", "auto"), ("scroll-gutter", "stable"),
    ("scroll-marker-group", "none"), ("scroll-marker-group", "before"),
    ("scroll-marker-group", "after first"),
]


def page():
    literal = "[" + ",".join(f'[{json.dumps(p)},{json.dumps(v)}]' for p, v in CASES) + "]"
    read = """  if (cases[i][1] !== '~') el.style.setProperty(cases[i][0], cases[i][1]);
  rows.push(i + '|' + cases[i][0] + '|' + cases[i][1]
      + '|sup=' + (cases[i][1] === '~' ? '-' : (CSS.supports(cases[i][0], cases[i][1]) ? 1 : 0))
      + '|echo=' + el.style.getPropertyValue(cases[i][0])
      + '|comp=' + getComputedStyle(el).getPropertyValue(cases[i][0]));"""
    return """<!doctype html>
<html><head><meta charset="utf-8"><title>b311 sweep</title></head>
<body>
<div id="host"></div>
<script>
// sweep
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
""" % (literal, read)


HERE = os.path.dirname(os.path.abspath(__file__))
with open(os.path.join(HERE, "_probe_b311_sweep.html"), "w", encoding="utf-8") as handle:
    handle.write(page())
print("cases:", len(CASES))
