"""Generate the b314 sweep probe: the property families no round has swept yet.

One fresh element per row (the rule b291 measured), three columns: what the grammar answers, what
the style attribute echoes and what the computed surface answers. A value of '~' asks only the
computed surface, which is how a property with no grammar of its own is still measured.
"""
import json
import os

CASES = [
    # CSS Motion Path 1 §3.2-§4: the path a box is placed along.
    ("offset-path", "none"), ("offset-path", "path('M 0 0 L 10 10')"),
    ("offset-path", "path('M 0 0', fill-box)"), ("offset-path", "content-box"),
    ("offset-path", "url(#p)"), ("offset-path", "circle(50%)"),
    ("offset-path", "polygon(0 0, 10px 10px)"), ("offset-path", "ray(45deg)"),
    ("offset-path", "ray(45deg closest-side contain)"),
    ("offset-distance", "50%"), ("offset-distance", "calc(10px + 1em)"),
    ("offset-distance", "-10px"), ("offset-anchor", "auto"),
    ("offset-anchor", "center"), ("offset-anchor", "50% 25%"),
    ("offset-anchor", "top"), ("offset-position", "auto"),
    ("offset-position", "10px 20px"), ("offset-position", "50%"),
    ("offset-rotate", "auto 90deg"), ("offset-rotate", "reverse"),
    ("offset-rotate", "0deg"), ("offset", "auto"),
    ("offset", "path('M 0 0') 50%"), ("offset-path", "M 0 0"),
    # CSS Ruby 1 §4-§7: the annotation's own placement.
    ("ruby-position", "over"), ("ruby-position", "under"), ("ruby-position", "right"),
    ("ruby-position", "left"), ("ruby-position", "line-right"),
    ("ruby-position", "inter-character"), ("ruby-position", "over right"),
    ("ruby-position", "top"), ("ruby-position", "auto"),
    ("ruby-align", "start"), ("ruby-align", "center"), ("ruby-align", "distribute"),
    ("ruby-align", "space-between"), ("ruby-align", "space-around"),
    ("ruby-align", "left right"), ("ruby-align", "line stem"),
    ("ruby-overhang", "auto"), ("ruby-overhang", "start"), ("ruby-overhang", "none"),
    ("ruby-merge", "separate"), ("ruby-merge", "collapse"), ("ruby-merge", "auto"),
    # CSS Text 4 §2.6, §3.1 and Text 3 §4.6: upright composition and hanging edges.
    ("text-combine-upright", "none"), ("text-combine-upright", "all"),
    ("text-combine-upright", "digits"), ("text-combine-upright", "digits 4"),
    ("text-combine-upright", "digits 5"),
    ("hanging-punctuation", "none"), ("hanging-punctuation", "first"),
    ("hanging-punctuation", "start"), ("hanging-punctuation", "end"),
    ("hanging-punctuation", "force-end"), ("hanging-punctuation", "allow-end"),
    ("hanging-punctuation", "first last"), ("hanging-punctuation", "first force-end allow-end"),
    ("word-boundary-detection", "auto"), ("word-boundary-detection", "true"),
    ("word-boundary-language", "ja"), ("word-boundary-language", "auto"),
    ("text-wrap-flow", "none"), ("text-wrap-flow", "both"),
    ("text-wrap-through", "none"), ("text-wrap-through", "override"),
    # CSS Writing Modes 4 §5.1, §5.5: how text stands in a vertical line.
    ("text-orientation", "mixed"), ("text-orientation", "upright"),
    ("text-orientation", "sideways"), ("text-orientation", "rotate-right"),
    ("text-orientation", "vertical"), ("writing-mode", "sideways-lr"),
    ("writing-mode", "sideways-rl"), ("writing-mode", "vertical-rl"),
    ("writing-mode", "horizontal-tb"),
    # CSS Color 5 §8 and Color 4: the contrast pair.
    ("color-contrast", "auto"), ("color-contrast", "#fff | #000"),
    ("color-contrast", "#fff | #000 | #222 contrast()"),
    ("color-contrast", "white vs. black"), ("color-contrast", "#000"),
    ("contrast-color", "auto"), ("contrast-color", "#fff #000"),
    ("accent-color", "contrast-color"),
    # CSS Filter 1 §3 and Filter 2: the function forms.
    ("filter", "filter(from oklab L 0.5 C 0.1 h 200)"),
    ("filter", "filter(from srgb-linear r g b / a)"),
    ("filter", "filter(from lab l calc(a + 10) b)"),
    ("filter", "brightness(1.5) contrast(2)"), ("filter", "hue-rotate(0.5turn)"),
    ("filter", "opacity(50%)"), ("filter", "blur()"), ("filter", "drop-shadow(2px 2px)"),
    ("filter", "url(#f) blur(2px)"), ("filter", "none blur(1px)"),
    ("backdrop-filter", "blur(4px)"), ("backdrop-filter", "url(#b)"),
    ("backdrop-filter", "invert(1) grayscale(20%)"),
    # CSS Images 4 §9, Images 5 and Painting 1: the image functions.
    ("background-image", "cross-fade(red, blue)"),
    ("background-image", "cross-fade(red 25%, blue 75%)"),
    ("background-image", "cross-fade(from oklch, red, blue)"),
    ("background-image", "cross-fade(10% red, blue)"),
    ("background-image", 'image("a.png", red)'),
    ("background-image", 'image(from "a.png" to bottom, red, transparent)'),
    ("background-image", "element(#x)"), ("background-image", "paint(my-worklet)"),
    ("background-image", "paint(my-worklet, 10px)"),
    ("background-image", "url(a.png) red"),
    # CSS Masking 1 §3, §11 and SVG 2 §11.6: the border mask and the SVG order.
    ("mask-border-source", "url(a.png)"), ("mask-border-source", "none"),
    ("mask-border-slice", "10 fill"), ("mask-border-mode", "alpha"),
    ("mask-border-mode", "luminance"), ("mask-border", "url(a.png) 10"),
    ("mask-composite", "add"), ("mask-composite", "intersect exclude"),
    ("mask-type", "alpha"), ("mask-type", "luminance"),
    ("paint-order", "normal"), ("paint-order", "stroke"),
    ("paint-order", "fill stroke markers"), ("paint-order", "markers stroke fill"),
    ("vector-effect", "none"), ("vector-effect", "non-scaling-stroke"),
    ("shape-rendering", "crispEdges"), ("shape-rendering", "geometricPrecision"),
    ("text-rendering", "optimizeLegibility"), ("text-rendering", "geometricPrecision"),
    ("image-rendering", "pixelated"), ("image-rendering", "crisp-edges"),
    ("image-rendering", "high-resolution"), ("image-rendering", "from-image"),
    # CSS Scroll Snap 1 §5, Scroll-to-View and Overflow 5: the snap pair and the markers.
    ("scroll-snap-type", "x"), ("scroll-snap-type", "y"), ("scroll-snap-type", "block"),
    ("scroll-snap-type", "inline"), ("scroll-snap-type", "both"),
    ("scroll-snap-type", "x mandatory"), ("scroll-snap-type", "none strict"),
    ("scroll-snap-type", "snap x"), ("scroll-snap-stop", "normal"),
    ("scroll-snap-stop", "always"), ("scroll-behavior", "auto"),
    ("scroll-behavior", "smooth"), ("scroll-marker-group", "before"),
    ("scroll-marker-group", "after"), ("scroll-marker-group", "none"),
    # CSS UI 4 §3.6, §8 and Basic UI 3: the caret, the navigation and the field.
    ("caret-animation", "auto"), ("caret-animation", "smooth"),
    ("caret-animation", "step"), ("caret", "auto"),
    ("caret", "red 4px"), ("caret-shape", "bar"),
    ("user-select", "contain"), ("user-select", "all"), ("user-select", "auto"),
    ("input-security", "auto"), ("input-security", "none"),
    ("nav-up", "auto"), ("nav-index", "auto"), ("nav-down", "a"),
    # CSS TeX 1 §2: the maths depth and shift.
    ("math-depth", "auto-add"), ("math-depth", "0"), ("math-shift", "compact"),
    ("math-shift", "normal"), ("math-style", "normal"), ("math-style", "compact"),
    # CSS Will-Change 1 §3 and Containment 3 §4: the hints.
    ("will-change", "auto"), ("will-change", "none"), ("will-change", "opacity"),
    ("will-change", "transform, scroll-position"), ("will-change", "contents"),
    ("will-change", "left, top"), ("will-change", "all"),
    ("contain", "style"), ("contain", "content"), ("contain", "inline-size"),
    ("contain", "size layout style"),
    # CSS Paged Media 3 §3.9-§3.11 and Line Grid 1: the page and the grid.
    ("page", "auto"), ("page", "name"), ("marks", "none"), ("marks", "crop"),
    ("marks", "cross crop"), ("bleed", "10px"), ("bleed", "auto"),
    ("bleed", "10px 20px"), ("line-grid", "create"), ("line-grid", "none"),
    ("line-snap", "contain"), ("line-snap", "none"),
    ("box-decoration-break", "slice"), ("box-decoration-break", "clone"),
    ("print-color-adjust", "economy"), ("print-color-adjust", "exact"),
    ("color-adjust", "exact"),
    # CSS Transitions 1 §4 and Animations 2 §2.5: the timing words.
    ("transition-timing-function", "steps(4, jump-none)"),
    ("transition-timing-function", "steps(2, start)"),
    ("transition-timing-function", "linear(0, 0.5 25%, 1)"),
    ("transition-timing-function", "linear(0, 1, 0)"),
    ("transition-timing-function", "cubic-bezier(0.5, 0, 0.5, 1)"),
    ("transition-timing-function", "spring(1 80 10 10)"),
    ("animation-timing-function", "steps(1)"),
    ("animation-direction", "alternate-reverse"),
    ("animation-fill-mode", "both"), ("animation-play-state", "paused"),
    ("animation-trigger", "click"), ("animation-composition", "subtract"),
    # CSS View Transitions 1 §7: the transition tree.
    ("view-transition-name", "none"), ("view-transition-name", "auto"),
    ("view-transition-name", "a"), ("view-transition-classes", "none"),
    ("view-transition-class", "a"), ("view-transition-animation", "auto"),
    # CSS Box 3 legacy and Sizing 4: the old flexbox words and an aspect.
    ("box-orient", "horizontal"), ("box-direction", "reverse"),
    ("box-flex-group", "2"), ("box-lines", "multiple"),
    ("aspect-ratio", "auto"), ("aspect-ratio", "auto 1 / 2"), ("aspect-ratio", "0"),
    ("object-fit", "scale-down"), ("object-position", "top 10px right 20px"),
    ("object-view-box", "xywh(0% 0% 100% 100%)"), ("object-view-box", "none"),
    ("~", "offset-path"), ("~", "ruby-position"), ("~", "text-combine-upright"),
    ("~", "color-contrast"), ("~", "mask-border-source"), ("~", "paint-order"),
    ("~", "scroll-snap-type"), ("~", "caret-animation"), ("~", "math-depth"),
    ("~", "will-change"), ("~", "bleed"), ("~", "view-transition-name"),
]


def page():
    literal = "[" + ",".join(f'[{json.dumps(p)},{json.dumps(v)}]' for p, v in CASES) + "]"
    read = """  if (cases[i][1] !== '~') el.style.setProperty(cases[i][0], cases[i][1]);
  rows.push(i + '|' + cases[i][0] + '|' + cases[i][1]
      + '|sup=' + (cases[i][1] === '~' ? '-' : (CSS.supports(cases[i][0], cases[i][1]) ? 1 : 0))
      + '|echo=' + el.style.getPropertyValue(cases[i][0])
      + '|comp=' + getComputedStyle(el).getPropertyValue(cases[i][0]));"""
    template = """<!doctype html>
<html><head><meta charset="utf-8"><title>b314 sweep</title></head>
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
// textContent, not HTML: a value carrying '<' is read as a start tag and vanishes, which turns
// one row into another row.
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
with open(os.path.join(HERE, "_probe_b314_sweep.html"), "w", encoding="utf-8") as handle:
    handle.write(page())
print("cases:", len(CASES))
