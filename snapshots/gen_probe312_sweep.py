"""Generate the b312 sweep probe: the property families no round has swept yet.

One fresh element per row (the rule b291 measured), three columns: what the grammar answers, what
the style attribute echoes and what the computed surface answers.
"""
import json
import os

CASES = [
    # CSS Color 5: a palette index, and a colour that is only a component change.
    ("color", "device-cmyk(0% 50% 100% 0.5)"), ("color", "device-cmyk(0 0 0 1)"),
    ("color", "color(from red r g b)"), ("color", "color(from red 25% g b)"),
    ("color", "color(from red r g b / 0.5)"), ("color", "color(from blue h 50% 50%)"),
    ("color", "lab(from red 50% a b)"), ("color", "lch(from red l c h / 50%)"),
    ("color", "color-mix(in oklab, red 30%, blue)"),
    ("color", "color-mix(in srgb longer hue, red, blue)"),
    ("color", "rgb(1 2 3 / none)"), ("color", "hsl(none 50% 50%)"),
    ("color", "rgb(1 2 3 / calc(10% + 5))"),
    ("color", "rgb(1 2 3 / 1e2%)"), ("color", "color(srgb 1 0 0 / none)"),
    ("color", "light-dark(red, blue)"), ("color", "light-dark(red)"),
    # CSS Transitions 2 §4.4 and Anchor Positioning §6.4: the two at-rules' preludes.
    ("transition-property", "-webkit-transform"), ("transition-property", "anchor-size(width)"),
    ("transition-property", "--custom"), ("transition-property", "all, none"),
    ("transition-property", "none, all"), ("transition-property", "span-a"),
    # CSS Text 3 §5.5 and CSS Text 4: the hyphen and the justification pair.
    ("hyphens", "manual"), ("hyphens", "auto"), ("hyphens", "none"),
    ("hyphenate-character", '"-"'), ("hyphenate-character", "'…'"),
    ("hyphenate-character", "auto"), ("hyphenate-character", "none"),
    ("hyphenate-limit-chars", "auto"), ("hyphenate-limit-chars", "5"),
    ("hyphenate-limit-chars", "5 3 3"), ("hyphenate-limit-chars", "5 3 3 2"),
    ("hyphenate-limit-zone", "10px"), ("hyphenate-limit-zone", "20%"),
    ("hyphenate-limit-zone", "auto"),
    ("text-justify", "auto"), ("text-justify", "inter-word"),
    ("text-justify", "distribute"), ("text-justify", "none"),
    # CSS Text 4 §3.2 and CSS Text 3 §4.5: the pair of a line's edges.
    ("line-padding", "10px"), ("line-padding", "auto"),
    ("line-padding", "10px 20px"), ("line-padding", "50%"),
    ("justify-content", "right"), ("justify-content", "left"),
    ("justify-content", "legacy"), ("justify-content", "legacy right"),
    ("justify-content", "center baseline"), ("justify-content", "first center"),
    # CSS Overflow 3 §3.7 and CSSOM View: the scrollbar's own colours and the marker pair.
    ("scrollbar-color", "auto"), ("scrollbar-color", "red blue"),
    ("scrollbar-color", "red"), ("scrollbar-color", "transparent auto"),
    ("scrollbar-width", "auto"), ("scrollbar-width", "thin"), ("scrollbar-width", "none"),
    ("scrollbar-width", "vertical"),
    ("scroll-start", "auto"), ("scroll-start", "center"),
    ("scroll-start", "auto center"), ("scroll-start", "none"),
    ("scroll-start-target", "auto"), ("scroll-start-target", "none"),
    # CSS Anchor Positioning: the target text and the sizing function on an inset.
    ("target-text", "none"), ("target-text", "auto"), ("target-text", "highlight"),
    ("target-text", "current"), ("target-text", "span-a"),
    ("anchor-default", "--a"), ("anchor-default", "none"),
    ("anchor-default", "--a --b"),
    ("width", "anchor-size(self-inline)"), ("width", "anchor-size(--a self-end)"),
    ("width", "calc(anchor-size(width) + 10px)"),
    # CSS Box Sizing 4: the box a percentage is measured against.
    ("box-sizing", "border-box"), ("box-sizing", "content-box"),
    ("box-sizing", "stretch"), ("box-sizing", "inherit"),
    # CSS Overflow 5 and CSS Paged Media: the marker group and the page's own margins.
    ("marker-side", "block-start"), ("marker-side", "inline-start"), ("marker-side", "auto"),
    ("margin-block", "10px 20px"), ("margin-inline", "auto"),
    ("margin-block-start", "10px"),
    ("margin-trim", "none"), ("margin-trim", "block"), ("margin-trim", "inline"),
    ("margin-trim", "all"), ("margin-trim", "block start"),
    # CSS Fonts 5 §6.1: the numeric variants the reference engine carries.
    ("font-variant-numeric", "ordinal slashed-zero tabular-nums"),
    ("font-variant-numeric", "diagonal-fractions stacked-fractions"),
    ("font-variant-numeric", "proportional-figures tabular-nums oldstyle-nums"),
    ("font-variant-numeric", "normal normal"), ("font-variant-numeric", "lining-nums slashed-zero"),
    # CSS Text 4 §2.6 and CSS Text 3 §3.1: the two spaces no other round priced.
    ("line-break", "loose"), ("line-break", "anywhere"), ("line-break", "strict"),
    ("line-break", "normal"), ("line-break", "easy"),
    ("tab-size", "4"), ("tab-size", "10px"), ("tab-size", "0"), ("tab-size", "auto"),
    ("white-space-trim", "none"), ("white-space-trim", "discard newlines"),
    ("white-space-trim", "discard space leading"), ("white-space-trim", "trim trailing"),
    # CSS Text 4 §3.9: the autospace pair, and the vertical pair.
    ("text-autospace", "normal"), ("text-autospace", "no-autospace"),
    ("text-autospace", "ideograph-ascii-space"), ("text-autospace", "trim space-start"),
    ("text-spacing-trim", "trim-both"), ("text-spacing-trim", "trim-atomic-start"),
    ("text-spacing-trim", "space-all"), ("text-spacing-trim", "normal"),
]


def page():
    literal = "[" + ",".join(f'[{json.dumps(p)},{json.dumps(v)}]' for p, v in CASES) + "]"
    read = """  if (cases[i][1] !== '~') el.style.setProperty(cases[i][0], cases[i][1]);
  rows.push(i + '|' + cases[i][0] + '|' + cases[i][1]
      + '|sup=' + (cases[i][1] === '~' ? '-' : (CSS.supports(cases[i][0], cases[i][1]) ? 1 : 0))
      + '|echo=' + el.style.getPropertyValue(cases[i][0])
      + '|comp=' + getComputedStyle(el).getPropertyValue(cases[i][0]));"""
    return """<!doctype html>
<html><head><meta charset="utf-8"><title>b312 sweep</title></head>
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
with open(os.path.join(HERE, "_probe_b312_sweep.html"), "w", encoding="utf-8") as handle:
    handle.write(page())
print("cases:", len(CASES))
