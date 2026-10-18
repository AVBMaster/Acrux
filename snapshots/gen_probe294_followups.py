"""Generate probe F: the values that tell one grammar from its neighbour.

Probe E (snapshots/_probe_b294_keyword_candidates.html) said which properties disagree with the
reference engine; several of those disagreements are between two grammars that both fit the values
already measured — 'scale: 50%' folding to a number, 'mask: add' keeping a composite the shorthand
drops, 'animation-range' being a pair. This page asks the questions that separate them, and adds the
pair a parent's own value decides (a relative font-weight).

Every row is 'index|prop|value|sup=|n=|echo=|comp=|text=', where 'text=' is what the reference
engine prints for getComputedStyle of the *parent*, only for the rows that need it.

Usage: python3 snapshots/gen_probe294_followups.py snapshots/_probe_b294_followups.html
"""
import sys

# [index, property, value, parent declarations]
CASES = []


def add(prop, values, parent=None):
    for value in values:
        CASES.append((prop, value, parent))


# ---- the legacy text-orientation word set, and the spelling its computed value answers in
add("-webkit-text-orientation", ["vertical-right", "vertical-left", "horizontal-top-to-bottom",
                                 "horizontal-bottom-to-top", "sideways-left", "sideways-right",
                                 "mixed", "upright", "sideways"])

# ---- the animation shorthand's computed value: which components it prints, and in what order
add("animation", ["none", "1s", "slide", "1s slide", "slide 1s", "2s slide 3s infinite linear",
                  "a, b", "1s a, 2s b", "running", "infinite", "normal", "both", "reverse",
                  "auto", "paused", "linear", "forwards", "backwards", "ease-in-out",
                  "cubic-bezier(0.1, 2, 3, 4)", "1s ease 2s slide infinite reverse forwards paused",
                  "0s", "-1s"])

# ---- a longhand list is comma-separated; a space inside one item is no value
add("animation-iteration-count", ["1, 2", "1 2", "infinite, 1", "-1", "0", "1.5", "calc(1)",
                                  "infinite infinite"])
add("animation-duration", ["1s, 2s", "1s 2s", "0s", "-1s", "1", "calc(1s + 2s)"])
add("animation-delay", ["1s, 2s", "-1s", "1s 2s"])
add("animation-name", ["none", "a", "a, b", "a b", "inherit", "normal", "none none"])
add("animation-fill-mode", ["forwards", "forwards backwards", "forwards, backwards", "none"])
add("animation-direction", ["reverse", "alternate", "alternate-reverse", "reverse normal",
                            "reverse, normal", "normal"])
add("animation-timing-function", ["linear", "ease-in-out", "steps(2)", "linear ease-out",
                                  "linear, ease-out", "cubic-bezier(0.1, 2, 3, 4)",
                                  "linear(0, 1 50%, 1)"])
add("animation-play-state", ["running", "paused", "running paused", "running, paused"])
add("animation-composition", ["add", "accumulate", "replace", "add, accumulate", "add add"])

# ---- animation-range: a pair of edges, each an area plus an optional offset
add("animation-range", ["normal", "normal normal", "1px 2px 3px", "entry 1px", "entry 50%",
                        "cover cover", "contain 1px", "1px", "0", "0px", "auto",
                        "entry-crossing", "exit-crossing", "span-a", "calc(1px + 2px) 3px",
                        "entry calc(50% + 1px)", "normal 1px", "1px normal",
                        "entry exit", "cover 50% contain 100%"])
add("animation-range-start", ["normal", "1px", "50%", "entry", "cover", "entry 50%", "calc(1px + 2px)"])

# ---- field-sizing: which word spells 'grow with the text'
add("field-sizing", ["fixed", "auto", "content", "normal", "content fixed", "content auto",
                     "auto content", "content content", ""])

# ---- a relative font-weight resolves against the parent the row names
add("font-weight", ["normal", "bold", "bolder", "lighter", "100", "900", "1000", "1001", "-1",
                    "0", "1.5", "250", "999", "550", "600"])
