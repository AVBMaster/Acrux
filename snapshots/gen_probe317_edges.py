"""Generate the b317 edges probe: the boundary of each family the sweep found open.

The sweep said which seats differ; this table asks where the reference engine's boundary actually
is — which intrinsic keyword a flex basis may be, how a line named and numbered at once reads back,
what a duration's 'auto' may sit beside, which negative a property keeps and which it drops, and
what the font shorthand prints for a family of several words. One fresh element per row, five
columns.
"""
import json
import os

CASES = [
    # CSS Sizing 4 §2.2 as CSS Flexbox 1 §7 reads it: which widths a basis may be.
    ("flex-basis", "min-content"), ("flex-basis", "max-content"),
    ("flex-basis", "fit-content"), ("flex-basis", "stretch"),
    ("flex-basis", "fit-content(100px)"), ("flex-basis", "auto"),
    ("flex-basis", "content"), ("flex-basis", "min-content max-content"),
    ("flex", "min-content"), ("flex", "max-content"), ("flex", "stretch"),
    ("flex", "fit-content(100px)"), ("flex", "1 fit-content"),
    ("flex", "1 1 fit-content"), ("flex", "fit-content auto"),
    ("flex", "1 1 min-content max-content"),
    # CSS Grid 1 §7.1: a line that is named and numbered at once.
    ("grid-row-start", "a 2"), ("grid-row-start", "2 a"),
    ("grid-row-start", "a -1"), ("grid-row-start", "-1 a"),
    ("grid-row-start", "0 a"), ("grid-row-start", "a b"),
    ("grid-row-start", "span a 2"), ("grid-row-start", "a span 2"),
    ("grid-row-start", "1 2"), ("grid-row-start", "a 2 b"),
    ("grid-column-end", "b 3"), ("grid-row", "a 2 / 3 b"),
    ("grid-area", "a 1 / 2 / b 3 / 4"),
    # CSS Text 3 §3.3: what an indent may be, and what the two flags may stand on.
    ("text-indent", "auto"), ("text-indent", "hanging"),
    ("text-indent", "each-line"), ("text-indent", "10px hanging hanging"),
    ("text-indent", "each-line 10px"), ("text-indent", "50% hanging"),
    ("text-indent", "calc(10px + 1em) hanging"), ("text-indent", "-10px"),
    ("text-indent", "10px 20px"), ("text-indent", "0"),
    # CSS Multi-column 1 §6 and CSS Sizing: a width that is less than nothing.
    ("column-width", "-1px"), ("column-width", "-0px"),
    ("column-width", "calc(-1px)"), ("column-width", "0"),
    ("column-width", "1e3px"), ("column-width", "-1em"),
    ("columns", "-100px"), ("columns", "-1"),
    # Scroll-driven Animations 1 §5: 'auto' is a duration of an animation and of nothing else.
    ("animation-duration", "auto"), ("animation-duration", "AUTO"),
    ("animation-duration", "auto auto"), ("animation-duration", "1s auto"),
    ("animation-duration", "auto 1s"), ("animation-duration", "auto bogus"),
    ("transition-duration", "auto"), ("transition-delay", "auto"),
    ("animation-duration", "auto 2min"), ("animation-duration", "0s"),
    # CSS UI 4 §3.7: the caret's own two words.
    ("caret-animation", "MANUAL"), ("caret-animation", "manual auto"),
    ("caret-animation", "always"), ("caret-animation", "never"),
    ("caret-animation", "manual manual"),
    # CSS Text Decoration 4 §2: a thickness the page wrote negative.
    ("text-decoration-thickness", "-2px"), ("text-decoration-thickness", "-50%"),
    ("text-decoration-thickness", "calc(-1px)"), ("text-decoration-thickness", "0"),
    ("text-decoration-thickness", "-0px"),
    ("text-decoration", "underline wavy 2px red"), ("text-decoration", "2px"),
    ("text-decoration", "from-font red"), ("text-decoration", "underline 2px 3px"),
    ("text-decoration", "underline -2px"),
    # CSS Fonts 4 §5.7: what the shorthand prints for a name of several words.
    ("font", "12px Times New Roman"), ("font", "12px serif"),
    ("font", "bold 12px \"A\", B C"), ("font", "12px Arial"),
    ("font", "12px/1.4 Times New Roman"), ("font", "small-caps 12px FOO"),
    ("font-family", "Times New Roman, serif"),
    # CSS Borders 3 §6: the nine-patch's own arity.
    ("border-image-slice", "1 2 3 4"), ("border-image-slice", "fill 1"),
    ("border-image-slice", "1 fill 2"), ("border-image-slice", "10% 20%"),
    ("border-image-outset", "1px 2px 3px 4px"), ("border-image-outset", "0 auto"),
    ("border-image-width", "50%"), ("border-image-width", "1 2 3 4 5"),
    ("border-image-repeat", "round round"), ("border-image-repeat", "space"),
    # CSS Grid 1 §7.2: the track list's functions.
    ("grid-auto-rows", "fit-content(100px)"), ("grid-auto-rows", "minmax(auto, 1fr)"),
    ("grid-auto-rows", "minmax(1fr, auto)"), ("grid-auto-rows", "auto 1fr minmax(10px, 20px)"),
    ("grid-auto-rows", "1fr 1fr 1fr"), ("grid-auto-rows", "bogus"),
    # The seats whose word sets the sweep only asked once.
    ("pointer-events", "visibleFill"), ("pointer-events", "all"),
    ("pointer-events", "painted"), ("pointer-events", "none auto"),
    ("vertical-align", "20%"), ("vertical-align", "top bottom"),
    ("vertical-align", "text-bottom"), ("vertical-align", "-5px"),
    ("resize", "inline"), ("resize", "both horizontal"),
    ("user-select", "text"), ("user-select", "element"),
    ("text-orientation", "sideways-left"), ("text-orientation", "mixed upright"),
    ("writing-mode", "vertical-lr"), ("writing-mode", "sideways-rl"),
    ("writing-mode", "horizontal-tb vertical-rl"),
    ("caret-color", "abc"), ("caret-color", "red blue"),
    ("isolation", "isolation"), ("mix-blend-mode", "normal"),
    ("mix-blend-mode", "luminosity"), ("background-blend-mode", "normal"),
    ("mask-clip", "border-box padding-box"), ("mask-origin", "fill-box stroke-box"),
    ("background-position", "left 10% top"), ("background-position", "center center center"),
    ("outline-width", "calc(1px + 2px)"), ("outline-width", "0"),
    ("outline-offset", "50%"), ("outline-offset", "calc(1px)"),
    ("contain-intrinsic-block-size", "auto none"),
    ("contain-intrinsic-block-size", "10px 20px"),
    ("box-shadow", "inset 1px"), ("box-shadow", "0 0 0 0 bogus"),
    ("caption-side", "block-start"), ("caption-side", "bottom"),
    ("empty-cells", "show show"), ("border-spacing", "1px 2px 3px"),
    ("stroke-dasharray", "1, 2"), ("stroke-dasharray", "50%"),
    ("stroke-miterlimit", "0"), ("stroke-linecap", "BUTT"),
    ("fill-rule", "nonzero"), ("fill-rule", "evenodd nonzero"),
    ("grid-gap", "10px 20px 30px"), ("grid-row-gap", "10px 20px"),
    ("animation-timeline", "a, b"), ("animation-timeline", "auto"),
    ("animation-name", "a b"), ("animation-fill-mode", "backwards"),
    ("animation-direction", "alternate"), ("transition-property", "opacity, width"),
    ("transition-property", "all none"),
    ("flex-direction", "ROW"), ("flex-wrap", "WRAP-REVERSE"),
    ("font-style", "OBLIQUE 20DEG"), ("font-stretch", "50"),
    ("font-weight", "bold"), ("font-variant", "small-caps normal"),
    ("font-synthesis", "style"), ("font-synthesis-weight", "auto"),
    ("text-decoration-line", "grammar-error spelling-error"),
    ("text-decoration-style", "double solid"),
    ("text-decoration-color", "currentcolor"),
]


def page():
    literal = "[" + ",".join(f'[{json.dumps(p)},{json.dumps(v)}]' for p, v in CASES) + "]"
    read = """  if (cases[i][1] !== '~') el.style.setProperty(cases[i][0], cases[i][1]);
  rows.push(i + '|' + cases[i][0] + '|' + cases[i][1]
      + '|sup=' + (cases[i][1] === '~' ? '-' : (CSS.supports(cases[i][0], cases[i][1]) ? 1 : 0))
      + '|n=' + el.style.length
      + '|echo=' + el.style.getPropertyValue(cases[i][0])
      + '|cssText=' + el.style.cssText
      + '|comp=' + getComputedStyle(el).getPropertyValue(cases[i][0]));"""
    template = """<!doctype html>
<html><head><meta charset="utf-8"><title>b317 edges</title></head>
<body>
<div id="host"></div>
<script>
// edges
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
with open(os.path.join(HERE, "_probe_b317_edges.html"), "w", encoding="utf-8") as handle:
    handle.write(page())
print("cases:", len(CASES))
