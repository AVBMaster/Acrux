"""Generate the b328 sweep: the background layer longhands and the font
sub-properties, value by value.

The background-* longhands (clip, origin, size, repeat, position, attachment)
each get their keyword set, a box-pair, a list of two layers, a negative / a
bogus word — the shapes the shorthand sweeps only met once. The font-*
sub-properties (variant axes, feature-settings, kerning, synthesis, optical
sizing, emoji) get every keyword the specs name plus the two-value and list
forms. One fresh element per row (measured in b283: a reused host answers a
dropped declaration with the previous row's value).
"""
import json
import os

CASES = [
    # background-clip: the three boxes, the 'text' keyword, two layers, bogus.
    ("background-clip", "border-box"), ("background-clip", "padding-box"),
    ("background-clip", "content-box"), ("background-clip", "text"),
    ("background-clip", "border-box padding-box"), ("background-clip", "content-box, border-box"),
    ("background-clip", "bogus"), ("background-clip", "border"),
    # background-origin: three boxes (no 'text'), two layers, bogus.
    ("background-origin", "border-box"), ("background-origin", "padding-box"),
    ("background-origin", "content-box"), ("background-origin", "text"),
    ("background-origin", "padding-box content-box"), ("background-origin", "content-box, border-box"),
    ("background-origin", "bogus"),
    # background-size: keywords, one/two values, auto pairs, list, bogus.
    ("background-size", "auto"), ("background-size", "cover"), ("background-size", "contain"),
    ("background-size", "50% 50%"), ("background-size", "10px"), ("background-size", "10px 20px"),
    ("background-size", "auto 10px"), ("background-size", "10px auto"), ("background-size", "auto auto"),
    ("background-size", "cover, contain"), ("background-size", "cover cover"),
    ("background-size", "-10px"), ("background-size", "bogus"),
    # background-repeat: two-keyword, single keywords, space/round, list, bogus.
    ("background-repeat", "repeat"), ("background-repeat", "no-repeat"),
    ("background-repeat", "repeat-x"), ("background-repeat", "repeat-y"),
    ("background-repeat", "space"), ("background-repeat", "round"),
    ("background-repeat", "space round"), ("background-repeat", "repeat no-repeat"),
    ("background-repeat", "no-repeat, repeat"), ("background-repeat", "bogus"),
    ("background-repeat", "repeat repeat repeat"),
    # background-position: one value, two values, edge offsets, list, bogus.
    ("background-position", "center"), ("background-position", "left"), ("background-position", "top"),
    ("background-position", "0 0"), ("background-position", "50% 50%"),
    ("background-position", "left top"), ("background-position", "right bottom"),
    ("background-position", "10px 20px"), ("background-position", "10px"),
    ("background-position", "left 10px top"), ("background-position", "center bottom"),
    ("background-position", "0% 0%"), ("background-position", "10px, 20px"),
    ("background-position", "-10px -20px"), ("background-position", "bogus"),
    # background-attachment: the three keywords, list, bogus.
    ("background-attachment", "scroll"), ("background-attachment", "fixed"),
    ("background-attachment", "local"), ("background-attachment", "scroll, fixed"),
    ("background-attachment", "bogus"),
    # The background shorthand carrying a layer list and the position/size slash.
    ("background", "red"), ("background", "url(a.png) center / cover no-repeat"),
    ("background", "none"), ("background", "red, blue"), ("background", "bogus"),
    # font-variant: normal / none and one keyword per axis.
    ("font-variant", "normal"), ("font-variant", "none"),
    ("font-variant", "small-caps"), ("font-variant", "all-small-caps"),
    ("font-variant", "petite-caps"), ("font-variant", "all-petite-caps"),
    ("font-variant", "unicase"), ("font-variant", "titling-caps"),
    ("font-variant", "lining-nums"), ("font-variant", "oldstyle-nums"),
    ("font-variant", "proportional-nums"), ("font-variant", "tabular-nums"),
    ("font-variant", "diagonal-fractions"), ("font-variant", "stacked-fractions"),
    ("font-variant", "ordinal"), ("font-variant", "slashed-zero"),
    ("font-variant", "common-ligatures"), ("font-variant", "no-common-ligatures"),
    ("font-variant", "discretionary-ligatures"), ("font-variant", "no-discretionary-ligatures"),
    ("font-variant", "historical-ligatures"), ("font-variant", "no-historical-ligatures"),
    ("font-variant", "contextual"), ("font-variant", "no-contextual"),
    ("font-variant", "full-width"), ("font-variant", "proportional-width"),
    ("font-variant", "ruby"), ("font-variant", "bogus"),
    ("font-variant", "small-caps lining-nums ordinal"),
    # font-variant-caps / numeric / ligatures / east-asian / position / alternates.
    ("font-variant-caps", "normal"), ("font-variant-caps", "small-caps"),
    ("font-variant-caps", "all-small-caps"), ("font-variant-caps", "petite-caps"),
    ("font-variant-caps", "titling-caps"), ("font-variant-caps", "unicase"),
    ("font-variant-caps", "bogus"),
    ("font-variant-numeric", "normal"), ("font-variant-numeric", "lining-nums"),
    ("font-variant-numeric", "tabular-nums ordinal"), ("font-variant-numeric", "slashed-zero"),
    ("font-variant-numeric", "bogus"),
    ("font-variant-ligatures", "normal"), ("font-variant-ligatures", "none"),
    ("font-variant-ligatures", "common-ligatures"), ("font-variant-ligatures", "no-common-ligatures"),
    ("font-variant-ligatures", "discretionary-ligatures"), ("font-variant-ligatures", "bogus"),
    ("font-variant-east-asian", "normal"), ("font-variant-east-asian", "full-width"),
    ("font-variant-east-asian", "ruby"), ("font-variant-east-asian", "bogus"),
    ("font-variant-position", "normal"), ("font-variant-position", "sub"),
    ("font-variant-position", "super"), ("font-variant-position", "bogus"),
    ("font-variant-alternates", "normal"), ("font-variant-alternates", "historical-forms"),
    ("font-variant-alternates", "bogus"),
    # font-feature-settings: normal, a tag, tag + value forms, list, bogus.
    ("font-feature-settings", "normal"), ("font-feature-settings", '"kern"'),
    ("font-feature-settings", '"liga" 1'), ("font-feature-settings", '"smcp" on'),
    ("font-feature-settings", '"smcp" off'), ("font-feature-settings", '"kern", "liga"'),
    ("font-feature-settings", "kern"), ("font-feature-settings", "bogus"),
    ("font-feature-settings", '"kern" 2'),
    # font-kerning / synthesis / optical-sizing / variant-emoji.
    ("font-kerning", "auto"), ("font-kerning", "normal"), ("font-kerning", "none"),
    ("font-kerning", "bogus"),
    ("font-synthesis", "none"), ("font-synthesis", "weight"), ("font-synthesis", "style"),
    ("font-synthesis", "small-caps"), ("font-synthesis", "weight style"),
    ("font-synthesis", "bogus"),
    ("font-synthesis-weight", "auto"), ("font-synthesis-weight", "none"),
    ("font-synthesis-style", "auto"), ("font-synthesis-style", "none"),
    ("font-synthesis-small-caps", "auto"), ("font-synthesis-small-caps", "none"),
    ("font-optical-sizing", "auto"), ("font-optical-sizing", "none"),
    ("font-optical-sizing", "bogus"),
    ("font-variant-emoji", "normal"), ("font-variant-emoji", "text"),
    ("font-variant-emoji", "emoji"), ("font-variant-emoji", "unicode"),
    ("font-variant-emoji", "bogus"),
    # font-language-override / font-feature 'normal'.
    ("font-language-override", "normal"), ("font-language-override", '"ENG"'),
    ("font-language-override", "bogus"),
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
<html><head><meta charset="utf-8"><title>b328 sweep</title></head>
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
with open(os.path.join(HERE, "_probe_b328_bg_font.html"), "w", encoding="utf-8") as handle:
    handle.write(page())
print("cases:", len(CASES))