add("font-weight", ["bolder", "lighter"], parent="font-weight: 300")
add("font-weight", ["bolder", "lighter"], parent="font-weight: 500")
add("font-weight", ["bolder", "lighter"], parent="font-weight: 700")
add("font-weight", ["bolder", "lighter"], parent="font-weight: 100")
add("font-weight", ["bolder", "lighter"], parent="font-weight: 900")

# ---- font-stretch: the word set, and the percentage each word computes to
add("font-stretch", ["normal", "100%", "0%", "125%", "wider", "narrower", "ultra-condensed",
                     "semi-expanded", "calc(100% + 1px)", "bold"])

# ---- clip: the legacy rect() with the comma spelling the reference prints
add("clip", ["auto", "rect(0)", "rect(0 0)", "rect(0 0 0 0)", "rect(1px auto)",
             "rect(0px, 1px, 2px, 3px)", "rect(0)", "rect(10px 5px 20px 5px)", "rect()",
             "rect(0 0 0 0, auto)", "inset(0)", "shape(rect(0 0))", "auto auto"])

# ---- the mask shorthand: which component each word belongs to, and what the echo prints
add("mask", ["none", "url(a.png)", "url(a.png) 1px", "url(a.png) 1px 2px", "url(a.png) repeat",
             "url(a.png) repeat-x", "url(a.png) no-repeat", "url(a.png) content-box",
             "url(a.png) border-box", "url(a.png) add", "url(a.png) intersect",
             "url(a.png) luminance", "url(a.png) alpha", "url(a.png) no-clip",
             "url(a.png) 1px center", "url(a.png) 50% 50%", "linear-gradient(red, blue)",
             "url(a.png), url(b.png)", "url(a.png) content-box border-box",
             "url(a.png) center / contain", "url(a.png) center / 10px", "50% 50%",
             "top", "left 1px", "1px", "repeat", "no-repeat", "alpha", "luminance", "add",
             "subtract", "exclude", "no-clip", "content-box", "padding-box", "view-box",
             "border-box", "contain", "cover", "auto", "inherit"])
add("mask-repeat", ["no-repeat repeat", "repeat space", "space round", "repeat-x", "repeat-y",
                    "no-repeat", "repeat repeat", "space", "round"])
add("mask-composite", ["add", "intersect", "subtract", "exclude", "add, subtract", "add add"])
add("mask-mode", ["luminance", "alpha", "match-source", "luminance, alpha", "auto"])
add("mask-origin", ["border-box", "padding-box", "fill-box", "stroke-box", "view-box", "margin-box",
                    "content-box border-box", "auto"])
add("mask-clip", ["border-box", "padding-box", "content-box", "fill-box", "stroke-box", "view-box",
                  "margin-box", "no-clip", "border-box padding-box", "auto"])
add("mask-size", ["auto", "1px", "1px 2px", "1px auto", "auto auto", "0", "50%", "contain", "cover",
                  "1px 2px 3px"])
add("mask-position", ["top", "center", "1px", "1px 2px", "top 1px", "left 1px", "bottom",
                      "center left", "right 1px bottom", "top center"])

# ---- the motion-path shorthand: which axis the offsets land on, and the computed order
add("offset", ["none", "auto", "normal", "0", "1px", "1px 2px", "1px 2px 3px", "50%",
               "path('M0 0')", "path('M0 0') 1px", "url(#a)", "circle()", "calc(1px + 2px)",
               "1px 90deg", "normal 1px", "auto 1px", "45deg", "1px 2px 45deg",
               "margin-box path('M0 0')", "padding-box", "content-box"])

# ---- the alignment family: which word set belongs to which axis, and the pair an echo prints
add("place-content", ["normal", "center", "start", "end", "left", "right", "baseline",
                      "first baseline", "last baseline", "stretch", "unsafe start",
                      "safe center", "center start", "baseline center", "center baseline",
                      "space-between", "flex-start", "flex-end", "legacy center"])
add("place-items", ["auto", "normal", "center", "start", "end", "left", "right", "stretch",
                    "flex-start", "baseline", "first baseline", "center start", "legacy",
                    "legacy center", "legacy left", "unsafe start", "self-start", "self-end"])
add("place-self", ["auto", "normal", "center", "start", "end", "left", "right", "stretch",
                   "center center", "center start", "baseline", "unsafe center", "legacy center"])
