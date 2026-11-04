"""Generate the b313 sweep probe: the property families no round has swept yet.

One fresh element per row (the rule b291 measured), three columns: what the grammar answers, what
the style attribute echoes and what the computed surface answers. A value of '~' asks only the
computed surface, which is how a property with no grammar of its own is still measured.
"""
import json
import os

CASES = [
    # CSS Rounding 1 §3, §4: the corner's own shape, on one corner and on all four.
    ("corner-shape", "round"), ("corner-shape", "square"), ("corner-shape", "scoop"),
    ("corner-shape", "notch"), ("corner-shape", "bevel"), ("corner-shape", "uncurved"),
    ("corner-shape", "superellipse(1.5)"), ("corner-shape", "superellipse(infinity)"),
    ("corner-shape", "superellipse(-2)"), ("corner-shape", "superellipse(0)"),
    ("corner-shape", "round scoop"), ("corner-shape", "round scoop round"),
    ("corner-shape", "round scoop round bevel"), ("corner-shape", "none"),
    ("corner-top-left-shape", "scoop"), ("corner-bottom-right-shape", "notch"),
    ("corner-start-start-shape", "bevel"), ("corner-end-shape", "square"),
    ("border-radius", "10px 20px round"), ("border-radius", "round 10px"),
    # CSS Values 4 §11.1 and Containment 3: a size function written inside a size.
    ("width", "calc-size(auto, size + 10px)"), ("width", "calc-size(<length-percentage>, 100%)"),
    ("height", "calc-size(auto, 20px)"), ("height", "calc-size(auto, size * 2)"),
    ("width", "calc-size(auto, size)"), ("width", "calc-size(intrinsic, size)"),
    ("width", "calc-size(auto)"), ("min-height", "calc-size(auto, size + 1em)"),
    # CSS Animations 2 §3 and Scroll-driven 1 §3: the timeline family.
    ("animation-timeline", "auto"), ("animation-timeline", "none"),
    ("animation-timeline", "scroll()"), ("animation-timeline", "scroll(nearest block)"),
    ("animation-timeline", "view(200px)"), ("animation-timeline", "--name"),
    ("animation-timeline", "span-a"), ("animation-range", "normal"),
    ("animation-range", "entry 0% exit 100%"), ("animation-range-start", "entry"),
    ("animation-range-end", "cover 50%"), ("animation-composition", "add"),
    ("animation-composition", "accumulate"), ("animation-composition", "replace, add"),
    ("animation-range", "entry 10% exit"),
    ("scroll-timeline-name", "--t"), ("scroll-timeline-axis", "block"),
    ("scroll-timeline-axis", "inline-block-x"), ("view-timeline-inset", "auto 10px"),
    ("timeline-scope", "--t"), ("timeline-scope", "all"),
    # CSS Anchor Positioning 1 §4, §7: the anchor a box is placed against.
    ("position-anchor", "--a"), ("position-anchor", "auto"),
    ("position-anchor", "none"), ("position-anchor", "--a --b"),
    ("position-visibility", "auto"), ("position-visibility", "anchors-visible"),
    ("position-visibility", "no-overflow"),
    ("position-visibility", "anchors-visible no-overflow"),
    ("left", "anchor(--a right)"), ("top", "anchor(center)"),
    ("margin-left", "anchor(--a start)"), ("width", "anchor(--a width)"),
    ("position-anchor", "anchor(--a)"),
    # CSS Transforms 2 §2, §4: the individual properties' own spellings.
    ("rotate", "x 45deg"), ("rotate", "1 1 0 45deg"), ("rotate", "45deg"),
    ("rotate", "z 45deg 20deg"), ("rotate", "none"), ("rotate", "x y 45deg"),
    ("translate", "10px 20px"), ("translate", "10px"), ("translate", "none"),
    ("translate", "10px 20px 30px"), ("scale", "2 3"), ("scale", "2 3 4"),
    ("scale", "none"), ("scale", "50%"), ("perspective", "none"),
    ("perspective", "0"), ("perspective", "-10px"),
    ("transform-box", "view-box"), ("transform-box", "content-box"),
    ("transform-box", "border-box"), ("transform-box", "fill-box"),
    ("backface-visibility", "visible"), ("backface-visibility", "hidden"),
    ("rotate", "calc(45deg + 1turn)"), ("translate", "calc(10px + 1em) 20%"),
    # CSS UI 4 §4 and §6: what the widget is allowed to look like.
    ("appearance", "textfield"), ("appearance", "menulist"), ("appearance", "listbox"),
    ("appearance", "progress-bar"), ("appearance", "meter"), ("appearance", "searchfield"),
    ("appearance", "auto"), ("appearance", "none"), ("appearance", "button"),
    ("appearance", "textarea"), ("appearance", "checkbox"), ("appearance", "push-button"),
    ("appearance", "slider-horizontal"), ("appearance", "inner-text-field"),
    ("accent-color", "auto"), ("accent-color", "red"), ("accent-color", "currentColor"),
    ("color-scheme", "light dark"), ("color-scheme", "normal"),
    ("color-scheme", "light only"), ("color-scheme", "--custom light"),
    # CSS Fonts 5 §3, §9: the palette, the optical size and the variations.
    ("font-palette", "light"), ("font-palette", "dark"), ("font-palette", "color(2)"),
    ("font-palette", "color(2 light)"), ("font-palette", "normal"),
    ("font-optical-sizing", "auto"), ("font-optical-sizing", "none"),
    ("font-variation-settings", '"wght" 400'), ("font-variation-settings", "'wght' 400, 'wdth' 80"),
    ("font-variation-settings", "normal"), ("font-variation-settings", '"wght"'),
    ("font-named-instance", '"Book"'), ("font-named-instance", "auto"),
    ("font-language-override", '"SRB"'), ("font-language-override", "normal"),
    ("font-synthesis-position", "auto"), ("font-synthesis-position", "none"),
    ("font-revert", "none"),
    # CSS Text 4 §2.5, §5.6 and Lists 3 §3: the trim, the emphasis and the counter's own list.
    ("word-spacing-trim", "normal"), ("word-spacing-trim", "trim-all"),
    ("word-spacing-trim", "collapse-spaces"), ("word-spacing-trim", "trim-inter-cluster-spaces"),
    ("word-spacing-trim", "collapse"), ("word-spacing-trim", "trim-all collapse-spaces"),
    ("emphasize-punctuation", "auto"), ("emphasize-punctuation", "first"),
    ("emphasize-punctuation", "last"), ("emphasize-punctuation", "force-end"),
    ("counter-reset", "none"), ("counter-reset", "x"), ("counter-reset", "x none"),
    ("counter-set", "none"), ("counter-set", "x 1"),
    ("counter-increment", "x none"), ("list-style-type", "symbols('*')"),
    ("list-style-type", "symbols(cyclic '*')"),
    ("list-style-type", "symbols(alphabetic '0123456789')"),
    ("list-style-type", 'symbols(fixed "a" "b" "c")'),
    ("list-style-type", "symbols('*' circular)"),
    # CSS Grid 2 §4, §6 and Box Alignment 3: subgrid, the baseline pair and an area's name.
    ("grid-template-columns", "subgrid"), ("grid-template-columns", "subgrid [a]"),
    ("grid-template-rows", "subgrid row"), ("grid-template-rows", "subgrid row column"),
    ("align-content", "baseline"), ("align-content", "first baseline"),
    ("align-content", "last true"), ("justify-items", "legacy left"),
    ("justify-items", "right"), ("row-gap", "1lh"), ("row-gap", "calc(10px + 1em)"),
    ("grid-area", "span 2 / span 3"), ("grid-area", "a / b / c"),
    ("grid-column-end", "span"), ("grid-template-areas", '"a a" "a"'),
    ("grid-template-areas", "none"), ("grid-template", "subgrid / 10px"),
    # CSS Overflow 4 §3.6, §4.1 and CSSOM View: the string a clipped line prints.
    ("text-overflow", "'…'"), ("text-overflow", "'…' '→'"), ("text-overflow", "ellipsis ellipsis"),
    ("text-overflow", "clip ellipsis"), ("text-overflow", '"" "x"'),
    ("text-overflow", "ellipsis ''"), ("text-overflow", "none ellipsis"),
    ("scroll-start-align", "center"), ("scroll-start-align", "auto"),
    ("scroll-start-target", "auto"), ("scroll-start-target", "none"),
    ("scroll-margin-block", "10px 20px"), ("scroll-padding-inline", "auto 5px"),
    # CSS Color 5 §3, §4: the mix's own space and method, and a relative channel's math.
    ("color", "color-mix(in oklch longer hue, red 20%, blue)"),
    ("color", "color-mix(in hsl increasing hue, red, blue 40%)"),
    ("color", "color-mix(in srgb, red, blue)"),
    ("color", "lab(from red calc(l + 10) a b)"),
    ("color", "hwb(from red calc(h + 30) w b)"),
    ("color", "oklch(from red l c none)"),
    ("color", "rgb(from red none g b)"),
    ("color", "color(srgb-linear 1 0 0)"), ("color", "color(xyz 0.2 0.1 0.05)"),
    ("color", "color(display-p3 1 0 0 / 50%)"),
    ("~", "corner-shape"), ("~", "animation-timeline"), ("~", "position-anchor"),
    ("~", "font-palette"), ("~", "word-spacing-trim"), ("~", "scroll-start-align"),
    ("~", "emphasize-punctuation"), ("~", "appearance"), ("~", "accent-color"),
    ("~", "grid-template-columns"), ("~", "text-overflow"), ("~", "calc-size"),
]


