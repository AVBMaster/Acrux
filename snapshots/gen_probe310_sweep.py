"""Generate the b310 sweep probe: the property families no round has swept yet.

One fresh element per row (the rule b291 measured), three columns: what the grammar answers, what
the style attribute echoes and what the computed surface answers.
"""
import json
import os

CASES = [
    # CSS UI 4 §10: the pointer device's shape, and a URL with its hotspot.
    ("cursor", "auto"), ("cursor", "default"), ("cursor", "none"), ("cursor", "pointer"),
    ("cursor", "help"), ("cursor", "wait"), ("cursor", "progress"), ("cursor", "cell"),
    ("cursor", "crosshair"), ("cursor", "text"), ("cursor", "vertical-text"),
    ("cursor", "alias"), ("cursor", "copy"), ("cursor", "move"), ("cursor", "no-drop"),
    ("cursor", "not-allowed"), ("cursor", "grab"), ("cursor", "grabbing"),
    ("cursor", "all-scroll"), ("cursor", "col-resize"), ("cursor", "row-resize"),
    ("cursor", "n-resize"), ("cursor", "e-resize"), ("cursor", "s-resize"),
    ("cursor", "w-resize"), ("cursor", "ne-resize"), ("cursor", "nw-resize"),
    ("cursor", "se-resize"), ("cursor", "sw-resize"), ("cursor", "ew-resize"),
    ("cursor", "ns-resize"), ("cursor", "nesw-resize"), ("cursor", "nwse-resize"),
    ("cursor", "zoom-in"), ("cursor", "zoom-out"), ("cursor", "hand"), ("cursor", "up-down"),
    ("cursor", "9999"), ("cursor", "0"),
    ("cursor", "url(a.png)"), ("cursor", "url(a.png) 2"), ("cursor", "url(a.png) 2 3"),
    ("cursor", "url(a.png) 2 3, pointer"), ("cursor", "url(a.png), pointer"),
    ("cursor", "url(a.png) 2 3 4"), ("cursor", "url(a.png) x y"), ("cursor", "pointer, pointer"),
    ("cursor", "auto pointer"), ("cursor", "-webkit-grab"), ("cursor", "POINTER"),
    ("cursor", "-webkit-grabbing"), ("cursor", "-webkit-zoom-in"), ("cursor", "-webkit-alias"),
    ("cursor", "-webkit-resize"), ("cursor", "-webkit-auto"),
    ("cursor", "url(a.png) -1 -1, pointer"),
    ("cursor", "url(a.png) 2 3, url(b.png) 1, pointer"),
    ("cursor", "url(a.png) 2 3, auto"), ("cursor", 'url("a.png") 2 3, pointer'),
    ("cursor", "-webkit-grab grabbing"), ("cursor", "-webkit-zoom-out"),
    ("cursor", "-webkit-ew-resize"), ("cursor", "-webkit-text"), ("cursor", "-webkit-help"),
    ("cursor", "-webkit-grab, -webkit-grabbing"), ("cursor", "-webkit-none"),
    # CSS GCP 3 / CSS Text 4: the quotation pair and the named string.
    ("quotes", "none"), ("quotes", "auto"), ("quotes", "'“' '”'"),
    ("quotes", "'“' '”' '‘' '’'"), ("quotes", "'“' '”' '‘'"),
    ("quotes", "'a' 'b' 'c' 'd' 'e' 'f'"), ("quotes", "“ ”"), ("quotes", 'none auto'),
    ("string-set", "none"), ("string-set", "foo 'bar'"), ("string-set", "foo content()"),
    ("string-set", "foo 'bar', baz 'qux'"), ("string-set", "foo"),
    # CSS Counters 2 §3.1: the 'span' keyword that says how many steps one element takes.
    ("counter-reset", "foo"), ("counter-reset", "foo 1"), ("counter-reset", "foo span 2"),
    ("counter-reset", "foo span"), ("counter-reset", "foo span 2 bar 3"),
    ("counter-reset", "foo span -2"), ("counter-reset", "foo span 1.5"),
    ("counter-reset", "foo span 0"), ("counter-reset", "none"), ("counter-reset", "auto"),
    ("counter-reset", "foo none"), ("counter-set", "foo 2"), ("counter-set", "foo span 2"),
    ("counter-set", "foo"), ("counter-increment", "foo 2"), ("counter-increment", "foo span 2"),
    ("counter-increment", "foo"), ("counter-increment", "none"),
    # CSS Text 4 §3: the white-space shorthand made of the longhands it now folds.
    ("white-space", "normal"), ("white-space", "pre"), ("white-space", "pre-wrap"),
    ("white-space", "pre-line"), ("white-space", "break-spaces"), ("white-space", "nowrap"),
    ("white-space", "collapse"), ("white-space", "preserve"), ("white-space", "preserve-wrap"),
    ("white-space", "preserve-collapse"), ("white-space", "wrap"), ("white-space", "balance"),
    ("white-space", "stable"), ("white-space", "preserve stable"),
    ("white-space", "collapse wrap"), ("white-space", "preserve nowrap"),
    ("white-space", "preserve-breaks"), ("white-space", "discard"),
    ("white-space", "pre-wrap break-spaces"), ("white-space", "anywhere"),
    ("white-space", "collapse nowrap"), ("white-space", "nowrap collapse"),
    ("white-space", "nowrap preserve"), ("white-space", "preserve wrap"),
    ("white-space", "preserve-breaks nowrap"), ("white-space", "discard wrap"),
    ("white-space", "discard nowrap"), ("white-space", "collapse collapse"),
    ("white-space", "wrap wrap"), ("white-space", "preserve preserve-breaks"),
    ("white-space", "normal nowrap"), ("white-space", "pre wrap"),
    ("white-space", "PRESERVE NOWRAP"), ("white-space", "preserve nowrap extra"),
    ("white-space", "nowrap preserve-breaks"), ("white-space", "preserve-breaks wrap"),
    ("white-space", "WRAP"), ("white-space", "COLLAPSE"),
    ("white-space", "break-spaces nowrap"), ("white-space", "collapse preserve"),
    ("white-space", "inherit"), ("white-space", "initial"), ("white-space", "unset"),
    ("white-space", "revert"), ("white-space", "revert-layer"),
    ("text-wrap", "pretty"), ("text-wrap", "balance 2"), ("text-wrap", "wrap pretty"),
    ("white-space-collapse", "collapse"), ("white-space-collapse", "preserve"),
    ("white-space-collapse", "preserve-breaks"), ("white-space-collapse", "discard"),
    ("white-space-collapse", "preserve-spaces"),
    # CSS Masking 1: the mask's own kind, mode and composition.
    ("mask-type", "alpha"), ("mask-type", "luminance"), ("mask-type", "alpha invert"),
    ("mask-type", "invert"), ("mask-mode", "alpha"), ("mask-mode", "luminance"),
    ("mask-mode", "match-source"), ("mask-composite", "add"), ("mask-composite", "subtract"),
    ("mask-composite", "intersect"), ("mask-composite", "exclude"),
    ("mask-composite", "add subtract"), ("mask-composite", "add, subtract"),
    ("mask-source-type", "alpha"), ("box-orient", "inline-axis"),
    # Filter Effects 1 on the backdrop.
    ("backdrop-filter", "none"), ("backdrop-filter", "blur(2px)"),
    ("backdrop-filter", "blur(2px) brightness(2)"), ("backdrop-filter", "brightness(0)"),
    ("backdrop-filter", "url(#f)"), ("backdrop-filter", "sepia(0.5) hue-rotate(90deg)"),
    ("backdrop-filter", "blur()"), ("backdrop-filter", "blur(2)"),
    ("backdrop-filter", "-webkit-blur(2px)"), ("backdrop-filter", "grayscale(20%)"),
    # CSS Text 3 §5.5 and Text 4: the transform set, including the CJK spellings.
    ("text-transform", "none"), ("text-transform", "capitalize"), ("text-transform", "uppercase"),
    ("text-transform", "lowercase"), ("text-transform", "full-width"),
    ("text-transform", "full-size-kana"), ("text-transform", "Uppercase"),
    ("text-transform", "uppercase lowercase"), ("text-transform", "math-auto"),
    ("text-transform", "revert"), ("text-transform", "math"),
    ("text-transform", "math-auto uppercase"), ("text-transform", "MATH-AUTO"),
    # The digits form of the vertical-combine property and the ruby trio.
    ("text-combine-upright", "none"), ("text-combine-upright", "all"),
    ("text-combine-upright", "digits 2"), ("text-combine-upright", "digits 0"),
    ("text-combine-upright", "digits 3"), ("text-combine-upright", "all digits 2"),
    ("ruby-position", "over under"), ("ruby-position", "under over"),
    ("ruby-position", "alternate"), ("ruby-position", "over over"),
    ("ruby-align", "space-between"), ("ruby-merge", "distribute"),
    # The scroll and timeline axes no round has swept together.
    ("scroll-timeline-axis", "block"), ("scroll-timeline-axis", "inline"),
    ("scroll-timeline-axis", "x"), ("scroll-timeline-axis", "block inline"),
    ("scroll-timeline-axis", "auto"), ("view-timeline-axis", "y"),
    ("view-timeline-axis", "nearest"), ("view-timeline-axis", "self"),
    ("view-timeline-inset", "auto"), ("view-timeline-inset", "10px"),
    ("view-timeline-inset", "auto 10px"), ("timeline-scope", "none"),
    ("timeline-scope", "--a --b"), ("scroll-timeline-name", "none"),
    ("scroll-timeline-name", "--a"),
]


def page():
    literal = "[" + ",".join(f'[{json.dumps(p)},{json.dumps(v)}]' for p, v in CASES) + "]"
    read = """  if (cases[i][1] !== '~') el.style.setProperty(cases[i][0], cases[i][1]);
  rows.push(i + '|' + cases[i][0] + '|' + cases[i][1]
      + '|sup=' + (cases[i][1] === '~' ? '-' : (CSS.supports(cases[i][0], cases[i][1]) ? 1 : 0))
      + '|echo=' + el.style.getPropertyValue(cases[i][0])
      + '|comp=' + getComputedStyle(el).getPropertyValue(cases[i][0]));"""
    return """<!doctype html>
<html><head><meta charset="utf-8"><title>b310 sweep</title></head>
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
with open(os.path.join(HERE, "_probe_b310_sweep.html"), "w", encoding="utf-8") as handle:
    handle.write(page())
print("cases:", len(CASES))
