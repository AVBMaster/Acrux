"""Generate the b315 edges probe: where the boundary of each family the sweep found open actually is.

The sweep said which families differ; this table asks the boundary of each, because a value model is
only finished when the collapsed spellings, the implied keywords and the refusals agree too. The
host is a fixed 300x100 box so the rows that price a size answer the same number in both engines —
an auto-width host would make every 'stretch' and every percentage a viewport artefact.
"""
import json
import os

CASES = [
    # CSS Containment 3 §3.4 and Conditional 3 §3.3: what a container name may be spelled like.
    ("container-name", "-cards"), ("container-name", "--cards"), ("container-name", "cards"),
    ("container-name", "1cards"), ("container-name", "none"), ("container-name", "NONE"),
    ("container-name", "auto"), ("container-name", "--c --d"), ("container-name", "--c, --d"),
    ("container-name", "[ --c ]"), ("container-name", "--c /"), ("container-name", "c d e"),
    ("container-type", "scroll-state"), ("container-type", "SCROLL-STATE"),
    ("container-type", "Scroll-State"), ("container-type", "--scroll-state"),
    ("container-type", "scroll-state()"), ("container-type", "size scroll-state"),
    ("container-type", "normal"), ("container-type", "none"),
    ("container", "--c"), ("container", "--c size"), ("container", "size --c"),
    ("container", "size/--c"), ("container", "c"), ("container", "c / size"),
    ("container", "c/size"), ("container", "c / normal"), ("container", "none / normal"),
    ("container", "--c / scroll-state"), ("container", "scroll-state"),
    ("container", "scroll-state --c"), ("container", "/ size"), ("container", "c /"),
    ("container", "c / bogus"), ("container", "c / size / inline-size"),
    ("container", "1cards"), ("container", "NONE"), ("container", "none none"),
    # CSS Values 4 §1.2: the interpolation control's two words.
    ("interpolate-size", "numeric-only"), ("interpolate-size", "allow-keywords"),
    ("interpolate-size", "allows-keywords"), ("interpolate-size", "NUMERIC-ONLY"),
    ("interpolate-size", "numeric-only allow-keywords"),
    # CSS Shapes 1 §4.5: a number, and the math function that says one.
    ("shape-image-threshold", "calc(0.2 + 0.3)"), ("shape-image-threshold", "calc(1)"),
    ("shape-image-threshold", "calc(50%)"), ("shape-image-threshold", "calc(1 + 1)"),
    ("shape-image-threshold", "min(0.5, 1)"), ("shape-image-threshold", "1.5"),
    ("shape-image-threshold", "+0.5"), ("shape-image-threshold", ".5"),
    ("shape-image-threshold", "0.5 0.5"), ("shape-image-threshold", "calc(0.2+0.3)"),
    # The two-value pairs that fold into one word when both sides say the same.
    ("inset-block", "10px 10px"), ("inset-block", "auto auto"), ("inset-block", "0 0"),
    ("inset-block", "10px 20px"), ("margin-block", "10px 10px"),
    ("margin-inline", "auto auto"), ("margin-inline", "10px 10px"),
    ("padding-block", "10px 10px"), ("padding-inline", "10px 10px"),
    ("padding-inline", "0 0"), ("scroll-margin-block", "10px 10px"),
    ("scroll-margin-inline", "auto auto"), ("scroll-padding-block", "auto auto"),
    ("scroll-padding-inline", "10px 10px"),
    ("background-repeat", "repeat repeat"), ("background-repeat", "no-repeat no-repeat"),
    ("background-repeat", "space space"), ("background-repeat", "repeat-x repeat-x"),
    ("background-repeat", "repeat space"), ("background-size", "auto auto"),
    ("background-size", "10px 10px"), ("background-size", "cover cover"),
    ("background-size", "contain contain"), ("background-size", "auto 10px"),
    ("mask-repeat", "repeat repeat"), ("mask-size", "auto auto"),
    # CSS Padding/Margin 3 §4: 'auto' belongs to the margin, not to the padding.
    ("padding-inline", "auto"), ("padding-block", "auto"), ("padding-inline", "auto 10px"),
    ("padding-inline-start", "auto"), ("padding-left", "auto"),
    ("padding-block", "10px auto"), ("margin-inline", "auto"),
    # CSS Multi-column 1 §7.1: the count is a positive integer.
    ("columns", "0"), ("columns", "1"), ("columns", "-1"), ("columns", "1.5"),
    ("columns", "2.0"), ("columns", "calc(1 + 1)"), ("columns", "auto auto"),
    ("columns", "200px auto"), ("columns", "10px 20px"), ("column-count", "-1"),
    ("column-count", "0"), ("column-count", "1.5"),
    # CSS Overscroll 1 §3.1: one axis says one word.
    ("overscroll-behavior-inline", "contain none"), ("overscroll-behavior-inline", "auto auto"),
    ("overscroll-behavior-block", "contain contain"),
    ("overscroll-behavior-inline", "pan-x"),
    # CSS Sizing 3 §5.1: the intrinsic keyword, priced in a 300x100 host.
    ("inline-size", "stretch"), ("block-size", "stretch"), ("width", "stretch"),
    ("height", "stretch"), ("min-inline-size", "stretch"), ("max-inline-size", "stretch"),
    ("inline-size", "contain"), ("inline-size", "cover"), ("inline-size", "calc-size(auto, size)"),
    ("margin-inline", "5%"), ("margin-block", "10%"), ("padding-inline", "5%"),
    ("inset-inline", "5% 10%"),
    # CSS Containment 3 §4.4: one 'auto' flag for the pair, said once.
    ("contain-intrinsic-size", "auto 100px 200px"), ("contain-intrinsic-size", "auto auto"),
    ("contain-intrinsic-size", "none 100px"), ("contain-intrinsic-size", "auto 100px auto"),
    ("contain-intrinsic-size", "100px"), ("contain-intrinsic-size", "auto none"),
    ("container", "~"), ("container-name", "~"), ("interpolate-size", "~"),
    ("shape-image-threshold", "~"), ("inset-block", "~"), ("padding-inline", "~"),
    ("columns", "~"), ("overscroll-behavior-inline", "~"), ("contain-intrinsic-size", "~"),
    ("background-repeat", "~"), ("background-size", "~"),
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
<html><head><meta charset="utf-8"><title>b315 edges</title></head>
<body>
<div id="anchor"></div>
<script>
// edges
function build() {{
// A sized host: 'stretch' and a percentage are priced against this box, not against a viewport
// the two engines do not share.
const anchor = document.getElementById('anchor');
const host = document.createElement('div');
host.style.position = 'absolute';
host.style.left = '0px';
host.style.top = '0px';
host.style.width = '300px';
host.style.height = '100px';
anchor.appendChild(host);
const rows = [];
const cases = {cases};
for (let i = 0; i < cases.length; i++) {{
const el = document.createElement('div');
host.appendChild(el);
{read}
    el.remove();
}}
rows.push('host|w=' + Math.round(host.getBoundingClientRect().width)
    + '|h=' + Math.round(host.getBoundingClientRect().height));
document.body.insertAdjacentHTML('beforeend',
    '<pre id="record" style="position:absolute;left:10px;top:130px;font:10px/1.35 monospace;white-space:pre-wrap"></pre>');
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
with open(os.path.join(HERE, "_probe_b315_edges.html"), "w", encoding="utf-8") as handle:
    handle.write(page())
print("cases:", len(CASES))
