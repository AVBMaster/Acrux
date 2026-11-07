"""Generate the b321 sweep: the properties no round has swept yet.

Every earlier value sweep covered a different family; this one takes the 81
property names none of them measured, each with a value matrix — the legal
form, a variant spelling, a wrong one, a list, a negative or out-of-range
number and a bogus word — so each seat's four surfaces can be compared against
the reference engine. One fresh element per row (measured in b283: a reused
host answers a dropped declaration with the previous row's value).
"""
import json
import os

CASES = [
    ("align-content", "normal"), ("align-content", "center"), ("align-content", "start"),
    ("align-content", "space-between"), ("align-content", "space-around"),
    ("align-content", "first baseline"), ("align-content", "safe center"),
    ("align-content", "center start"), ("align-content", "bogus"),
    ("aspect-ratio", "auto"), ("aspect-ratio", "16 / 9"), ("aspect-ratio", "1.5"),
    ("aspect-ratio", "16/9"), ("aspect-ratio", "auto 16 / 9"), ("aspect-ratio", "0"),
    ("aspect-ratio", "-1"), ("aspect-ratio", "bogus"),
    ("backdrop-filter", "none"), ("backdrop-filter", "blur(2px)"),
    ("backdrop-filter", "brightness(1.5) saturate(2)"), ("backdrop-filter", "bogus"),
    ("backdrop-filter", "url(#p)"), ("backdrop-filter", "blur(2px) blur(3px)"),
    ("clear", "none"), ("clear", "left"), ("clear", "right"), ("clear", "both"),
    ("clear", "inline-start"), ("clear", "inline-end"), ("clear", "bogus"),
    ("clip", "auto"), ("clip", "rect(0px, 10px, 10px, 0px)"), ("clip", "rect(0, 10, 10, 0)"),
    ("clip", "inset(10px)"), ("clip", "bogus"),
    ("color", "red"), ("color", "#abc"), ("color", "rgb(1 2 3)"),
    ("color", "hsl(120 50% 50%)"), ("color", "color-mix(in srgb, red, blue)"),
    ("color", "bogus"), ("color", "rgb(1,2)"), ("color", "COLOR"),
    ("content", "none"), ("content", "normal"), ("content", '"x"'),
    ("content", "attr(x)"), ("content", "open-quote"), ("content", "counter(x)"),
    ("content", "url(a.png)"), ("content", "counter(x) ' / '"),
    ("counter-increment", "none"), ("counter-increment", "x"), ("counter-increment", "x 2"),
    ("counter-increment", "x y"), ("counter-increment", "x -1"), ("counter-increment", "bogus"),
    ("counter-reset", "none"), ("counter-reset", "x"), ("counter-reset", "x 3"),
    ("counter-reset", "x 3 y 4"), ("counter-reset", "bogus"),
    ("counter-set", "none"), ("counter-set", "x 2"), ("counter-set", "x"), ("counter-set", "bogus"),
    ("cursor", "auto"), ("cursor", "pointer"), ("cursor", "grab"), ("cursor", "url(a.png), auto"),
    ("cursor", "bogus"), ("cursor", "pointer pointer"),
    ("filter", "none"), ("filter", "blur(2px)"), ("filter", "drop-shadow(1px 2px red)"),
    ("filter", "url(#p)"), ("filter", "blur(2px) brightness(1.5)"),
    ("filter", "blur()"), ("filter", "bogus"), ("filter", "opacity(2)"),
    ("float", "none"), ("float", "left"), ("float", "right"), ("float", "inline-start"),
    ("float", "inline-end"), ("float", "bogus"),
    ("font-feature-settings", "normal"), ("font-feature-settings", '"liga"'),
    ("font-feature-settings", '"liga" 0'), ("font-feature-settings", "liga"),
    ("font-feature-settings", '"smcp" 1, "liga" 0'), ("font-feature-settings", "bogus"),
    ("font-kerning", "auto"), ("font-kerning", "normal"), ("font-kerning", "none"),
    ("font-kerning", "bogus"),
    ("font-optical-sizing", "auto"), ("font-optical-sizing", "none"),
    ("font-optical-sizing", "bogus"),
    ("font-variant-alternates", "normal"), ("font-variant-alternates", "historical-forms"),
    ("font-variant-alternates", "styleset(a)"), ("font-variant-alternates", "bogus"),
    ("font-variant-east-asian", "normal"), ("font-variant-east-asian", "full-width"),
    ("font-variant-east-asian", "ruby"), ("font-variant-east-asian", "bogus"),
    ("font-variant-ligatures", "normal"), ("font-variant-ligatures", "no-common-ligatures"),
    ("font-variant-ligatures", "common-ligatures"), ("font-variant-ligatures", "bogus"),
    ("font-variant-numeric", "normal"), ("font-variant-numeric", "tabular-nums"),
    ("font-variant-numeric", "oldstyle-nums"), ("font-variant-numeric", "bogus"),
    ("hyphenate-character", "auto"), ("hyphenate-character", '"-"'),
    ("hyphenate-character", "none"), ("hyphenate-character", "bogus"),
    ("hyphens", "none"), ("hyphens", "manual"), ("hyphens", "auto"), ("hyphens", "bogus"),
    ("image-orientation", "from-image"), ("image-orientation", "none"),
    ("image-orientation", "90deg"), ("image-orientation", "flip"), ("image-orientation", "bogus"),
    ("image-rendering", "auto"), ("image-rendering", "smooth"), ("image-rendering", "pixelated"),
    ("image-rendering", "crisp-edges"), ("image-rendering", "bogus"),
    ("justify-content", "normal"), ("justify-content", "center"),
    ("justify-content", "space-between"), ("justify-content", "safe center"),
    ("justify-content", "left"), ("justify-content", "bogus"),
    ("justify-items", "normal"), ("justify-items", "center"), ("justify-items", "stretch"),
    ("justify-items", "legacy left"), ("justify-items", "bogus"),
    ("line-break", "auto"), ("line-break", "strict"), ("line-break", "loose"),
    ("line-break", "anywhere"), ("line-break", "bogus"),
    ("line-height", "normal"), ("line-height", "1.5"), ("line-height", "20px"),
    ("line-height", "50%"), ("line-height", "0"), ("line-height", "-1"), ("line-height", "bogus"),
    ("list-style-image", "none"), ("list-style-image", "url(a.png)"),
    ("list-style-image", "linear-gradient(red, blue)"), ("list-style-image", "bogus"),
    ("list-style-position", "outside"), ("list-style-position", "inside"),
    ("list-style-position", "bogus"),
    ("list-style-type", "disc"), ("list-style-type", "square"), ("list-style-type", "none"),
    ("list-style-type", "'-'"), ("list-style-type", "counter(x)"), ("list-style-type", "bogus"),
    ("mask-composite", "add"), ("mask-composite", "subtract"), ("mask-composite", "bogus"),
    ("mask-mode", "match-source"), ("mask-mode", "alpha"), ("mask-mode", "luminance"),
    ("mask-mode", "bogus"),
    ("mask-repeat", "repeat"), ("mask-repeat", "no-repeat"), ("mask-repeat", "space round"),
    ("mask-repeat", "bogus"),
    ("mask-size", "auto"), ("mask-size", "cover"), ("mask-size", "50% 50%"),
    ("mask-size", "10px 20px"), ("mask-size", "bogus"),
    ("object-fit", "fill"), ("object-fit", "contain"), ("object-fit", "cover"),
    ("object-fit", "none"), ("object-fit", "scale-down"), ("object-fit", "bogus"),
    ("object-position", "50% 50%"), ("object-position", "left top"),
    ("object-position", "10px 20px"), ("object-position", "center"), ("object-position", "bogus"),
    ("opacity", "1"), ("opacity", "0.5"), ("opacity", "0"), ("opacity", "2"),
    ("opacity", "-0.5"), ("opacity", "50%"), ("opacity", "bogus"),
    ("overflow", "visible"), ("overflow", "hidden"), ("overflow", "auto"),
    ("overflow", "hidden auto"), ("overflow", "bogus"),
    ("paint-order", "normal"), ("paint-order", "stroke fill"), ("paint-order", "fill"),
    ("paint-order", "bogus"),
    ("position", "static"), ("position", "relative"), ("position", "absolute"),
    ("position", "fixed"), ("position", "sticky"), ("position", "bogus"),
    ("quotes", "auto"), ("quotes", "none"), ('quotes', '"«" "»"'),
    ('quotes', '"«" "»" "„" "“"'), ("quotes", "bogus"),
    ("scroll-behavior", "auto"), ("scroll-behavior", "smooth"), ("scroll-behavior", "bogus"),
    ("shape-outside", "none"), ("shape-outside", "margin-box"),
    ("shape-outside", "circle(50%)"), ("shape-outside", "url(a.png)"),
    ("shape-outside", "inset(10px)"), ("shape-outside", "bogus"),
    ("text-combine-upright", "none"), ("text-combine-upright", "all"),
    ("text-combine-upright", "digits"), ("text-combine-upright", "bogus"),
    ("text-justify", "auto"), ("text-justify", "inter-word"), ("text-justify", "none"),
    ("text-justify", "bogus"),
    ("text-transform", "none"), ("text-transform", "uppercase"),
    ("text-transform", "capitalize"), ("text-transform", "full-width"),
    ("text-transform", "bogus"), ("text-transform", "uppercase lowercase"),
    ("text-wrap", "wrap"), ("text-wrap", "nowrap"), ("text-wrap", "balance"),
    ("text-wrap", "pretty"), ("text-wrap", "stable"), ("text-wrap", "bogus"),
    ("white-space", "normal"), ("white-space", "pre"), ("white-space", "pre-wrap"),
    ("white-space", "pre-line"), ("white-space", "nowrap"), ("white-space", "break-spaces"),
    ("white-space", "bogus"),
    ("z-index", "auto"), ("z-index", "0"), ("z-index", "10"), ("z-index", "-1"),
    ("z-index", "1.5"), ("z-index", "bogus"),
    ("zoom", "normal"), ("zoom", "reset"), ("zoom", "1.5"), ("zoom", "50%"),
    ("zoom", "0"), ("zoom", "-1"), ("zoom", "bogus"),
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
<html><head><meta charset="utf-8"><title>b321 sweep</title></head>
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
with open(os.path.join(HERE, "_probe_b321_sweep.html"), "w", encoding="utf-8") as handle:
    handle.write(page())
print("cases:", len(CASES))