add("align-content", ["normal", "baseline", "first baseline", "last baseline", "stretch",
                      "left", "right", "unsafe start", "legacy center"])
add("justify-content", ["normal", "baseline", "left", "right", "stretch", "unsafe start",
                         "legacy center", "first baseline", "last baseline"])
add("align-items", ["auto", "normal", "baseline", "first baseline", "last baseline", "stretch",
                    "left", "right", "unsafe start", "legacy", "legacy center", "self-start"])
add("justify-items", ["auto", "normal", "baseline", "left", "right", "stretch", "legacy center",
                      "unsafe start", "self-start", "last baseline"])

# ---- overflow: the pair an echo elides, and the value a computed surface renames
add("overflow", ["auto auto", "visible visible", "hidden hidden", "clip clip", "overlay overlay",
                 "auto visible", "visible auto", "scroll auto", "sticky auto", "overlay"])
add("overflow-x", ["overlay", "sticky", "visible"])
add("column-rule", ["1px", "solid", "red", "1px solid red", "thin", "0", "none", "hidden",
                    "1px solid", "solid red"])
add("column-rule-style", ["none", "hidden", "dotted", "dashed", "solid", "double", "groove",
                          "ridge", "inset", "outset", "wavy", "auto"])
add("column-rule-width", ["thin", "medium", "thick", "1px", "0", "auto", "1px 2px"])
add("column-rule-color", ["red", "currentcolor", "#fff", "red blue"])

# ---- the logical border family: how many longhands each shorthand counts as, and the computed
#    text of the pair
add("border-block", ["1px", "solid", "red", "1px solid red", "medium", "0", "none", "hidden",
                     "1px solid", "1px 2px", "solid solid", "red blue"])
add("border-block-start", ["1px", "solid", "1px solid red", "0"])
add("border-block-color", ["red", "red blue", "currentcolor", "#fff", "red, blue"])
add("border-block-style", ["none", "hidden", "solid", "inset", "outset", "solid solid",
                           "solid, solid", "wavy"])
add("border-block-width", ["thin", "medium", "thick", "1px", "0", "1px 2px", "auto"])
add("border-inline", ["1px", "solid", "1px solid red"])
add("border-inline-start", ["1px", "solid", "1px solid red"])
add("border-inline-end", ["1px"])
add("border-inline-color", ["red", "red blue"])
add("border-inline-style", ["none", "solid", "solid solid"])
add("border-inline-width", ["thin", "1px", "1px 2px"])
add("border-block-width", ["1px"], parent="border-block-style: solid")
add("border-block-color", ["red"], parent="border-block-style: solid")
add("border-block-style", ["solid"], parent="border-block-width: 3px")

# ---- the logical corners need their own computed text
add("border-end-end-radius", ["1px", "50%", "1px 2px", "0", "10% 20%"])
add("border-start-start-radius", ["1px", "50%", "1px 2px"])
add("border-start-end-radius", ["1px"])
add("border-end-start-radius", ["1px"])
add("border-top-left-radius", ["1px", "1px 2px", "50%"])
add("border-radius", ["1px", "50%", "1px 2px", "1px / 2px", "top left 1px", "0 0 0 0",
                      "1px 1px 1px 1px", "1px 1px 1px 1px / 2px 2px 2px 2px", "auto"])

# ---- a border style's computed spelling, and the width word that is nobody's
add("border-style", ["none", "hidden", "solid", "wavy", "auto", "hidden hidden hidden hidden"])
add("border-top-style", ["hidden", "wavy", "auto"])
add("border-top-width", ["auto", "1px", "thin", "medium", "thick", "-1px", "50%"])
add("border-width", ["auto", "1px 2px 3px 4px", "thin medium thick 1px"])

# ---- the image functions each property will take
IMAGE_VALUES = ["url(a.png)", "linear-gradient(red, blue)", "radial-gradient(red, blue)",
                "conic-gradient(red, blue)", "cross-fade(a.png, b.png)", "element(x)",
                "image-set(url(a.png) 1x)", "-webkit-image-set(url(a.png) 1x)",
                "paint(x)", "none", "url(a.png), url(b.png)", "repeating-linear-gradient(red, blue)",
                "url(#g)", "paint-worklet(x)"]
