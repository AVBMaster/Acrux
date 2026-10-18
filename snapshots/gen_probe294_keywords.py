"""Build the b294 keyword-candidate probe.

Each property is asked for the keywords its module names, plus a couple of neighbours it must refuse.
The reference's answer is what the grammar arm reproduces — the list here is the question, never the
answer.

Usage: python3 snapshots/gen_probe294_keywords.py snapshots/_probe_b294_keyword_candidates.html
"""
import json
import sys

# property -> the candidate values to ask. Every one is a keyword some draft of the module names.
CANDIDATES = {
    "direction": ["ltr", "rtl", "inherit"],
    "writing-mode": ["horizontal-tb", "vertical-rl", "sideways-lr"],
    "flex-direction": ["row", "row-reverse", "column", "column-reverse", "flex-row"],
    "flex-wrap": ["nowrap", "wrap", "wrap-reverse", "reverse"],
    "overflow": ["visible", "hidden", "clip", "scroll", "auto", "overlay", "sticky",
                 "visible hidden", "auto auto"],
    "overflow-x": ["visible", "hidden", "clip", "scroll", "auto", "overlay", "sticky"],
    "overflow-y": ["visible", "hidden", "clip", "scroll", "auto", "overlay", "sticky"],
    "overflow-wrap": ["normal", "break-word", "anywhere"],
    "word-wrap": ["normal", "break-word", "anywhere", "break-all"],
    "white-space-collapse": ["collapse", "discard", "preserve", "preserve-breaks",
                             "preserve-spaces", "break-spaces", "normal"],
    "text-wrap-mode": ["wrap", "nowrap", "auto"],
    "text-wrap-style": ["auto", "balance", "stable", "pretty"],
    "text-decoration-line": ["none", "underline", "overline", "line-through", "spelling-error",
                             "grammar-error", "blink", "underline overline"],
    "text-decoration-style": ["solid", "double", "dotted", "dashed", "wavy", "dotted wavy"],
    "text-emphasis": ["none", "auto", "filled", "open", "dot", "circle", "sesame", "accent",
                      "filled dot", "red", "dot red", "before", "right"],
    "pointer-events": ["auto", "none", "visiblePainted", "visible", "painted", "fill", "stroke",
                       "all", "bounding-box", "fill-box", "stroke-box", "visibleStroke"],
    "position": ["static", "relative", "absolute", "fixed", "sticky", "-webkit-sticky",
                 "sticky fixed"],
    "clip": ["auto", "rect(0px 1px 2px 3px)", "inset(0px)", "rect(auto)", "none"],
    "place-content": ["normal", "center", "space-between", "space-around", "space-evenly",
                      "start", "end", "flex-start", "flex-end", "left", "right", "baseline",
                      "first baseline", "last baseline", "stretch", "unsafe start",
                      "center start", "baseline center"],
    "place-items": ["auto", "normal", "center", "start", "end", "flex-start", "stretch",
                    "center start", "legacy", "legacy center"],
    "place-self": ["auto", "normal", "center", "start", "end", "stretch", "center center"],
    "transform-box": ["content-box", "border-box", "fill-box", "stroke-box", "view-box", "auto"],
    "math-style": ["normal", "compact"],
    "math-shift": ["normal", "bold", "subscript"],
    "font-style": ["normal", "italic", "oblique", "oblique 20deg", "oblique 20deg italic",
                   "left", "right", "upright", "auto", "line-through"],
    "font-variant": ["normal", "none", "small-caps", "common-ligatures", "oldstyle-nums",
                     "tabular-nums", "diagonal-fractions", "stacked-fractions",
                     "ordinal", "slashed-zero", "jis78", "ruby", "full-width",
                     "small-caps common-ligatures"],
    "font-stretch": ["normal", "ultra-condensed", "extra-condensed", "condensed",
                     "semi-condensed", "semi-expanded", "expanded", "extra-expanded",
                     "ultra-expanded", "wider", "narrower", "125%"],
    "font-weight": ["normal", "bold", "bolder", "lighter", "100", "900", "1000", "0",
                    "ultra-light", "thin", "medium", "black", "550"],
    "font-synthesis-weight": ["auto", "none"],
    "font-synthesis-style": ["auto", "none"],
    "font-synthesis-small-caps": ["auto", "none", "bold"],
    "field-sizing": ["content", "size", "normal", "content size"],
    "column-span": ["none", "all", "auto"],
    "column-rule-style": ["none", "hidden", "dotted", "dashed", "solid", "double", "groove",
                          "ridge", "inset", "outset"],
    "column-rule-width": ["thin", "medium", "thick", "1px", "0"],
    "column-rule": ["1px", "solid", "red", "1px solid red", "thin", "0"],
    "border-style": ["none", "hidden", "dotted", "dashed", "solid", "double", "groove",
                     "ridge", "inset", "outset", "solid solid solid solid", "solid solid"],
    "border-block-style": ["none", "hidden", "solid", "inset", "outset", "solid solid"],
    "border-inline-style": ["none", "solid", "solid solid"],
    "border-top-style": ["none", "hidden", "solid", "wavy"],
    "border-width": ["thin", "medium", "thick", "1px", "0", "1px 2px", "1px 2px 3px 4px"],
    "border-block-width": ["thin", "medium", "thick", "1px", "0", "1px 2px"],
    "border-inline-width": ["thin", "1px", "1px 2px"],
    "border-top-width": ["thin", "medium", "thick", "1px", "0", "auto"],
    "border-radius": ["1px", "50%", "1px 2px", "1px / 2px", "1px 2px 3px 4px",
                      "1px 2px / 3px 4px", "0", "top left 1px"],
    "border-top-left-radius": ["1px", "50%", "1px 2px", "0", "10% 20%"],
    "border-end-end-radius": ["1px", "50%", "1px 2px"],
    "border-block-color": ["red", "red blue", "currentcolor", "#fff"],
    "border-inline-color": ["red", "red blue"],
    "border-block": ["1px", "solid", "red", "1px solid red", "medium", "0"],
    "border-inline-start": ["1px", "solid", "1px solid red"],
    "list-style-position": ["inside", "outside", "auto"],
    "list-style-image": ["none", "url(a.png)", "linear-gradient(red, blue)", "cross-fade(a,b)"],
    "list-style-type": ["disc", "circle", "square", "decimal", "none", "armenian",
                        "cjk-decimal", "lower-roman", "upper-alpha", "\"x\"", "symbols(*)"],
    "list-style": ["disc", "inside", "url(a.png)", "none", "disc inside", "inside disc",
                   "decimal leading-zero", "katakana-iroha"],
    "background-image": ["none", "url(a.png)", "linear-gradient(red, blue)",
                         "url(a.png), url(b.png)", "cross-fade(a.png, b.png)", "element(x)"],
    "mask-image": ["none", "url(a.png)", "linear-gradient(red, blue)", "url(a.png), url(b.png)"],
    "border-image-source": ["none", "url(a.png)", "linear-gradient(red, blue)"],
    "mask-repeat": ["repeat", "no-repeat", "space", "round", "repeat-x", "repeat-y",
                    "repeat no-repeat", "no-repeat no-repeat"],
    "mask-origin": ["border-box", "padding-box", "content-box", "fill-box", "stroke-box",
                    "view-box", "margin-box", "border-box padding-box"],
    "mask-clip": ["border-box", "padding-box", "content-box", "fill-box", "stroke-box",
                  "view-box", "margin-box", "no-clip", "border-box padding-box"],
    "mask-size": ["auto", "contain", "cover", "1px", "50%", "1px 2px", "auto 1px",
                  "1px auto", "0"],
    "mask-position": ["top", "center", "bottom", "left", "right", "1px", "50%",
                      "1px 2px", "top 1px", "center center"],
    "mask": ["none", "url(a.png)", "1px", "50%", "repeat", "add", "intersect",
             "no-clip", "content-box", "contain", "url(a.png) repeat"],
    "animation-iteration-count": ["infinite", "1", "0", "2.5", "-1", "1 2"],
    "animation": ["none", "infinite", "1s", "slide", "1s slide infinite", "linear",
                  "running", "paused", "reverse", "forwards", "both", "auto", "normal"],
    "transition-property": ["all", "none", "opacity", "opacity, color", "unknown-prop",
                            "all, none"],
    "transition": ["all", "none", "1s", "opacity", "opacity 1s", "all 1s ease-out 0s",
                   "discrete", "allow-discrete"],
    "will-change": ["auto", "none", "all", "opacity", "transform", "opacity, transform",
                    "unknown-prop"],
    "color-scheme": ["normal", "light", "dark", "only light", "only dark", "light dark",
                     "unknown-scheme"],
    "counter-reset": ["none", "x", "x 1", "x 1 y 2", "section", "x -1", "auto"],
    "counter-increment": ["none", "x", "x 1", "x -1", "x 1 y 2", "auto"],
    "counter-set": ["none", "x", "x 1", "x 0", "auto"],
    "grid-area": ["auto", "none", "1", "1 / 2", "1 / 2 / 3 / 4", "span 2", "a", "a / b",
                  "1 span 2"],
    "grid-column": ["auto", "none", "1", "1 / 2", "span 2", "a", "1 span a"],
    "grid-row": ["auto", "1", "1 / 2", "span 2", "a"],
    "grid-template-columns": ["none", "auto", "1px", "50%", "1fr", "100px 1fr",
                              "repeat(2, 1fr)", "[a] 1px", "minmax(1px, 2px)", "min-content",
                              "max-content", "auto auto", "1px 1px"],
    "grid-template-rows": ["none", "auto", "1px", "1fr", "repeat(2, 1px)", "subgrid"],
    "grid-template-areas": ["none", "\"a b\"", "\"a\" \"b\"", "span", "."],
    "grid-template": ["none", "subgrid", "1px 2px", "\"a\" 1px", "auto auto / auto auto"],
    "grid": ["none", "subgrid", "1px", "1px / 2px", "none / auto", "[a] 1px [b]"],
    "grid-gap": ["normal", "1px", "50%", "1px 2px", "auto", "0"],
    "grid-column-gap": ["normal", "1px", "50%", "0", "1px 2px"],
    "grid-row-gap": ["normal", "1px", "50%", "0"],
    "scale": ["none", "1", "1 2", "1 2 3", "50%", "10%", "1 50%", "auto", "1px"],
    "translate": ["none", "1px", "50%", "1px 2px", "1px 2px 3px", "0", "auto",
                  "1px 2px 3px 4px"],
    "offset": ["none", "auto", "normal", "1px", "50%", "1px 2px", "path('M0 0')",
               "url(#a)", "circle()", "calc(1px + 2px)", "0"],
    "view-transition-group": ["normal", "contain", "allow-discrete", "none", "auto",
                              "unknown-ident", "--a"],
    "animation-range": ["normal", "1px", "50%", "1px 2px", "entry", "exit", "cover",
                        "contain", "entry 50% exit 100%", "calc(1px + 2px)", "0"],
    "-webkit-text-orientation": ["mixed", "upright", "sideways", "vertical", "auto"],
    "text-orientation": ["mixed", "upright", "sideways", "vertical"],
}

