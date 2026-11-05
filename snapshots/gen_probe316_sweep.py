"""Generate the b316 sweep probe: the families the last five rounds left unmeasured as a family.

The names come from the engine's own property table minus everything a b311-b315 probe wrote a row
for: the border family's sides and sub-shorthands, the physical margin/padding/scroll longhands,
the shadow and outline parts, the break family with its legacy page-break spellings, the
self-alignment pair, the generic font families, and the odd names the engine declares but no probe
has ever asked. One fresh element per row, four columns: what the grammar answers, how many
longhands the CSSOM counts, what the style attribute echoes and what the computed surface answers.
"""
import json
import os

CASES = [
    # CSS Borders 3 §4.1/§4.2: the side shorthand, its order, and what one side may say alone.
    ("border", "1px"), ("border", "solid"), ("border", "red"),
    ("border", "1px solid red"), ("border", "thin"), ("border", "medium"),
    ("border", "thick"), ("border", "hidden"), ("border", "none double"),
    ("border", "1px solid red 2px"), ("border", "currentcolor"),
    ("border", "2px solid currentcolor"), ("border", "1px solid red, 2px dashed blue"),
    ("border", "dotted"), ("border", "0"),
    ("border-top", "3px dotted blue"), ("border-top", "1px"),
    ("border-top", "solid red"), ("border-top", "red solid"),
    ("border-left", "thin double green"), ("border-right", "1px hidden"),
    ("border-bottom", "2px"), ("border-bottom", "inset"), ("border-bottom", "groove"),
    ("border-color", "red blue"), ("border-color", "red blue green yellow"),
    ("border-color", "red, blue"), ("border-color", "transparent"),
    ("border-style", "solid dashed"), ("border-style", "solid solid solid solid"),
    ("border-style", "hidden"), ("border-style", "dotted double groove ridge"),
    ("border-width", "thin thick"), ("border-width", "1px 2px 3px 4px"),
    ("border-width", "0"), ("border-width", "1px 1px"),
    ("border-block-start", "2px solid red"), ("border-inline-end", "3px dashed"),
    ("border-block-start-width", "2px"), ("border-inline-end-style", "dashed"),
    ("border-block-start-color", "red"), ("border-inline-color", "red blue"),
    # CSS Borders 3 §5.3: a corner's two radii, and the pair that says one corner.
    ("border-top-left-radius", "10px"), ("border-top-left-radius", "10px 20px"),
    ("border-top-left-radius", "50%"), ("border-top-left-radius", "10px 20px 30px"),
    ("border-top-left-radius", "calc(1px + 2px)"), ("border-top-left-radius", "10px, 20px"),
    ("border-bottom-right-radius", "1em 2em"), ("border-bottom-right-radius", "0 0"),
    ("border-radius", "10px 20px 30px 40px / 50px"),
    ("border-radius", "10px 10px 10px 10px"),
    # CSS Box Model 4 and Scroll Snap 1: the physical edges, one at a time.
    ("margin-bottom", "auto"), ("margin-right", "5%"), ("margin-inline-start", "10px"),
    ("margin-inline-end", "auto"), ("margin-block-end", "1em"),
    ("padding-bottom", "auto"), ("padding-right", "0"), ("padding-inline-end", "5px"),
    ("padding-block-start", "10px"),
    ("scroll-margin-top", "10px"), ("scroll-margin-right", "auto"),
    ("scroll-margin-bottom", "1px 2px"), ("scroll-margin-inline-start", "5px"),
    ("scroll-margin-block-end", "1em"),
    ("scroll-padding-top", "auto"), ("scroll-padding-bottom", "10px"),
    ("scroll-padding-inline-end", "5%"), ("scroll-padding-block-start", "auto"),
    ("bottom", "10px"), ("bottom", "auto"), ("bottom", "50%"),
    # CSS Text Decoration 3 §3.6 and Color 4: the shadow list, in both orders.
    ("text-shadow", "none"), ("text-shadow", "1px 2px"), ("text-shadow", "1px 2px 3px"),
    ("text-shadow", "1px 2px red"), ("text-shadow", "red 1px 2px"),
    ("text-shadow", "1px 2px 3px red"), ("text-shadow", "inset 1px 2px red"),
    ("text-shadow", "1px 2px red, 3px 4px blue"), ("text-shadow", "1px"),
    ("text-shadow", "1px 2px currentcolor"), ("text-shadow", "0 0 0 #000"),
    ("outline-color", "red"), ("outline-color", "invert"), ("outline-color", "currentcolor"),
    ("outline-color", "#fff"), ("outline-style", "auto"), ("outline-style", "invert"),
    ("outline-style", "solid"), ("outline-style", "dotted double"),
    ("text-emphasis-color", "red"), ("text-emphasis-color", "currentcolor"),
    ("text-decoration-color", "red"), ("text-decoration-color", "#00ff00"),
    ("caret-color", "auto"), ("caret-color", "transparent"), ("caret-color", "red"),
    ("accent-color", "auto"), ("accent-color", "transparent"),
    # CSS Scrollbars 1 §4.2: the gutter pair and its only legal order.
    ("scrollbar-gutter", "auto"), ("scrollbar-gutter", "stable"),
    ("scrollbar-gutter", "both-edges"), ("scrollbar-gutter", "stable both-edges"),
    ("scrollbar-gutter", "both-edges stable"), ("scrollbar-gutter", "stable stable"),
    # CSS Breaking 3 and the legacy page-break spellings.
    ("break-before", "auto"), ("break-before", "page"), ("break-before", "left"),
    ("break-before", "right"), ("break-before", "column"), ("break-before", "always"),
    ("break-before", "avoid-page"), ("break-before", "recto"), ("break-before", "verso"),
    ("break-after", "auto"), ("break-after", "avoid"), ("break-after", "avoid-column"),
    ("break-inside", "auto"), ("break-inside", "avoid"), ("break-inside", "avoid-page"),
    ("break-inside", "avoid-column"), ("break-inside", "avoid avoid"),
    ("page-break-before", "auto"), ("page-break-before", "always"),
    ("page-break-before", "left"), ("page-break-before", "right"),
    ("page-break-before", "avoid"), ("page-break-before", "recto"),
    ("page-break-after", "always"), ("page-break-inside", "avoid"),
    ("widows", "2"), ("widows", "auto"), ("widows", "0"), ("widows", "-1"),
    ("orphans", "2"), ("orphans", "auto"),
    # CSS Display 3 §3: the outer keyword, the inner keyword, and the legacy spellings.
    ("display", "block"), ("display", "inline"), ("display", "run-in"),
    ("display", "flow"), ("display", "flow-root"), ("display", "table"),
    ("display", "contents"), ("display", "none"), ("display", "list-item"),
    ("display", "inline flow-root"), ("display", "block flow"),
    ("display", "ruby"), ("display", "ruby-base"), ("display", "math"),
    ("display", "inline-table"), ("display", "table-cell"), ("display", "box"),
    ("display", "block flex"), ("display", "flex inline"),
    # CSS Box Alignment 3 §5/§8: the self pair, with the baseline and safety words.
    ("align-self", "auto"), ("align-self", "normal"), ("align-self", "stretch"),
    ("align-self", "center"), ("align-self", "baseline"),
    ("align-self", "first baseline"), ("align-self", "last baseline"),
    ("align-self", "safe center"), ("align-self", "unsafe flex-start"),
    ("align-self", "flex-start"), ("align-self", "end"), ("align-self", "self-start"),
    ("justify-self", "auto"), ("justify-self", "stretch"), ("justify-self", "center"),
    ("justify-self", "legacy"), ("justify-self", "legacy left"),
    ("justify-self", "right"), ("justify-self", "flex-end"),
    # CSS Fonts 4 §4.6: the generic families, the UI set, and a list.
    ("font-family", "serif"), ("font-family", "sans-serif"), ("font-family", "monospace"),
    ("font-family", "cursive"), ("font-family", "fantasy"), ("font-family", "math"),
    ("font-family", "emoji"), ("font-family", "fangsong"), ("font-family", "ui-serif"),
    ("font-family", "ui-monospace"), ("font-family", '"Helvetica Neue"'),
    ("font-family", "Arial, sans-serif"), ("font-family", "Arial sans-serif"),
    ("font-family", "inherit"), ("font-family", '"\""'), ("font-family", "Arial 12pt"),
    # CSS Text Decoration 4 §3.3 and the emphasis colour's own spelling.
    ("text-underline-position", "auto"), ("text-underline-position", "from-font"),
    ("text-underline-position", "under"), ("text-underline-position", "left"),
    ("text-underline-position", "under left"), ("text-underline-position", "under right"),
    ("text-underline-position", "left under"), ("text-underline-position", "auto under"),
    # The names this engine declares that no probe has asked: do they answer at all?
    ("marker", "~"), ("size", "~"), ("starting-style", "~"),
    ("initial-letter-align", "~"), ("text-security", "~"), ("display-type", "~"),
    ("timing-function", "~"), ("offset-position-normal", "~"),
    ("content-visibility-auto-state", "~"), ("position-try-options", "~"),
    ("highlight", "~"), ("page-size", "~"), ("view-timeline", "~"),
    ("scroll-timeline", "~"), ("overscroll-behavior-y", "~"),
    ("text-underline-position", "~"), ("scrollbar-gutter", "~"), ("border", "~"),
    ("text-security", "none"), ("text-security", "disc"), ("text-security", "star"),
    ("text-security", "circle"), ("text-security", "square"),
    ("-webkit-text-security", "disc"), ("-webkit-text-security", "none"),
    ("initial-letter-align", "auto"), ("initial-letter-align", "alphabetic"),
    ("initial-letter-align", "hanging"), ("initial-letter-align", "mathematical"),
    ("marker", "none"), ("marker", "url(#m)"), ("marker", "red"),
    # The legacy webkit spellings this engine answers.
    ("-webkit-writing-mode", "horizontal-tb"), ("-webkit-writing-mode", "vertical-rl"),
    ("-webkit-line-break", "auto"), ("-webkit-line-break", "loose"),
    ("-webkit-line-break", "normal"), ("-webkit-line-break", "anywhere"),
    ("-webkit-text-orientation", "vertical-right"),
    ("-webkit-text-orientation", "mixed"),
    ("-webkit-box-decoration-break", "slice"),
    ("-webkit-box-decoration-break", "clone"),
    ("-webkit-border-horizontal-spacing", "2px"),
    ("-webkit-border-vertical-spacing", "2px 4px"),
    ("-webkit-column-break-before", "auto"), ("-webkit-column-break-before", "always"),
    ("-webkit-column-break-before", "avoid"), ("-webkit-column-break-before", "left"),
    ("-webkit-column-break-after", "right"), ("-webkit-column-break-after", "avoid"),
    ("-webkit-column-break-inside", "avoid"), ("-webkit-column-break-inside", "auto"),
    ("-webkit-column-break-before", "~"), ("-webkit-column-break-inside", "~"),
    # The timeline shorthands, said as shorthands.
    ("scroll-timeline", "none"), ("scroll-timeline", "--t"),
    ("scroll-timeline", "--t block"), ("scroll-timeline", "block"),
    ("view-timeline", "none"), ("view-timeline", "--t inline"),
    ("view-timeline", "auto"), ("view-timeline-name", "--t"),
    ("view-timeline-name", "none"), ("view-timeline-name", "auto"),
    # CSS Animations 1 §4: the timing function's own name slot and the easing spellings.
    ("timing-function", "ease"), ("transition-timing-function", "steps(2, start)"),
    ("transition-timing-function", "cubic-bezier(0, 0, 1, 1)"),
    ("transition-timing-function", "linear(0, 1)"),
    ("transition-timing-function", "ease-in-out"),
    ("animation-timing-function", "step-end"), ("animation-timing-function", "steps(2)"),
    # CSS Anchor Positioning 1 §4.5: the legacy name slot the engine still declares.
    ("position-try-options", "flip-block"), ("position-try-options", "flip-start"),
    ("position-try-options", "flip-block flip-start"),
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
<html><head><meta charset="utf-8"><title>b316 sweep</title></head>
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
with open(os.path.join(HERE, "_probe_b316_sweep.html"), "w", encoding="utf-8") as handle:
    handle.write(page())
print("cases:", len(CASES))
