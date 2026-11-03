"""Generate the b309 sweep probe: the keyword sets no round has swept yet.

Rows are written one per case on a fresh element (the rule b291 measured: a reused element keeps the
previous row's spelling). The columns are the three surfaces a value can be wrong on: what the
grammar answers, what the style attribute echoes, and what the computed surface answers.
"""
import os

CASES = [
    # CSS Line Grid 3 — the leading-trim family.
    ("leading-trim", "normal"), ("leading-trim", "trim-font-extents"),
    ("leading-trim", "trim-source"), ("leading-trim", "text-top"),
    ("leading-trim", "text-bottom"), ("leading-trim", "edge-side"),
    ("leading-trim", "edge"), ("leading-trim", "cap-edge"),
    ("leading-trim", "use-size"), ("leading-trim", "trim-both"),
    ("leading-trim", "trim-font"), ("leading-trim", "0"),
    ("line-gap-override", "normal"), ("line-gap-override", "100%"),
    ("line-gap-override", "0%"), ("line-gap-override", "1em"),
    ("line-gap-override", "-50%"), ("line-gap-override", "auto"),
    # CSS Text 4 — the spacing-trim pair and autospace.
    ("text-autospace", "normal"), ("text-autospace", "none"),
    ("text-autospace", "no-autospace"), ("text-autospace", "punctuation"),
    ("text-autospace", "replace"), ("text-autospace", "punctuation normal"),
    ("text-spacing-trim", "normal"), ("text-spacing-trim", "none"),
    ("text-spacing-trim", "space-first"), ("text-spacing-trim", "trim-start"),
    ("text-spacing-trim", "trim-both"), ("text-spacing-trim", "trim-all"),
    ("text-spacing-trim", "space-all"), ("text-spacing-trim", "trim-both space-first"),
    ("word-spacing-trim", "normal"), ("word-spacing-trim", "none"),
    ("word-spacing-trim", "space-first"), ("word-spacing-trim", "trim-start"),
    ("word-spacing-trim", "trim-both"), ("word-spacing-trim", "trim-all"),
    ("word-spacing-trim", "keep-all"),
    # CSS Overflow 4 — the clamp family.
    ("line-clamp", "none"), ("line-clamp", "2"), ("line-clamp", "2 100%"),
    ("line-clamp", "100%"), ("line-clamp", "0"), ("line-clamp", "-1"),
    ("line-clamp", "2.5"), ("line-clamp", "auto"),
    ("max-lines", "none"), ("max-lines", "3"), ("max-lines", "50%"),
    ("max-lines", "0"), ("block-ellipsis", "none"), ("block-ellipsis", "auto"),
    ('block-ellipsis', '"…"'), ("block-ellipsis", "'more'"),
    ("block-ellipsis", 'auto "x"'), ("block-ellipsis", "foo"),
    ("continue", "auto"), ("continue", "discard"), ("continue", "overflow"),
    ("continue", "none"),
    ("line-grid", "none"), ("line-grid", "true"), ("line-grid", "false"),
    ("line-snap", "none"), ("line-snap", "contain"), ("line-snap", "nearest"),
    ("baseline-source", "auto"), ("baseline-source", "first"), ("baseline-source", "last"),
    # overflow-clip-margin and the anchor/position-try family.
    ("overflow-clip-margin", "content-box"), ("overflow-clip-margin", "border-box"),
    ("overflow-clip-margin", "10px"), ("overflow-clip-margin", "0"),
    ("overflow-clip-margin", "padding-box"), ("overflow-clip-margin", "fill-box"),
    ("overflow-clip-margin", "-5px"),
    ("overflow-anchor", "auto"), ("overflow-anchor", "none"),
    ("position-try-order", "normal"), ("position-try-order", "most-width"),
    ("position-try-order", "most-height"), ("position-try-order", "most-inline-size"),
    ("position-try-order", "block"),
    ("position-try-fallbacks", "none"), ("position-try-fallbacks", "bottom"),
    ("position-try-fallbacks", "flip-block top"),
    ("position-try", "none"), ("position-try", "bottom"), ("position-try", "span-inline"),
    ("position-anchor", "none"), ("position-anchor", "--foo"),
    ("position-visibility", "auto"), ("position-visibility", "anchors-visible"),
    ("position-visibility", "no-clip"), ("position-visibility", "visible"),
    ("anchor-name", "none"), ("anchor-name", "--foo"), ("anchor-name", "--FOO"),
    ("anchor-scope", "none"), ("anchor-scope", "--foo"), ("anchor-scope", "all"),
    ("overlay", "none"), ("overlay", "auto"), ("overlay", "top"),
    # SVG rendering and paint, the surfaces a declaration answers.
    ("shape-rendering", "auto"), ("shape-rendering", "optimizeSpeed"),
    ("shape-rendering", "crispEdges"), ("shape-rendering", "geometricPrecision"),
    ("shape-rendering", "optimizeQuality"), ("shape-rendering", "crisp-edges"),
    ("text-rendering", "auto"), ("text-rendering", "optimizeSpeed"),
    ("text-rendering", "optimizeLegibility"), ("text-rendering", "geometricPrecision"),
    ("text-rendering", "optimize"),
    ("paint-order", "normal"), ("paint-order", "stroke"), ("paint-order", "fill stroke markers"),
    ("paint-order", "markers stroke"), ("paint-order", "stroke fill"),
    ("paint-order", "fill fill"), ("paint-order", "auto"),
    ("vector-effect", "none"), ("vector-effect", "non-scaling-stroke"),
    ("vector-effect", "zoom-and-pan"), ("vector-effect", "scaled-down"),
    ("vector-effect", "flip-y"), ("vector-effect", "no-scaling-stroke"),
    ("dominant-baseline", "auto"), ("dominant-baseline", "alphabetic"),
    ("dominant-baseline", "ideographic"), ("dominant-baseline", "hanging"),
    ("dominant-baseline", "mathematical"), ("dominant-baseline", "central"),
    ("dominant-baseline", "middle"), ("dominant-baseline", "text-before-edge"),
    ("dominant-baseline", "text-after-edge"), ("dominant-baseline", "use-script"),
    ("dominant-baseline", "reset-size"), ("dominant-baseline", "no-change"),
    ("alignment-baseline", "auto"), ("alignment-baseline", "baseline"),
    ("alignment-baseline", "before-edge"), ("alignment-baseline", "middle"),
    ("alignment-baseline", "central"), ("alignment-baseline", "after-edge"),
    ("alignment-baseline", "ideographic"), ("alignment-baseline", "mathematical"),
    ("baseline-shift", "baseline"), ("baseline-shift", "super"), ("baseline-shift", "sub"),
    ("baseline-shift", "10px"), ("baseline-shift", "50%"), ("baseline-shift", "-3px"),
    ("fill", "none"), ("fill", "currentColor"), ("fill", "red"), ("fill", "url(#a)"),
    ("fill", "url(#a) red"), ("fill", "#abc"),
    ("stroke", "none"), ("stroke", "red"), ("stroke", "url(#a)"),
    ("stroke-dasharray", "none"), ("stroke-dasharray", "5 2"), ("stroke-dasharray", "5,2"),
    ("stroke-dasharray", "1 2 3"), ("stroke-dasharray", "50%"),
    ("stroke-dasharray", "auto"), ("marker-start", "none"), ("marker-start", "url(#a)"),
    ("marker-start", "url(#a) red"), ("clip-rule", "nonzero"), ("clip-rule", "evenodd"),
    ("clip-rule", "all"), ("fill-rule", "nonzero"), ("fill-rule", "evenodd"),
    ("color-interpolation", "sRGB"), ("color-interpolation", "linearRGB"),
    ("color-interpolation", "auto"), ("color-interpolation-filters", "sRGB"),
    ("color-rendering", "auto"), ("color-rendering", "optimizeSpeed"),
    ("color-rendering", "optimizeQuality"),
    # CSS Fonts 4 — the settings family and size adjust.
    ("font-palette", "normal"), ("font-palette", "light"), ("font-palette", "dark"),
    ("font-palette", "0"), ("font-palette", "1"),
    ("font-optical-sizing", "auto"), ("font-optical-sizing", "none"),
    ("font-variation-settings", "normal"), ("font-variation-settings", '"wght" 400'),
    ("font-variation-settings", '"wght"'),
    ("font-feature-settings", "normal"), ("font-feature-settings", '"liga" 1, "dlig" 0'),
    ("font-feature-settings", '"smcp"'), ("font-feature-settings", '"liga" on'),
    ("font-language-override", "normal"), ("font-language-override", '"SGML"'),
    ("font-language-override", "dflt"),
    ("font-size-adjust", "none"), ("font-size-adjust", "from-font"),
    ("font-size-adjust", "0.5"), ("font-size-adjust", "ex-height 0.5"),
    ("font-size-adjust", "cap-height 0.8"), ("font-size-adjust", "ch-width 0.5"),
    ("font-size-adjust", "ic-height 0.5"), ("font-size-adjust", "bu-scale 0.5"),
    ("font-size-adjust", "ex-height"), ("font-size-adjust", "from-font 0.5"),
    ("font-size-adjust", "auto"), ("font-size-adjust", "-1"),
    ("font-variant-emoji", "normal"), ("font-variant-emoji", "text"),
    ("font-variant-emoji", "emoji"), ("font-variant-emoji", "unicode"),
    # CSS Fonts 4 — the variant subtags no round has swept.
    ("font-variant-east-asian", "normal"), ("font-variant-east-asian", "full-width"),
    ("font-variant-east-asian", "proportional-width"), ("font-variant-east-asian", "ruby"),
    ("font-variant-east-asian", "isolated"), ("font-variant-east-asian", "third-width"),
    ("font-variant-east-asian", "quarter-width"),
    ("font-variant-ligatures", "normal"), ("font-variant-ligatures", "none"),
    ("font-variant-ligatures", "common-ligatures"),
    ("font-variant-ligatures", "no-common-ligatures"),
    ("font-variant-ligatures", "rare-ligatures"),
    ("font-variant-ligatures", "historical-ligatures"),
    ("font-variant-ligatures", "contextual"), ("font-variant-ligatures", "no-contextual"),
    ("font-variant-numeric", "normal"), ("font-variant-numeric", "ordinal"),
    ("font-variant-numeric", "slashed-zero"), ("font-variant-numeric", "lining-nums"),
    ("font-variant-numeric", "oldstyle-nums"), ("font-variant-numeric", "tabular-nums"),
    ("font-variant-numeric", "proportional-nums"), ("font-variant-numeric", "diagonal-fractions"),
    ("font-variant-numeric", "stacked-fractions"), ("font-variant-numeric", "all-small-caps"),
    ("font-variant-alternates", "normal"), ("font-variant-alternates", "historical-forms"),
    ("font-variant-alternates", "styleset(foo)"), ("font-variant-alternates", 'character-variant(2)'),
    ("font-variant-alternates", "ornament(7)"), ("font-variant-alternates", "annotation(2)"),
    ("font-kerning", "auto"), ("font-kerning", "normal"), ("font-kerning", "none"),
    ("font-variant-position", "normal"), ("font-variant-position", "super"),
    ("font-variant-position", "sub"), ("font-variant-position", "superscript"),
    # CSS Math Deep 2.
    ("math-style", "normal"), ("math-style", "compact"), ("math-style", "auto"),
    ("math-shift", "normal"), ("math-shift", "bold-italic"), ("math-shift", "italic"),
    ("math-shift", "auto"), ("math-depth", "auto"), ("math-depth", "1"),
    ("math-depth", "-1"), ("math-depth", "2.5"), ("math-depth", "0px"),
    # Chrome-only legacy seats no round has read.
    ("-webkit-app-region", "no-drag"), ("-webkit-app-region", "drag"),
    ("-webkit-app-region", "foo"), ("input-security", "auto"), ("input-security", "none"),
    ("text-orientation", "mixed"), ("writing-mode", "vertical-rl"),
    ("writing-mode", "sideways-lr"), ("unicode-bidi", "plaintext"),
    ("unicode-bidi", "isolate-override"), ("unicode-bidi", "embed"),
    ("transform-box", "content-box"), ("transform-box", "border-box"),
    ("transform-box", "fill-box"), ("transform-box", "stroke-box"),
    ("transform-box", "view-box"),
]

READ = """  if (cases[i][1] !== '~') el.style.setProperty(cases[i][0], cases[i][1]);
  rows.push(i + '|' + cases[i][0] + '|' + cases[i][1]
      + '|sup=' + (cases[i][1] === '~' ? '-' : (CSS.supports(cases[i][0], cases[i][1]) ? 1 : 0))
      + '|echo=' + el.style.getPropertyValue(cases[i][0])
      + '|comp=' + getComputedStyle(el).getPropertyValue(cases[i][0]));"""


def page(tag, cases):
    import json
    literal = "[" + ",".join(f'[{json.dumps(p)},{json.dumps(v)}]' for p, v in cases) + "]"
    return """<!doctype html>
<html><head><meta charset="utf-8"><title>b309 %s sweep</title></head>
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
""" % (tag, tag, literal, READ)


HERE = os.path.dirname(os.path.abspath(__file__))
with open(os.path.join(HERE, "_probe_b309_sweep.html"), "w", encoding="utf-8") as handle:
    handle.write(page("sweep", CASES))
print("cases:", len(CASES))