def page():
    literal = "[" + ",".join(f'[{json.dumps(p)},{json.dumps(v)}]' for p, v in CASES) + "]"
    read = """  if (cases[i][1] !== '~') el.style.setProperty(cases[i][0], cases[i][1]);
  rows.push(i + '|' + cases[i][0] + '|' + cases[i][1]
      + '|sup=' + (cases[i][1] === '~' ? '-' : (CSS.supports(cases[i][0], cases[i][1]) ? 1 : 0))
      + '|echo=' + el.style.getPropertyValue(cases[i][0])
      + '|comp=' + getComputedStyle(el).getPropertyValue(cases[i][0]));"""
    return """<!doctype html>
<html><head><meta charset="utf-8"><title>b313 sweep</title></head>
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
    '<pre id="record" style="font:10px/1.35 monospace;white-space:pre-wrap"></pre>');
// The record is filled through textContent, not HTML: a value carrying '<length-percentage>' is
// otherwise parsed as a start tag and vanishes, which turns a row into a different row.
document.getElementById('record').textContent = rows.join('\\n');
console.log(rows.join('\\n'));
host.remove();
}
build();
</script>
</body></html>
""" % (literal, read)


HERE = os.path.dirname(os.path.abspath(__file__))
with open(os.path.join(HERE, "_probe_b313_sweep.html"), "w", encoding="utf-8") as handle:
    handle.write(page())
print("cases:", len(CASES))
