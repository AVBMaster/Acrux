"""Generate the b315 sweep probe: the families no round has swept as a family.

The names come from the engine's own property table minus everything a b308-b314 probe already
wrote a row for: the container triple, the view-transition pair, the speech and SVG words, the
four logical shorthands whose pair folds, and the geometry/painting longhands whose grammar has
more shapes than a keyword set. One fresh element per row (the rule b291 measured), four columns:
what the grammar answers, how many longhands the CSSOM counts, what the style attribute echoes and
what the computed surface answers.
"""
import json
import os

CASES = [
    # CSS Containment 3 §3.5 + Conditional 3 §3.3: the container name, type and shorthand.
    ("container", "normal"), ("container", "none"), ("container", "size"),
    ("container", "inline-size"), ("container", "size --c"), ("container", "--c"),
    ("container", "--c size"), ("container", "--c inline-size"),
    ("container", "inline normal"), ("container", "normal size"),
    ("container", "size, inline-size"), ("container", "SCROLL-STATE"),
    ("container-name", "none"), ("container-name", "--c"), ("container-name", "c"),
    ("container-name", '"c"'), ("container-name", "--c, --d"), ("container-name", "--c --d"),
    ("container-name", "auto"), ("container-name", "initial"),
    ("container-type", "normal"), ("container-type", "size"), ("container-type", "inline-size"),
    ("container-type", "block-size"), ("container-type", "scroll-state"),
    ("container-type", "scroll-state(--a)"), ("container-type", "AUTO"),
    # CSS View Transitions 1 §4: the group and the class a transition names.
    ("view-transition-group", "normal"), ("view-transition-group", "auto"),
    ("view-transition-group", "my-name"), ("view-transition-group", "--c"),
    ("view-transition-class", "none"), ("view-transition-class", "a"),
    ("view-transition-class", "a b"), ("view-transition-class", "a, b"),
    ("view-transition-class", "auto"),
    # CSS Speech 1 §4-§5: what this engine reads of the speech family.
    ("speak", "auto"), ("speak", "none"), ("speak", "normal"), ("speak", "spell-out"),
    ("speak", "none normal"), ("speak-as", "normal"),
    # SVG 2 §11.15.1 and CSS UI 4 §6: the anchor word and inertness.
    ("text-anchor", "start"), ("text-anchor", "middle"), ("text-anchor", "end"),
    ("text-anchor", "auto"), ("text-anchor", "left"),
    ("interactivity", "auto"), ("interactivity", "inert"), ("interactivity", "none"),
    # CSS Sizes 4 §5.1 / Values 4 §10.2: interpolating to keywords, and discrete behaviour.
    ("interpolate-size", "numeric"), ("interpolate-size", "allow-keywords"),
    ("interpolate-size", "auto"), ("interpolate-size", "10px"),
    ("transition-behavior", "normal"), ("transition-behavior", "allow-discrete"),
    ("transition-behavior", "allow-discrete, normal"),
    ("transition-behavior", "normal normal"), ("transition-behavior", "auto"),
    # CSS Scrollbars 1 §4.1: where a scroller starts.
    ("scroll-initial-target", "none"), ("scroll-initial-target", "nearest"),
    ("scroll-initial-target", "auto"), ("scroll-initial-target", "top"),
    # CSS Shapes 1 §4.5: the threshold is a number, not a length.
    ("shape-image-threshold", "0"), ("shape-image-threshold", "1"),
    ("shape-image-threshold", "0.5"), ("shape-image-threshold", "-1"),
    ("shape-image-threshold", "50%"), ("shape-image-threshold", "calc(0.2 + 0.3)"),
    ("shape-image-threshold", "10px"),
    # The legacy webkit words this engine still answers.
    ("-webkit-app-region", "none"), ("-webkit-app-region", "drag"),
    ("-webkit-app-region", "no-drag"), ("-webkit-rtl-ordering", "logical"),
    ("-webkit-rtl-ordering", "numeric"), ("-webkit-text-combine", "none"),
    ("-webkit-text-combine", "horizontal"), ("-webkit-print-color-adjust", "economy"),
    ("-webkit-print-color-adjust", "exact"), ("-webkit-print-color-adjust", "auto"),
    # CSS Logical 1 §4.3: the four pairs that fold, and the two that elide an initial side.
    ("inset-block", "10px"), ("inset-block", "10px 20px"), ("inset-block", "auto"),
    ("inset-block", "10px 10px"), ("inset-block", "top 20px"),
    ("inset-inline", "0"), ("inset-inline", "5% 10%"), ("inset-inline", "auto auto"),
    ("margin-block", "10px"), ("margin-block", "10px 20px"), ("margin-block", "auto"),
    ("margin-inline", "10px 20px"), ("margin-inline", "5%"),
    ("padding-block", "10px"), ("padding-block", "10px 10px"),
    ("padding-inline", "0 10px"), ("padding-inline", "auto"),
    ("scroll-margin-block", "10px"), ("scroll-margin-inline", "10px 20px"),
    ("scroll-padding-block", "auto"), ("scroll-padding-inline", "10px"),
    ("inset-block-end", "10px"), ("inset-inline-end", "auto"),
    # CSS Multi-column 1 §7.1: the pair, and which side the length is on.
    ("columns", "2"), ("columns", "auto"), ("columns", "200px"),
    ("columns", "2 200px"), ("columns", "200px 2"), ("columns", "auto 200px"),
    ("columns", "normal"), ("columns", "2 auto"), ("columns", "-1"), ("columns", "1.5"),
    # CSS Flexbox 1 §5.3: the direction/wrap pair and its elisions.
    ("flex-flow", "row"), ("flex-flow", "column"), ("flex-flow", "row wrap"),
    ("flex-flow", "wrap row"), ("flex-flow", "column-reverse nowrap"),
    ("flex-flow", "wrap wrap"), ("flex-flow", "nowrap"), ("flex-flow", "row nowrap wrap"),
    # CSS UI 4 §16 and 2 §18: the outline shorthand, including its own keyword width.
    ("outline", "2px solid red"), ("outline", "auto"), ("outline", "thin dotted"),
    ("outline", "2px"), ("outline", "solid"), ("outline", "invert"),
    ("outline", "2px solid red 5px"), ("outline", "medium auto"),
    # CSS Backgrounds 3 §4.1: the syntax levels on one axis.
    ("background-position-x", "left"), ("background-position-x", "0%"),
    ("background-position-x", "10px 20px"), ("background-position-x", "calc(50% - 10px)"),
    ("background-position-x", "right 10px"), ("background-position-x", "repeat-x"),
    ("background-position-x", "space"), ("background-position-x", "center center"),
    ("background-position-y", "top"), ("background-position-y", "bottom 10px"),
    ("background-position-y", "repeat"), ("background-position-y", "50%"),
    ("background-attachment", "scroll"), ("background-attachment", "fixed"),
    ("background-attachment", "local"), ("background-attachment", "scroll fixed"),
    ("background-repeat", "repeat-x"), ("background-repeat", "space round"),
    ("background-repeat", "no-repeat no-repeat"), ("background-repeat", "repeat repeat"),
    ("background-origin", "padding-box border-box"),
    ("background-clip", "content-box border-box"),
    ("background-size", "10px"), ("background-size", "cover"),
    ("background-size", "auto auto"), ("background-size", "50% 10px"),
    ("background-size", "min-content"), ("background-size", "calc(100% - 20px)"),
    # CSS Sizing 3 §5.3: the intrinsic size pair and its 'auto' flag.
    ("contain-intrinsic-size", "auto 100px"), ("contain-intrinsic-size", "none"),
    ("contain-intrinsic-size", "100px 200px"), ("contain-intrinsic-size", "auto none"),
    ("contain-intrinsic-size", "auto 100px 200px"), ("contain-intrinsic-size", "auto auto"),
    ("contain-intrinsic-width", "auto 100px"), ("contain-intrinsic-height", "100px"),
    ("contain-intrinsic-inline-size", "auto 100px"),
    # CSS Overscroll 1 §3.1 and Overflow 3 §3.2: the pair words.
    ("overscroll-behavior-block", "auto"), ("overscroll-behavior-block", "contain"),
    ("overscroll-behavior-block", "none"), ("overscroll-behavior-inline", "contain none"),
    ("overflow-y", "clip"), ("overflow-y", "auto hidden"), ("overflow-y", "visible clip"),
    ("overflow-clip-margin", "10px"), ("overflow-clip-margin", "content-box"),
    ("overflow-clip-margin", "border-box"), ("overflow-clip-margin", "paint-area"),
    ("overflow-clip-margin", "0"), ("overflow-clip-margin", "50%"),
    ("overflow-anchor", "auto"), ("overflow-anchor", "none"),
    # CSS Display 3 §3.1 and Visibility 3 §1: the two-axis visibility set.
    ("visibility", "visible"), ("visibility", "hidden"), ("visibility", "collapse"),
    ("visibility", "auto"), ("content-visibility", "visible"),
    ("content-visibility", "auto"), ("content-visibility", "hidden"),
    ("content-visibility", "contain"), ("direction", "ltr"), ("direction", "rtl"),
    ("direction", "inherit"),
    # CSS Text 4 §2.4 and Text 3 §4.2: the last line, the emphasis position, the sizing.
    ("text-align-last", "auto"), ("text-align-last", "start"), ("text-align-last", "end"),
    ("text-align-last", "justify"), ("text-align-last", "left right"),
    ("text-align", "start match-parent"), ("text-align", "justify all"),
    ("text-emphasis-position", "over"), ("text-emphasis-position", "over right"),
    ("text-emphasis-position", "under left"), ("text-emphasis-position", "left"),
    ("text-emphasis-style", "dot"), ("text-emphasis-style", "filled circle"),
    ("text-emphasis-style", "open sesame"), ("text-emphasis-style", "auto"),
    ("text-emphasis", "dot"), ("text-emphasis", "filled"), ("text-emphasis", "over dot"),
    ("text-size-adjust", "100%"), ("text-size-adjust", "auto"), ("text-size-adjust", "none"),
    ("text-size-adjust", "0.5"), ("text-size-adjust", "80% font"),
    # CSS UI 4 §7.1 and Tables 1 §4.2: resize and the three table words.
    ("resize", "none"), ("resize", "both"), ("resize", "horizontal"),
    ("resize", "block"), ("resize", "inline"), ("resize", "vertical both"),
    ("table-layout", "auto"), ("table-layout", "fixed"), ("table-layout", "100px"),
    ("empty-cells", "show"), ("empty-cells", "hide"), ("empty-cells", "auto"),
    ("caption-side", "top"), ("caption-side", "block-end"), ("caption-side", "inline-start"),
    ("caption-side", "left"), ("border-collapse", "collapse"),
    ("border-collapse", "separate"), ("border-collapse", "separate 4px"),
    # CSS Grid 2 §6.3: the implicit tracks and the flow pair.
    ("grid-auto-columns", "auto"), ("grid-auto-columns", "100px"),
    ("grid-auto-columns", "minmax(10px, 1fr)"), ("grid-auto-columns", "fit-content(100px)"),
    ("grid-auto-columns", "1fr 2fr"), ("grid-auto-columns", "max-content"),
    ("grid-auto-flow", "row"), ("grid-auto-flow", "column"), ("grid-auto-flow", "dense"),
    ("grid-auto-flow", "row dense"), ("grid-auto-flow", "stack"),
    ("grid-auto-flow", "row column"), ("grid-auto-flow", "dense row"),
    # CSS Order 1 §4 and Will Change 2 §2: a number, and the change hint set.
    ("order", "1"), ("order", "-1"), ("order", "1.5"), ("order", "calc(1 + 2)"),
    ("order", "auto"), ("order", "2px"),
    ("will-change", "auto"), ("will-change", "scroll"), ("will-change", "contents"),
    ("will-change", "transform, opacity"), ("will-change", "transform opacity"),
    ("will-change", "--x"), ("will-change", "all"),
    # CSS Text 4 §1.6, §2.2, §5.5: the words the whitespace family split into.
    ("tab-size", "4"), ("tab-size", "2em"), ("tab-size", "40px"), ("tab-size", "-1"),
    ("white-space-collapse", "collapse"), ("white-space-collapse", "preserve"),
    ("white-space-collapse", "preserve-breaks"), ("white-space-collapse", "break-spaces"),
    ("text-spacing-trim", "space-all"), ("text-spacing-trim", "no-start"),
    ("text-spacing-trim", "trim-start"), ("text-spacing-trim", "trim-both"),
    ("text-spacing-trim", "space-first"), ("text-spacing-trim", "normal"),
    ("line-sliding", "none"), ("line-sliding", "translate"), ("line-sliding", "contain"),
    ("line-wrap-after", "auto"), ("line-wrap-after", "force"), ("line-wrap-after", "anywhere"),
    ("word-break", "normal"), ("word-break", "break-all"), ("word-break", "keep-all"),
    ("word-break", "break-strict"),
    # CSS Fonts 4 §8.6 and Text Decor 3 §4: the metric pair and the thickness pair.
    ("text-underline-offset", "3px"), ("text-underline-offset", "0.2em"),
    ("text-underline-offset", "auto"), ("text-underline-offset", "50%"),
    ("text-decoration-thickness", "auto"), ("text-decoration-thickness", "from-font"),
    ("text-decoration-thickness", "2px"), ("text-decoration-thickness", "10%"),
    ("text-decoration-thickness", "auto from-font"),
    ("text-decoration-skip-ink", "auto"), ("text-decoration-skip-ink", "none"),
    ("text-decoration-skip-ink", "all"),
    # CSS Box 3 §4.2 and Values 4 §7: the two sizing keywords per axis.
    ("block-size", "stretch"), ("block-size", "contain"), ("block-size", "cover"),
    ("inline-size", "none"), ("inline-size", "stretch"), ("min-inline-size", "stretch"),
    ("max-block-size", "stretch"), ("min-block-size", "contain"),
    # The declaration surface for the same names, so a family with no grammar of its own is asked.
    ("container", "~"), ("container-type", "~"), ("view-transition-group", "~"),
    ("speak", "~"), ("text-anchor", "~"), ("interactivity", "~"),
    ("interpolate-size", "~"), ("transition-behavior", "~"),
    ("scroll-initial-target", "~"), ("shape-image-threshold", "~"),
    ("inset-block", "~"), ("columns", "~"), ("flex-flow", "~"), ("outline", "~"),
    ("background-position-x", "~"), ("contain-intrinsic-size", "~"),
    ("overscroll-behavior-block", "~"), ("overflow-clip-margin", "~"),
    ("text-spacing-trim", "~"), ("line-sliding", "~"), ("grid-auto-flow", "~"),
    ("will-change", "~"), ("text-decoration-thickness", "~"), ("block-size", "~"),
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
<html><head><meta charset="utf-8"><title>b315 sweep</title></head>
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
with open(os.path.join(HERE, "_probe_b315_sweep.html"), "w", encoding="utf-8") as handle:
    handle.write(page())
print("cases:", len(CASES))
