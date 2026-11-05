"""Generate the b316 edges probe: the boundary of each family the sweep found open.

The sweep said which families differ; this table asks where each one's boundary actually is — how
many parts a border side may say, which two-word pairs fold, what the legacy column-break spellings
map to and what they leave in the style attribute, and which axis word a timeline shorthand is
allowed to drop. One fresh element per row, four columns.
"""
import json
import os

CASES = [
    # CSS Borders 3 §4.1: one width, one style, one colour — and what a second of either does.
    ("border", "1px 2px"), ("border", "solid solid"), ("border", "red blue"),
    ("border", "1px solid"), ("border", "solid red"), ("border", "red 1px"),
    ("border", "hidden hidden red"), ("border", "1px solid red medium"),
    ("border", "solid currentcolor"), ("border", "0 solid"),
    ("border-top", "1px 2px solid"), ("border-top", "red blue solid"),
    ("border-top", "solid 1px red"), ("border-top", "solid 1px red 2px"),
    ("border-left", "1px solid red"), ("border-left", "red"),
    ("border-color", "red red"), ("border-color", "red blue green"),
    ("border-color", "red red red red"), ("border-color", "currentcolor red"),
    ("border-style", "none none"), ("border-style", "solid none"),
    ("border-width", "1px 2px 1px 2px"), ("border-width", "1px 2px 1px"),
    ("border-block", "1px solid red"), ("border-block", "2px 4px"),
    ("border-inline-start", "3px dashed blue"),
    # CSS Borders 3 §5.3: the corner's two radii and their fold.
    ("border-top-left-radius", "0 0"), ("border-top-left-radius", "10px 10px"),
    ("border-top-left-radius", "50% 50%"), ("border-top-left-radius", "10px 50%"),
    ("border-top-left-radius", "0"), ("border-top-left-radius", "10px / 20px"),
    ("border-bottom-right-radius", "1em 1em"), ("border-bottom-right-radius", "50% 0"),
    ("border-radius", "0 0 0 0"), ("border-radius", "10px 10px / 20px 20px"),
    # CSS Scrollbars 1 §4.2: the two words, in either order, and their refusals.
    ("scrollbar-gutter", "stable stable"), ("scrollbar-gutter", "both-edges both-edges"),
    ("scrollbar-gutter", "auto stable"), ("scrollbar-gutter", "STABLE BOTH-EDGES"),
    ("scrollbar-gutter", "stable auto"),
    # CSS Display 3 §3: the inner keyword that says nothing, and the legacy pair.
    ("display", "math"), ("display", "inline math"), ("display", "ruby text"),
    ("display", "block flow list-item"), ("display", "flow list-item"),
    ("display", "table row-group"), ("display", "inline table"),
    ("display", "block ruby"), ("display", "compact"), ("display", "inline-flex"),
    # CSS Fonts 4 §4.6: the list, the quoting, and the empty name.
    ("font-family", "serif, Arial"), ("font-family", "Times New Roman"),
    ("font-family", '"Times New Roman"'), ("font-family", "'A' \"B\""),
    ("font-family", "-apple-system"), ("font-family", "Arial, Arial"),
    ("font-family", "sans-serif 12pt"), ("font-family", "inherit"),
    ("font-family", "initial"), ("font-family", "serif serif"),
    # CSS Breaking 3 §4 and the legacy column spellings: what each word maps to, and what the
    # style attribute keeps. The legacy names echo nothing at all (measured in the sweep).
    ("-webkit-column-break-before", "auto"), ("-webkit-column-break-before", "avoid"),
    ("-webkit-column-break-before", "always"), ("-webkit-column-break-before", "left"),
    ("-webkit-column-break-before", "right"), ("-webkit-column-break-before", "avoid-column"),
    ("-webkit-column-break-before", "inherit"), ("-webkit-column-break-before", "page"),
    ("-webkit-column-break-after", "auto"), ("-webkit-column-break-after", "avoid"),
    ("-webkit-column-break-after", "always"), ("-webkit-column-break-after", "left"),
    ("-webkit-column-break-inside", "auto"), ("-webkit-column-break-inside", "avoid"),
    ("-webkit-column-break-inside", "always"), ("-webkit-column-break-inside", "avoid-page"),
    ("break-before", "always"), ("break-before", "avoid"), ("break-before", "recto"),
    ("break-after", "always"), ("break-inside", "avoid-column"),
    # CSS Scroll-driven Animations 1 §3.1/§3.2: the two shorthands and the axis each may drop.
    ("scroll-timeline", "--t y"), ("scroll-timeline", "--t x"),
    ("scroll-timeline", "none block"), ("scroll-timeline", "--t auto"),
    ("scroll-timeline", "--t inline"), ("scroll-timeline", "--t block"),
    ("scroll-timeline", "--t --u"), ("scroll-timeline", "auto"),
    ("view-timeline", "--t"), ("view-timeline", "--t block"),
    ("view-timeline", "--t inline"), ("view-timeline", "--t auto"),
    ("view-timeline", "--t inline 10px"), ("view-timeline", "--t nearest"),
    ("view-timeline", "none"), ("view-timeline", "~"), ("scroll-timeline", "~"),
    # CSS Easing 1 §3: the two one-step spellings and what the computed surface says of them.
    ("animation-timing-function", "step-start"), ("animation-timing-function", "step-end"),
    ("animation-timing-function", "steps(1)"), ("animation-timing-function", "steps(1, start)"),
    ("animation-timing-function", "ease"), ("animation-timing-function", "linear"),
    ("animation-timing-function", "cubic-bezier(0.42, 0, 0.58, 1)"),
    ("transition-timing-function", "step-start"),
    ("transition-timing-function", "steps(2, end)"),
    # The names the sweep asked once: what does the computed surface say when nothing is written?
    ("font-family", "~"), ("display", "~"), ("border-top-left-radius", "~"),
    ("-webkit-column-break-before", "~"), ("scrollbar-gutter", "~"),
    ("animation-timing-function", "~"), ("align-self", "~"), ("text-security", "~"),
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
<html><head><meta charset="utf-8"><title>b316 edges</title></head>
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
with open(os.path.join(HERE, "_probe_b316_edges.html"), "w", encoding="utf-8") as handle:
    handle.write(page())
print("cases:", len(CASES))
