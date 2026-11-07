"""Generate the b329 P2 sweep: the list-style longhands, the text-decoration
longhands, the text-indent/transform pair, vertical-align, visibility, the
float/clear pair, and position — value by value.

Each gets its keyword set, a length/percentage variant, a two-value form, and a
bogus word. The list-style and text-decoration longhands get the shapes their
shorthands only met once. One fresh element per row (measured in b283).
"""
import json
import os

CASES = [
    # list-style-type: the disc/decimal family, a @counter-style name, none.
    ("list-style-type", "disc"), ("list-style-type", "circle"), ("list-style-type", "square"),
    ("list-style-type", "decimal"), ("list-style-type", "decimal-leading-zero"),
    ("list-style-type", "lower-roman"), ("list-style-type", "upper-roman"),
    ("list-style-type", "lower-alpha"), ("list-style-type", "upper-alpha"),
    ("list-style-type", "lower-latin"), ("list-style-type", "upper-latin"),
    ("list-style-type", "lower-greek"), ("list-style-type", "armenian"),
    ("list-style-type", "georgian"), ("list-style-type", "hebrew"), ("list-style-type", "hiragana"),
    ("list-style-type", "katakana"), ("list-style-type", "none"), ("list-style-type", "bogus"),
    ("list-style-type", "disc circle"),
    # list-style-position.
    ("list-style-position", "outside"), ("list-style-position", "inside"),
    ("list-style-position", "bogus"),
    # list-style-image.
    ("list-style-image", "none"), ("list-style-image", "url(a.png)"),
    ("list-style-image", "linear-gradient(red, blue)"), ("list-style-image", "red"),
    ("list-style-image", "bogus"),
    # list-style shorthand.
    ("list-style", "none"), ("list-style", "disc"), ("list-style", "inside"),
    ("list-style", "square inside url(a.png)"), ("list-style", "decimal outside"),
    ("list-style", "bogus"), ("list-style", "none none"),
    # text-decoration longhands.
    ("text-decoration-line", "none"), ("text-decoration-line", "underline"),
    ("text-decoration-line", "overline"), ("text-decoration-line", "line-through"),
    ("text-decoration-line", "underline overline"), ("text-decoration-line", "blink"),
    ("text-decoration-line", "underline line-through"), ("text-decoration-line", "bogus"),
    ("text-decoration-style", "solid"), ("text-decoration-style", "double"),
    ("text-decoration-style", "dotted"), ("text-decoration-style", "dashed"),
    ("text-decoration-style", "wavy"), ("text-decoration-style", "bogus"),
    ("text-decoration-color", "red"), ("text-decoration-color", "currentcolor"),
    ("text-decoration-color", "transparent"), ("text-decoration-color", "red blue"),
    ("text-decoration-color", "bogus"),
    ("text-decoration-thickness", "auto"), ("text-decoration-thickness", "from-font"),
    ("text-decoration-thickness", "2px"), ("text-decoration-thickness", "10%"),
    ("text-decoration-thickness", "-1px"), ("text-decoration-thickness", "bogus"),
    ("text-decoration-skip-ink", "none"), ("text-decoration-skip-ink", "auto"),
    ("text-decoration-skip-ink", "all"), ("text-decoration-skip-ink", "bogus"),
    ("text-underline-offset", "auto"), ("text-underline-offset", "2px"),
    ("text-underline-offset", "0.2em"), ("text-underline-offset", "-2px"),
    ("text-underline-offset", "bogus"),
    ("text-decoration", "underline"), ("text-decoration", "underline dotted red"),
    ("text-decoration", "underline wavy 2px"), ("text-decoration", "none"),
    ("text-decoration", "underline solid red 3px"), ("text-decoration", "bogus"),
    # text-indent: a length, a percentage, negative, hanging/each-line.
    ("text-indent", "0"), ("text-indent", "20px"), ("text-indent", "10%"),
    ("text-indent", "-20px"), ("text-indent", "20px hanging"), ("text-indent", "20px each-line"),
    ("text-indent", "hanging each-line"), ("text-indent", "bogus"),
    # text-transform.
    ("text-transform", "none"), ("text-transform", "capitalize"), ("text-transform", "uppercase"),
    ("text-transform", "lowercase"), ("text-transform", "full-width"),
    ("text-transform", "full-size-kana"), ("text-transform", "bogus"),
    ("text-transform", "uppercase lowercase"),
    # vertical-align: the keywords and the length/percentage forms.
    ("vertical-align", "baseline"), ("vertical-align", "sub"), ("vertical-align", "super"),
    ("vertical-align", "top"), ("vertical-align", "text-top"), ("vertical-align", "middle"),
    ("vertical-align", "bottom"), ("vertical-align", "text-bottom"),
    ("vertical-align", "0"), ("vertical-align", "5px"), ("vertical-align", "50%"),
    ("vertical-align", "-5px"), ("vertical-align", "bogus"), ("vertical-align", "top bottom"),
    # visibility.
    ("visibility", "visible"), ("visibility", "hidden"), ("visibility", "collapse"),
    ("visibility", "bogus"),
    # float / clear.
    ("float", "none"), ("float", "left"), ("float", "right"), ("float", "inline-start"),
    ("float", "inline-end"), ("float", "bogus"), ("float", "left right"),
    ("clear", "none"), ("clear", "left"), ("clear", "right"), ("clear", "both"),
    ("clear", "inline-start"), ("clear", "inline-end"), ("clear", "bogus"), ("clear", "left right"),
    # position.
    ("position", "static"), ("position", "relative"), ("position", "absolute"),
    ("position", "fixed"), ("position", "sticky"), ("position", "bogus"), ("position", "relative absolute"),
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
<html><head><meta charset="utf-8"><title>b329 P2 sweep</title></head>
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
with open(os.path.join(HERE, "_probe_b329_list_deco_pos.html"), "w", encoding="utf-8") as handle:
    handle.write(page())
print("cases:", len(CASES))