"""Generate the b329 P1 sweep: the flex longhands, the multicolumn longhands,
and the text-shaping family — value by value.

Each flex longhand gets the numbers, the keywords and the shapes the shorthand
only met through 'flex'. The multicolumn longhands get their keyword sets, the
integer and length forms, the rule sub-parts and the span/fill words. The text
shaping properties get every keyword their specs name plus the pair and list
forms. One fresh element per row (measured in b283: a reused host answers a
dropped declaration with the previous row's value).
"""
import json
import os

CASES = [
    # flex-grow / flex-shrink: numbers, zero, negative, a keyword, a length.
    ("flex-grow", "0"), ("flex-grow", "1"), ("flex-grow", "2.5"), ("flex-grow", "-1"),
    ("flex-grow", "auto"), ("flex-grow", "10px"), ("flex-grow", "bogus"),
    ("flex-shrink", "0"), ("flex-shrink", "1"), ("flex-shrink", "0.5"), ("flex-shrink", "-1"),
    ("flex-shrink", "auto"), ("flex-shrink", "bogus"),
    # flex-basis: lengths, percentages, keywords, content, auto.
    ("flex-basis", "auto"), ("flex-basis", "content"), ("flex-basis", "0"),
    ("flex-basis", "0px"), ("flex-basis", "100px"), ("flex-basis", "50%"),
    ("flex-basis", "max-content"), ("flex-basis", "min-content"),
    ("flex-basis", "fit-content"), ("flex-basis", "stretch"),
    ("flex-basis", "calc(100px + 2em)"), ("flex-basis", "-10px"), ("flex-basis", "bogus"),
    ("flex-basis", "auto auto"),
    # order: an integer, zero, negative, a non-integer, a keyword.
    ("order", "0"), ("order", "1"), ("order", "-1"), ("order", "999"), ("order", "1.5"),
    ("order", "auto"), ("order", "bogus"),
    # column-count: an integer, one, zero, negative, a length, auto, a float.
    ("column-count", "1"), ("column-count", "2"), ("column-count", "0"),
    ("column-count", "-1"), ("column-count", "2.5"), ("column-count", "auto"),
    ("column-count", "10px"), ("column-count", "bogus"),
    # column-width: a length, auto, a percentage, a keyword, negative.
    ("column-width", "auto"), ("column-width", "0"), ("column-width", "100px"),
    ("column-width", "10em"), ("column-width", "50%"), ("column-width", "-10px"),
    ("column-width", "bogus"),
    # column-gap: a length, normal, a percentage, negative.
    ("column-gap", "normal"), ("column-gap", "0"), ("column-gap", "20px"),
    ("column-gap", "10%"), ("column-gap", "-10px"), ("column-gap", "bogus"),
    # column-rule-*: the width/style/color parts, one per side.
    ("column-rule-width", "thin"), ("column-rule-width", "medium"), ("column-rule-width", "thick"),
    ("column-rule-width", "1px"), ("column-rule-width", "0"), ("column-rule-width", "auto"),
    ("column-rule-width", "bogus"),
    ("column-rule-style", "none"), ("column-rule-style", "hidden"), ("column-rule-style", "dotted"),
    ("column-rule-style", "dashed"), ("column-rule-style", "solid"), ("column-rule-style", "double"),
    ("column-rule-style", "groove"), ("column-rule-style", "ridge"),
    ("column-rule-style", "inset"), ("column-rule-style", "outset"), ("column-rule-style", "bogus"),
    ("column-rule-color", "red"), ("column-rule-color", "currentcolor"),
    ("column-rule-color", "transparent"), ("column-rule-color", "red blue"), ("column-rule-color", "bogus"),
    ("column-rule", "1px solid red"), ("column-rule", "solid"), ("column-rule", "thin"), ("column-rule", "bogus"),
    # column-span / column-fill.
    ("column-span", "none"), ("column-span", "all"), ("column-span", "auto"), ("column-span", "bogus"),
    ("column-fill", "auto"), ("column-fill", "balance"), ("column-fill", "balance-all"),
    ("column-fill", "bogus"),
    # The columns shorthand in its shapes.
    ("columns", "2"), ("columns", "100px"), ("columns", "2 100px"), ("columns", "auto"),
    ("columns", "auto 100px"), ("columns", "100px 2"), ("columns", "bogus"),
    # text-align: the keyword set, the pair forms, the justify variants.
    ("text-align", "start"), ("text-align", "end"), ("text-align", "left"), ("text-align", "right"),
    ("text-align", "center"), ("text-align", "justify"), ("text-align", "justify-all"),
    ("text-align", "match-parent"), ("text-align", "bogus"), ("text-align", "left right"),
    # text-align-last: the same keyword set.
    ("text-align-last", "auto"), ("text-align-last", "start"), ("text-align-last", "end"),
    ("text-align-last", "left"), ("text-align-last", "right"), ("text-align-last", "center"),
    ("text-align-last", "justify"), ("text-align-last", "match-parent"), ("text-align-last", "bogus"),
    # hyphens / hyphenate-character / line-break / word-break.
    ("hyphens", "none"), ("hyphens", "manual"), ("hyphens", "auto"), ("hyphens", "bogus"),
    ("line-break", "auto"), ("line-break", "loose"), ("line-break", "normal"), ("line-break", "strict"),
    ("line-break", "anywhere"), ("line-break", "bogus"),
    ("word-break", "normal"), ("word-break", "break-all"), ("word-break", "keep-all"),
    ("word-break", "break-word"), ("word-break", "auto-phrase"), ("word-break", "bogus"),
    # word-spacing / letter-spacing: lengths, normal, percentages, negatives.
    ("word-spacing", "normal"), ("word-spacing", "0"), ("word-spacing", "5px"),
    ("word-spacing", "2em"), ("word-spacing", "-2px"), ("word-spacing", "50%"),
    ("word-spacing", "calc(1px + 2px)"), ("word-spacing", "bogus"),
    ("letter-spacing", "normal"), ("letter-spacing", "0"), ("letter-spacing", "2px"),
    ("letter-spacing", "0.1em"), ("letter-spacing", "-1px"), ("letter-spacing", "50%"),
    ("letter-spacing", "bogus"),
    # white-space: the two-axis keyword set (the b310 shorthand) and its values.
    ("white-space", "normal"), ("white-space", "nowrap"), ("white-space", "pre"),
    ("white-space", "pre-wrap"), ("white-space", "pre-line"), ("white-space", "break-spaces"),
    ("white-space", "collapse preserve-breaks"), ("white-space", "preserve-breaks"),
    ("white-space", "wrap"), ("white-space", "bogus"), ("white-space", "normal normal"),
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
<html><head><meta charset="utf-8"><title>b329 P1 sweep</title></head>
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
with open(os.path.join(HERE, "_probe_b329_flex_cols_text.html"), "w", encoding="utf-8") as handle:
    handle.write(page())
print("cases:", len(CASES))