add("background-image", IMAGE_VALUES)
add("mask-image", IMAGE_VALUES)
add("list-style-image", IMAGE_VALUES)
add("border-image-source", IMAGE_VALUES)
add("column-rule", IMAGE_VALUES[:2])

# ---- the list-style shorthand: which order the echo prints, and what the type component takes
add("list-style", ["disc", "inside", "url(a.png)", "none", "disc inside", "inside disc",
                   "decimal leading-zero", "katakana-iroha", "symbols(*)", '"x"',
                   "disc url(a.png) inside", "url(a.png) disc inside", "inside url(a.png)",
                   "circle outside", "ethiopic-numeric", "arabic-indic", "cjk-decimal",
                   "decimal-leading-zero", "lower-alpha", "upper-alpha", "upper-latin",
                   "auto", "hebrew", "georgian", "armenian", "disc disc"])
add("list-style-type", ["disc", "upper-alpha", "upper-latin", "decimal-leading-zero",
                        "decimal leading-zero", "symbols(*)", '"x"', "auto", "bogus-zzz",
                        "circle", "ethiopic-numeric", "cjk-decimal", "hebrew", "arabic-indic",
                        "georgian", "armenian", "lower-greek", "simp-chinese-informal",
                        "trad-chinese-formal", "korean-hangul-formal", "thai", "myanmar",
                        "khmer", "lao", "tibetan", "mongolian", "tamil", "devanagari",
                        "gujarati", "gurmukhi", "kannada", "malayalam", "oriya", "persian",
                        "urdu", "telugu", "sinhala", "bengali", "gregorian", "hiragana",
                        "katakana", "cjk-heavenly-stem", "cjk-earthly-branch"])
add("list-style-position", ["inside", "outside", "auto", "inside outside"])

# ---- the grid shorthands: how the computed value reads, and the track list the grammar takes
add("grid", ["none", "1px / 2px", "none / auto", "auto / auto", "row", "column", "dense",
             "row dense", "[a] 1px / 2px", "1px 2px / 3px", "\"a\" / 1px", "subgrid",
             "1px 1fr / minmax(1px, 2px)", "none / none / none", "1px / 2px / 3px"])
add("grid-template", ["none", "subgrid", "1px 2px", "\"a\" 1px", "auto auto / auto auto",
                      "1px / 2px", "none / 1px", "\"a\" \"b\"", "[a] 1px [b] / 2px",
                      "subgrid / subgrid", "1px subgrid"])
add("grid-template-rows", ["none", "auto", "1px", "subgrid", "subgrid 1px", "1px 2px",
                           "repeat(2, 1px)", "[a] 1px [b]", "auto-flow", "span 2"])
add("grid-template-columns", ["none", "auto", "1px", "subgrid", "1fr", "minmax(1px, 2px)",
                              "fit-content(1px)", "min-content", "max-content"])
add("grid-template-areas", ["none", "\"a b\"", "\"a\" \"b\"", "span", ".", "\".\"",
                            "a", "\"a\" \"a\"", "\"a b\" \"c d\"", "dotted"])
add("grid-gap", ["normal", "1px", "50%", "1px 2px", "auto", "0", "1px, 2px", "-1px"])
add("grid-column-gap", ["normal", "1px", "50%", "0", "1px 2px", "auto", "-1px"])
add("grid-row-gap", ["normal", "1px", "auto", "0"])
add("column-gap", ["normal", "1px", "auto", "50%", "0", "1px 2px"])
add("row-gap", ["normal", "1px", "auto", "0"])
add("grid-auto-rows", ["auto", "1px", "min-content", "1px 2px", "repeat(2, 1px)", "subgrid"])
add("grid-auto-flow", ["row", "column", "dense", "row dense", "column dense", "dense row",
                       "auto", "row column"])
add("grid-area", ["auto", "none", "1", "1 / 2", "1 / 2 / 3 / 4", "span 2", "a", "a / b",
                  "1 span 2", "span", "a span 2 / b"])
add("grid-column", ["auto", "none", "1", "1 / 2", "span 2", "a", "1 span a", "span a",
                    "a / b / c"])
