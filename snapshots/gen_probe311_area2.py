"""Generate the b311 area probe round two: the pairs the first two area tables left unanswered —
the draft's self-x/self-block vocabulary, the two ambiguous keywords beside an axis word, and what
the computed surface calls a logical pair whose halves fold into one short keyword.
"""
import json
import os

CASES = [
    # The draft's axis-relative self-* words: does the reference carry them yet?
    ("position-area", "self-x-start"), ("position-area", "self-y-end"),
    ("position-area", "self-block-start"), ("position-area", "self-inline-end"),
    ("position-area", "span-self-x-start"), ("position-area", "span-self-block-end"),
    ("position-area", "span-self-inline-start"), ("position-area", "self-block-start center"),
    ("position-area", "self-x-start top"), ("position-area", "left self-y-end"),
    # 'span-all' is in every production; 'center' folds when it says the same track twice.
    ("position-area", "top span-all"), ("position-area", "span-all top"),
    ("position-area", "span-all center"), ("position-area", "center span-all"),
    ("position-area", "span-all span-all"), ("position-area", "span-all start"),
    ("position-area", "start span-all"), ("position-area", "span-all block-start"),
    ("position-area", "span-all inline-end"), ("position-area", "block-start span-all"),
    ("position-area", "left span-all"), ("position-area", "span-all left"),
    # A logical pair whose halves say one track, and the span- logical pair.
    ("position-area", "block-start inline-start"), ("position-area", "block-end inline-end"),
    ("position-area", "span-block-start span-inline-end"),
    ("position-area", "span-block-start center"), ("position-area", "block-start span-inline-start"),
    ("position-area", "span-inline-start center"), ("position-area", "inline-start end"),
    ("position-area", "start self-start"), ("position-area", "self-start self-end"),
    ("position-area", "end start"), ("position-area", "self-end center"),
    ("position-area", "center center"), ("position-area", "span-top span-all"),
]


def page():
    literal = "[" + ",".join(f'[{json.dumps(p)},{json.dumps(v)}]' for p, v in CASES) + "]"
    read = """  if (cases[i][1] !== '~') el.style.setProperty(cases[i][0], cases[i][1]);
  rows.push(i + '|' + cases[i][0] + '|' + cases[i][1]
      + '|sup=' + (cases[i][1] === '~' ? '-' : (CSS.supports(cases[i][0], cases[i][1]) ? 1 : 0))
      + '|echo=' + el.style.getPropertyValue(cases[i][0])
      + '|comp=' + getComputedStyle(el).getPropertyValue(cases[i][0]));"""
    return """<!doctype html>
<html><head><meta charset="utf-8"><title>b311 area 2</title></head>
<body>
<div id="host"></div>
<script>
// area2
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
with open(os.path.join(HERE, "_probe_b311_area2.html"), "w", encoding="utf-8") as handle:
    handle.write(page())
print("cases:", len(CASES))