out_path = sys.argv[1] if len(sys.argv) > 1 else 'snapshots/_probe_b294_keyword_candidates.html'
cases = []
i = 0
for name in sorted(CANDIDATES):
    for value in CANDIDATES[name]:
        cases.append([i, name, value])
        i += 1

html = """<!DOCTYPE html>
<html lang="en"><head><meta charset="utf-8"><title>b294 probe E &#8212; the keyword set each property names</title>
<style>body { font: 14px/18px monospace; color: rgb(9,9,9); margin: 8px; background: #fff; }
#box { width: 200px; } #box > div { height: 20px; }</style></head><body>
<div id="box"></div><pre id="report"></pre>
<script>
/* Every value below is a question: a keyword some module of CSS names for this property, or a
 * neighbour it must refuse. The reference engine's yes and no is what the grammar arm reproduces. */
var CASES = @@CASES@@;
var out = [];
for (var i = 0; i < CASES.length; i++) {
  var prop = CASES[i][1], value = CASES[i][2];
  var d = document.createElement('div');
  document.getElementById('box').appendChild(d);
  d.style.setProperty(prop, value);
  void d.offsetHeight;
  var sup = 'ERR';
  try { sup = CSS.supports(prop, value) ? '1' : '0'; } catch (e) {}
  out.push(CASES[i][0] + '|' + prop + '|' + value + '|sup=' + sup + '|n=' + d.style.length
    + '|echo=' + d.style.getPropertyValue(prop)
    + '|comp=' + getComputedStyle(d).getPropertyValue(prop));
}
text = out.join('\\n');
document.getElementById('report').textContent = text;
console.log(text);
</script></body></html>
"""
open(out_path, 'w', encoding='utf-8').write(html.replace('@@CASES@@', json.dumps(cases, ensure_ascii=False)))
print(out_path, 'rows', len(cases), 'properties', len(CANDIDATES))