add("grid-row", ["auto", "1", "1 / 2", "span 2", "a"])

# ---- the two shorthands the cascade owns as an echo
add("transition", ["all", "none", "1s", "opacity", "opacity 1s", "all 1s ease-out 0s", "discrete",
                   "allow-discrete", "opacity 1s, color 2s", "1s opacity"])
add("transition-property", ["all", "none", "opacity", "opacity, color", "unknown-prop",
                            "all, none", "all none", "--custom", "\"quoted\""])
add("will-change", ["auto", "none", "all", "opacity", "unknown-prop", "scroll",
                    "opacity, transform", "--custom", "translate"])
add("color-scheme", ["normal", "light", "dark", "only light", "only dark", "light dark",
                     "unknown-scheme", "auto", "only", "light, dark"])
add("counter-reset", ["none", "x", "x 1", "x 1 y 2", "auto", "section", "x -1", "x 1.5",
                      "x none", "x y", "1", "none x"])
add("counter-increment", ["none", "x", "x 1", "x -1", "x 1 y 2", "auto", "x 0", "x y"])
add("counter-set", ["none", "x", "x 1", "x 0", "auto", "x y"])

# ---- the transform individual properties: what a percentage folds to, and how many values
add("scale", ["none", "1", "1 2", "1 2 3", "50%", "10%", "1 50%", "auto", "1px", "0",
              "1, 2", "50% 50%", "calc(2)", "2 50% 2"])
add("translate", ["none", "1px", "50%", "1px 2px", "1px 2px 3px", "0", "auto",
                  "1px 2px 3px 4px", "10px 20px", "calc(1px + 2px)", "length"])
add("rotate", ["none", "45deg", "x 45deg", "1 0 0 45deg", "0.5turn", "45", "1 2",
               "x 1", "z 45deg", "1 0 0 0.5turn"])

# ---- the writing and wrapping word sets
add("white-space-collapse", ["collapse", "discard", "preserve", "preserve-breaks",
                             "preserve-spaces", "break-spaces", "normal", "auto", "collapse twice"])
add("text-wrap-mode", ["wrap", "nowrap", "auto", "normal", "wrap nowrap", "balance"])
add("text-wrap-style", ["auto", "balance", "stable", "pretty", "normal", "anywhere"])
add("transform-box", ["content-box", "border-box", "fill-box", "stroke-box", "view-box", "auto",
                      "margin-box", "padding-box"])
add("position", ["static", "relative", "absolute", "fixed", "sticky", "-webkit-sticky",
                 "running", "auto", "relative absolute"])
add("pointer-events", ["auto", "none", "visiblePainted", "visible", "painted", "fill", "stroke",
                       "all", "bounding-box", "fill-box", "stroke-box", "visibleStroke",
                       "visibleFill", "stroke-box"])
add("math-shift", ["normal", "bold", "subscript", "super", "auto"])
add("math-style", ["normal", "compact", "bold", "auto"])
add("text-decoration-style", ["solid", "double", "dotted", "dashed", "wavy", "dotted wavy",
                              "solid solid", "hidden"])
add("text-decoration-line", ["none", "underline", "overline", "line-through", "blink",
                             "spelling-error", "grammar-error", "underline overline",
                             "underline underline"])
add("text-emphasis", ["none", "auto", "filled", "open", "dot", "circle", "sesame", "accent",
                      "filled dot", "red", "dot red", "before", "right", "over", "under",
                      "left", "filled dot red", "red dot"])
add("flex-direction", ["row", "row-reverse", "column", "column-reverse", "flex-row", "auto"])
add("flex-wrap", ["nowrap", "wrap", "wrap-reverse", "reverse", "auto", "wrap nowrap"])
add("font-synthesis-small-caps", ["auto", "none", "bold", "normal"])
add("font-synthesis-style", ["auto", "none"])
add("font-synthesis-weight", ["auto", "none"])
add("font-variant", ["normal", "none", "small-caps", "common-ligatures", "oldstyle-nums",
                     "tabular-nums", "diagonal-fractions", "stacked-fractions", "ordinal",
                     "slashed-zero", "jis78", "ruby", "full-width", "small-caps common-ligatures",
                     "common-ligatures small-caps", "proportional-nums", "lining-nums",
                     "all-small-caps", "petit-caps", "titling-caps", "unicase", "no-common-ligatures",
                     "historical-forms", "scientific-infractions", "fraction", "capital-width"])
add("font-style", ["normal", "italic", "oblique", "oblique 20deg", "oblique 20deg italic",
                   "left", "right", "upright", "auto", "italic oblique 20deg"])
add("font-synthesis", ["auto", "none", "weight", "style", "small-caps", "weight style",
                       "none none", "bold"])
add("column-span", ["none", "all", "auto", "1"])
add("writing-mode", ["horizontal-tb", "vertical-rl", "vertical-lr", "sideways-lr", "sideways-rl",
                     "lr-tb", "tb-rl", "auto"])
add("text-orientation", ["mixed", "upright", "sideways", "vertical", "auto", "sideways-left"])
add("word-wrap", ["normal", "break-word", "anywhere", "break-all", "auto"])
add("view-transition-group", ["normal", "contain", "allow-discrete", "none", "auto",
                              "unknown-ident", "--a", "normal contain"])
add("background-image", ["auto", "normal", "red", "none none"])


def build():
    # Build the JS array with proper quoting instead of formatting by hand.
    def quote(text):
        return '"' + text.replace('\\', '\\\\').replace('"', '\\"') + '"'

    entries = ",\n  ".join("[{0}, {1}, {2}, {3}]".format(
        index, quote(prop), quote(value), quote(parent) if parent else "null")
        for index, (prop, value, parent) in enumerate(CASES))

    return TEMPLATE.replace('@@entries@@', entries)


# '@@entries@@' is the generated CASES array. The template is not an f-string: a CSS block's
# braces would be read as a replacement field.
TEMPLATE = """<!DOCTYPE html>
<html lang="en"><head><meta charset="utf-8"><title>b294 probe F &#8212; the values that separate two grammars</title>
<style>body { font: 14px/18px monospace; color: rgb(9,9,9); margin: 8px; background: #fff; }
#box { width: 200px; } #box > div { height: 20px; }
/* The rows with a parent set the property on a child of a box that already carries a value, so a
 * relative font-weight has a number to resolve against. */
#pbox { width: 200px; }</style></head><body>
<div id="pbox"></div><div id="box"></div><pre id="report"></pre>
<script>
/* A row is [index, property, value, decl]. 'decl' is a declaration written on a fresh box that
 * becomes the row's element's parent; getComputedStyle of that parent is printed as 'par=' so a
 * value that depends on the tree can be read from the same line. */
var CASES = [
  @@entries@@
];
var out = [];
for (var i = 0; i < CASES.length; i++) {
  var prop = CASES[i][1], value = CASES[i][2], decl = CASES[i][3];
  var host = document.getElementById('box');
  var parText = '';
  var d;
  if (decl) {
    var p = document.createElement('div');
    p.style.cssText = decl;
    document.getElementById('pbox').appendChild(p);
    d = document.createElement('div');
    p.appendChild(d);
    parText = '|par=' + getComputedStyle(p).getPropertyValue(
      decl.indexOf('font-weight') === 0 ? 'font-weight' : 'border-block-style');
  } else {
    d = document.createElement('div');
    host.appendChild(d);
  }
  d.style.setProperty(prop, value);
  void d.offsetHeight;
  var sup = 'ERR';
  try { sup = CSS.supports(prop, value) ? '1' : '0'; } catch (e) {}
  out.push(CASES[i][0] + '|' + prop + '|' + value + '|sup=' + sup + '|n=' + d.style.length
    + '|echo=' + d.style.getPropertyValue(prop)
    + '|comp=' + getComputedStyle(d).getPropertyValue(prop) + parText);
}
text = out.join('\\n');
document.getElementById('report').textContent = text;
console.log(text);
</script></body></html>
"""


if __name__ == '__main__':
    path = sys.argv[1] if len(sys.argv) > 1 else 'snapshots/_probe_b294_followups.html'
    open(path, 'w').write(build())
    print(path, 'rows', len(CASES))
