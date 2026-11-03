#!/usr/bin/env python3
"""Write the two b308 sweep probes and the scripts that measure their reference dumps.

Two families the engine has never carried a grammar for, asked on the same three surfaces.

'P1' is the graphics set: the paint a shape carries (fill, stroke and their opacity, the dash
list, the line caps and joins), the rendering hints (shape-, text-, image-rendering), the two
paint-order words, a marker's reference, and the geometry attributes CSS 2-level SVG exposes as
properties. Most of these this engine stores as an unparsed string at best, and a wrong keyword
here is a wrong pixel: 'stroke-linecap: triangle' is not a value, and 'crispEdges' is one word
that no CSS grammar reads with a capital E.

'P2' is the words a page writes about scrolling, breaking and the prefixed engine: the scroll-snap
pair and its axis words, the scroll padding and margin lists, the scrollbar colour and width, the
overscroll triple, the text-decoration family with its legacy skip words, the line-breaking set,
the ruby trio, the printing pair, and the '-webkit-' seats that a page still writes today. Each row
asks the grammar, then reads the CSSOM echo and the computed value back.

A row is 'index|group|property|value|sup=..|echo=..|comp=..'. 'sup=' is what the property's own
grammar answers, 'echo=' is the specified value the CSSOM prints and 'comp=' is what the computed
style says — a family is only finished when all three agree with the reference.
"""
import os

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

# ---------------------------------------------------------------- P1: the graphics surface
SVG = [
    # The paint a shape carries.
    ("fill", "#f00"), ("fill", "rgb(1,2,3)"), ("fill", "currentcolor"), ("fill", "NONE"),
    ("fill", "url(#a)"), ("fill", "url(#a) red"), ("fill", "transparent"), ("fill", "none red"),
    ("fill", "#ff0000ff"), ("fill", "hsl(120 50% 50%)"),
    ("fill-opacity", "0.5"), ("fill-opacity", "50%"), ("fill-opacity", "-1"), ("fill-opacity", "2"),
    ("fill-opacity", "0"), ("fill-opacity", "1"),
    ("fill-rule", "nonzero"), ("fill-rule", "evenodd"), ("fill-rule", "evenodd nonzero"),
    ("fill-rule", "inherit"), ("fill-rule", "winding"),
    ("stroke", "#00ff00"), ("stroke", "url(#g)"), ("stroke", "none"), ("stroke", "red blue"),
    ("stroke-width", "2"), ("stroke-width", "2px"), ("stroke-width", "50%"),
    ("stroke-width", "thin"), ("stroke-width", "-1"), ("stroke-width", "0"),
    ("stroke-linecap", "butt"), ("stroke-linecap", "round"), ("stroke-linecap", "square"),
    ("stroke-linecap", "triangle"), ("stroke-linecap", "BUTT"),
    ("stroke-linejoin", "miter"), ("stroke-linejoin", "round"), ("stroke-linejoin", "bevel"),
    ("stroke-linejoin", "arcs"), ("stroke-linejoin", "miter-clip"), ("stroke-linejoin", "ortho"),
    ("stroke-miterlimit", "4"), ("stroke-miterlimit", "1"), ("stroke-miterlimit", "0"),
    ("stroke-miterlimit", "1000"), ("stroke-miterlimit", "4px"),
    ("stroke-dasharray", "none"), ("stroke-dasharray", "5"), ("stroke-dasharray", "5 3"),
    ("stroke-dasharray", "5px,3px"), ("stroke-dasharray", "5 3 2"), ("stroke-dasharray", "100%"),
    ("stroke-dasharray", "-5"), ("stroke-dasharray", "5, 3, 2"), ("stroke-dasharray", "none 5"),
    ("stroke-dashoffset", "4"), ("stroke-dashoffset", "4px"), ("stroke-dashoffset", "50%"),
    ("stroke-dashoffset", "-4"), ("stroke-dashoffset", "0"),
    ("stroke-opacity", "0.4"), ("stroke-opacity", "40%"), ("stroke-opacity", "1.5"),
    # The rendering hints, each its own word list.
    ("shape-rendering", "auto"), ("shape-rendering", "crispEdges"), ("shape-rendering", "optimizeSpeed"),
    ("shape-rendering", "geometricPrecision"), ("shape-rendering", "smooth"),
    ("shape-rendering", "crispedges"),
    ("text-rendering", "auto"), ("text-rendering", "optimizeLegibility"),
    ("text-rendering", "geometricPrecision"), ("text-rendering", "optimizeSpeed"),
    ("text-rendering", "optimizeQuality"),
    ("image-rendering", "auto"), ("image-rendering", "crisp-edges"), ("image-rendering", "pixelated"),
    ("image-rendering", "smooth"), ("image-rendering", "high-quality"), ("image-rendering", "low-quality"),
    ("image-rendering", "-webkit-optimize-contrast"), ("image-rendering", "optimizespeed"),
    ("image-rendering", "optimizequality"), ("image-rendering", "crispEdges"),
    ("color-interpolation-filters", "auto"), ("color-interpolation-filters", "sRGB"),
    ("color-interpolation-filters", "linearRGB"), ("color-interpolation-filters", "srgb"),
    ("color-interpolation-filters", "rgb"),
    # Paint order, markers, clip.
    ("paint-order", "normal"), ("paint-order", "fill"), ("paint-order", "fill stroke"),
    ("paint-order", "stroke markers fill"), ("paint-order", "markers"), ("paint-order", "normal fill"),
    ("paint-order", "fill fill"), ("paint-order", "markup"),
    ("clip-rule", "evenodd"), ("clip-rule", "nonzero"), ("clip-rule", "inherit"),
    ("marker-start", "url(#a)"), ("marker-start", "none"), ("marker-start", "red"),
    ("marker-mid", "url(#a)"), ("marker-end", "none"),
    ("marker-width", "10"), ("marker-height", "10px"), ("marker-unit", "userSpaceOnUse"),
    ("flood-color", "red"), ("flood-color", "currentColor"), ("flood-opacity", "0.5"),
    ("flood-opacity", "50%"), ("stop-color", "#fff"), ("stop-color", "transparent"),
    ("stop-opacity", "1"), ("stop-opacity", "50%"),
    ("lighting-color", "red"), ("color-interpolation", "auto"),
    # The baselines a text in SVG is placed on.
    ("dominant-baseline", "auto"), ("dominant-baseline", "middle"), ("dominant-baseline", "central"),
    ("dominant-baseline", "hanging"), ("dominant-baseline", "text-bottom"),
    ("dominant-baseline", "ideographic"), ("dominant-baseline", "alphabetic"),
    ("alignment-baseline", "auto"), ("alignment-baseline", "baseline"),
    ("alignment-baseline", "before-edge"), ("alignment-baseline", "central"),
    ("baseline-shift", "2"), ("baseline-shift", "super"), ("baseline-shift", "50%"),
    ("baseline-shift", "2px"), ("baseline-shift", "sub"),
    # The geometry attributes CSS exposes.
    ("cx", "10"), ("cx", "10px"), ("cx", "50%"), ("cy", "10px"), ("r", "5"), ("r", "auto"),
    ("x", "1"), ("y", "1px"), ("width", "10px"), ("rx", "5"),
    ("d", "path('M 0 0')"), ("d", "url(#p)"), ("d", "none"),
    ("offset-distance", "10px"), ("offset-rotate", "auto"),
]

# ---------------------------------------------------------------- P2: scrolling, breaking, prefixes
WORDS = [
    # The scroll family.
    ("scroll-behavior", "auto"), ("scroll-behavior", "smooth"), ("scroll-behavior", "instant"),
    ("scroll-snap-type", "none"), ("scroll-snap-type", "x mandatory"), ("scroll-snap-type", "y proximity"),
    ("scroll-snap-type", "both mandatory"), ("scroll-snap-type", "block mandatory"),
    ("scroll-snap-type", "inline proximity"), ("scroll-snap-type", "x"), ("scroll-snap-type", "mandatory"),
    ("scroll-snap-type", "x strict"), ("scroll-snap-type", "none mandatory"),
    ("scroll-snap-align", "none"), ("scroll-snap-align", "start"), ("scroll-snap-align", "end"),
    ("scroll-snap-align", "center"), ("scroll-snap-align", "start end"), ("scroll-snap-align", "center center"),
    ("scroll-snap-align", "x start"), ("scroll-snap-align", "y end"), ("scroll-snap-align", "block start"),
    ("scroll-snap-align", "both mandatory"),
    ("scroll-snap-stop", "normal"), ("scroll-snap-stop", "always"), ("scroll-snap-stop", "never"),
    ("scroll-padding", "10px"), ("scroll-padding", "10px 20px"), ("scroll-padding", "auto"),
    ("scroll-padding", "10px auto 20px 30px"), ("scroll-padding", "10% 20%"),
    ("scroll-padding-left", "10px"), ("scroll-padding-left", "10%"), ("scroll-padding-left", "auto"),
    ("scroll-padding-block-start", "10px"),
    ("scroll-margin", "10px"), ("scroll-margin", "-20px"), ("scroll-margin", "auto"),
    ("scroll-margin", "10px 20px 30px 40px"), ("scroll-margin-left", "1rem"),
    ("scrollbar-width", "auto"), ("scrollbar-width", "thin"), ("scrollbar-width", "none"),
    ("scrollbar-width", "bare"),
    ("scrollbar-color", "auto"), ("scrollbar-color", "red"), ("scrollbar-color", "red blue"),
    ("scrollbar-color", "transparent"), ("scrollbar-color", "#fff"),
    ("overscroll-behavior", "auto"), ("overscroll-behavior", "contain"), ("overscroll-behavior", "none"),
    ("overscroll-behavior", "pan-contained"), ("overscroll-behavior", "contain none"),
    ("overscroll-behavior", "auto contain"), ("overscroll-behavior-x", "contain"),
    ("overscroll-behavior-inline", "none"),
    # How a box's content is fitted and how the box itself is promised to change.
    ("object-fit", "fill"), ("object-fit", "contain"), ("object-fit", "cover"), ("object-fit", "none"),
    ("object-fit", "scale-down"), ("object-fit", "smash"),
    ("object-position", "50% 50%"), ("object-position", "left top"), ("object-position", "10px 20px"),
    ("object-position", "center"), ("object-position", "top right"),
    ("will-change", "auto"), ("will-change", "none"), ("will-change", "scroll-position"),
    ("will-change", "contents"), ("will-change", "transform"), ("will-change", "transform, opacity"),
    ("will-change", "width"), ("will-change", "all"), ("will-change", "translate"),
    ("backface-visibility", "visible"), ("backface-visibility", "hidden"),
    ("transform-style", "flat"), ("transform-style", "preserve-3d"), ("transform-style", "preserve3d"),
    ("isolation", "auto"), ("isolation", "isolate"), ("isolation", "isolated"),
    # The blend set, which is one word list twice over.
    ("mix-blend-mode", "normal"), ("mix-blend-mode", "multiply"), ("mix-blend-mode", "screen"),
    ("mix-blend-mode", "overlay"), ("mix-blend-mode", "darken"), ("mix-blend-mode", "lighten"),
    ("mix-blend-mode", "color-dodge"), ("mix-blend-mode", "color-burn"), ("mix-blend-mode", "hard-light"),
    ("mix-blend-mode", "soft-light"), ("mix-blend-mode", "difference"), ("mix-blend-mode", "exclusion"),
    ("mix-blend-mode", "hue"), ("mix-blend-mode", "saturation"), ("mix-blend-mode", "color"),
    ("mix-blend-mode", "luminosity"), ("mix-blend-mode", "plus-lighter"), ("mix-blend-mode", "lighter"),
    ("mix-blend-mode", "multiply, screen"), ("mix-blend-mode", "contrast"),
    ("background-blend-mode", "normal"), ("background-blend-mode", "multiply, screen"),
    ("background-blend-mode", "plus-lighter"),
    ("pointer-events", "auto"), ("pointer-events", "none"), ("pointer-events", "visiblePainted"),
    ("pointer-events", "visibleFill"), ("pointer-events", "painted"), ("pointer-events", "fill"),
    ("pointer-events", "stroke"), ("pointer-events", "all"), ("pointer-events", "visible"),
    ("appearance", "auto"), ("appearance", "none"), ("appearance", "textfield"), ("appearance", "button"),
    ("appearance", "checkbox"), ("appearance", "searchfield"), ("appearance", "textarea"),
    ("appearance", "menulist"), ("appearance", "listbox"), ("appearance", "menulist-button"),
    ("appearance", "push-button"), ("appearance", "slider"), ("appearance", "meter"),
    ("appearance", "progress-bar"), ("appearance", "-webkit-menulist"),
    # The decoration family and its legacy words.
    ("text-decoration-thickness", "auto"), ("text-decoration-thickness", "from-font"),
    ("text-decoration-thickness", "2px"), ("text-decoration-thickness", "10%"),
    ("text-decoration-thickness", "thin"), ("text-decoration-thickness", "0.1em"),
    ("text-decoration-style", "solid"), ("text-decoration-style", "double"),
    ("text-decoration-style", "dotted"), ("text-decoration-style", "dashed"),
    ("text-decoration-style", "wavy"), ("text-decoration-style", "wavy double"),
    ("text-decoration-line", "underline"), ("text-decoration-line", "overline"),
    ("text-decoration-line", "line-through"), ("text-decoration-line", "blink"),
    ("text-decoration-line", "underline overline line-through blink"),
    ("text-decoration-line", "none"), ("text-decoration-line", "underline none"),
    ("text-decoration-skip-ink", "auto"), ("text-decoration-skip-ink", "none"),
    ("text-decoration-skip-ink", "all"),
    ("text-decoration-skip", "none"), ("text-decoration-skip", "objects"),
    ("text-decoration-skip", "spaces"), ("text-decoration-skip", "leading-spaces"),
    ("text-decoration-skip", "trailing-spaces"), ("text-decoration-skip", "all"),
    ("text-decoration-skip", "edges"), ("text-decoration-skip", "box-decoration"),
    ("text-decoration-break", "slice"), ("text-decoration-break", "clone"),
    ("text-decoration-break", "contains"),
    ("text-decoration", "underline red"), ("text-decoration", "wavy"), ("text-decoration", "red underline wavy"),
    ("text-decoration", "underline from-font"), ("text-decoration", "underline solid red"),
    # The line-breaking set.
    ("line-break", "auto"), ("line-break", "loose"), ("line-break", "normal"), ("line-break", "strict"),
    ("line-break", "anywhere"), ("line-break", "easy"),
    ("hyphens", "none"), ("hyphens", "manual"), ("hyphens", "auto"), ("hyphens", "inter-word"),
    ("overflow-wrap", "normal"), ("overflow-wrap", "break-word"), ("overflow-wrap", "anywhere"),
    ("overflow-wrap", "any"),
    ("word-break", "normal"), ("word-break", "break-all"), ("word-break", "keep-all"),
    ("word-break", "break-word"), ("word-break", "auto-phrase"), ("word-break", "standard"),
    ("tab-size", "4"), ("tab-size", "2"), ("tab-size", "0"), ("tab-size", "10px"),
    ("tab-size", "4em"), ("tab-size", "-1"),
    ("text-justify", "auto"), ("text-justify", "none"), ("text-justify", "inter-word"),
    ("text-justify", "distribute"), ("text-justify", "inter-ideograph"),
    ("ruby-position", "over"), ("ruby-position", "under"), ("ruby-position", "inter-rt"),
    ("ruby-position", "alternate over"), ("ruby-position", "outside"),
    ("ruby-align", "start"), ("ruby-align", "center"), ("ruby-align", "space-between"),
    ("ruby-align", "space-around"), ("ruby-align", "auto"),
    ("ruby-merge", "separate"), ("ruby-merge", "collapse"), ("ruby-merge", "auto"),
    ("text-orientation", "mixed"), ("text-orientation", "upright"), ("text-orientation", "sideways"),
    ("text-orientation", "sideways-right"),
    ("text-combine-upright", "none"), ("text-combine-upright", "all"),
    ("text-combine-upright", "digits 2"),
    ("initial-letter", "normal"), ("initial-letter", "2"), ("initial-letter", "2.5"),
    ("initial-letter", "drop 2"), ("initial-letter", "raise 3"),
    # The printing pair and the colour-adjust seats.
    ("widows", "1"), ("widows", "2"), ("widows", "auto"), ("widows", "0"), ("widows", "-1"),
    ("widows", "1.5"), ("orphans", "1"), ("orphans", "auto"), ("orphans", "0"),
    ("page", "auto"), ("page", "foo"), ("page", "inherit"),
    ("print-color-adjust", "economy"), ("print-color-adjust", "exact"), ("print-color-adjust", "auto"),
    ("color-adjust", "economy"), ("color-adjust", "exact"),
    ("-webkit-print-color-adjust", "exact"), ("-webkit-print-color-adjust", "economy"),
    ("forced-color-adjust", "auto"), ("forced-color-adjust", "none"),
    ("forced-color-adjust", "preserve-parent-color"),
    # The prefixed seats a page still writes.
    ("-webkit-text-stroke-width", "1px"), ("-webkit-text-stroke-width", "1"),
    ("-webkit-text-stroke-width", "thin"), ("-webkit-text-stroke-color", "red"),
    ("-webkit-text-fill-color", "red"), ("-webkit-text-fill-color", "transparent"),
    ("-webkit-text-security", "none"), ("-webkit-text-security", "disc"),
    ("-webkit-text-security", "circle"), ("-webkit-text-security", "square"),
    ("-webkit-text-size-adjust", "auto"), ("-webkit-text-size-adjust", "none"),
    ("-webkit-text-size-adjust", "200%"), ("-webkit-text-size-adjust", "12px"),
    ("-webkit-tap-highlight-color", "transparent"), ("-webkit-tap-highlight-color", "red"),
    ("-webkit-box-orient", "horizontal"), ("-webkit-box-orient", "vertical"),
    ("-webkit-box-orient", "inline-axis"), ("-webkit-box-orient", "block-axis"),
    ("-webkit-box-align", "start"), ("-webkit-box-align", "center"), ("-webkit-box-align", "end"),
    ("-webkit-box-align", "baseline"), ("-webkit-box-align", "stretch"),
    ("-webkit-box-pack", "start"), ("-webkit-box-pack", "center"), ("-webkit-box-pack", "end"),
    ("-webkit-box-pack", "justify"),
    ("-webkit-box-flex", "1"), ("-webkit-box-flex", "1.5"), ("-webkit-box-flex", "0"),
    ("-webkit-box-flex-group", "1"), ("-webkit-box-flex-group", "0"),
    ("-webkit-line-clamp", "1"), ("-webkit-line-clamp", "none"), ("-webkit-line-clamp", "2.5"),
    ("-webkit-line-clamp", "0"), ("-webkit-line-clamp", "-1"),
    ("-webkit-font-smoothing", "auto"), ("-webkit-font-smoothing", "none"),
    ("-webkit-font-smoothing", "antialiased"), ("-webkit-font-smoothing", "subpixel-antialiased"),
    ("-webkit-user-select", "auto"), ("-webkit-user-select", "none"), ("-webkit-user-select", "text"),
    ("-webkit-user-select", "element"), ("-webkit-user-select", "select-all"),
    ("-webkit-user-select", "contain"),
    ("user-select", "auto"), ("user-select", "none"), ("user-select", "text"),
    ("user-select", "all"), ("user-select", "contain"), ("user-select", "elements"),
    ("-webkit-user-modify", "read-only"), ("-webkit-user-modify", "read-write"),
    ("-webkit-user-modify", "read-write-plaintext-only"),
    ("-webkit-overflow-scrolling", "auto"), ("-webkit-overflow-scrolling", "touch"),
    ("-webkit-rtl-ordering", "logical"), ("-webkit-rtl-ordering", "numeric"),
    ("-webkit-text-combine", "none"), ("-webkit-text-combine", "horizontal"),
    ("-webkit-box-reflect", "below"), ("-webkit-box-reflect", "above 10px"),
    ("-webkit-box-reflect", "none"),
    ("caret-shape", "auto"), ("caret-shape", "bar"),
    ("text-wrap", "wrap"), ("text-wrap", "nowrap balance"), ("text-wrap", "stable"),
    ("text-wrap", "wrap 3"), ("text-wrap", "auto 2 12px"),
]

SETS = {"svg": SVG, "words": WORDS}

# The seats the sweeps above showed the engine does not answer for, asked again with the two
# questions an implementation needs before it writes anything: what the property says when the
# page wrote NOTHING at all (its initial value, which the computed surface still prints), and which
# shapes around the accepted one the grammar refuses. '~' is read as 'no declaration'.
SEEDS = [
    ("cx", "~", "init"), ("cy", "~", "init"), ("r", "~", "init"), ("rx", "~", "init"),
    ("ry", "~", "init"), ("x", "~", "init"), ("y", "~", "init"), ("d", "~", "init"),
    ("-webkit-rtl-ordering", "~", "init"), ("-webkit-text-combine", "~", "init"),
    ("initial-letter", "~", "init"), ("forced-color-adjust", "~", "init"),
    ("mix-blend-mode", "~", "init"), ("image-rendering", "~", "init"),
    ("text-decoration-line", "~", "init"), ("text-decoration-skip-ink", "~", "init"),
    # The geometry family's other shapes.
    ("cx", "5 5", "geo"), ("cx", "auto", "geo"), ("cx", "-5", "geo"), ("cx", "calc(1px + 1%)", "geo"),
    ("cx", "1em", "geo"), ("cx", "1", "geo"), ("cx", "1vw", "geo"), ("cx", "inherit", "geo"),
    ("rx", "5px 3px", "geo"), ("ry", "3", "geo"), ("r", "5%", "geo"), ("r", "1em", "geo"),
    ("x", "50%", "geo"), ("y", "auto", "geo"), ("cx", "0", "geo"),
    ("d", "path('M0 0')", "d"), ("d", "path('')", "d"), ("d", "path('M 0 0 L 1 1')", "d"),
    ("d", "url(#p) path('M0')", "d"), ("d", "normal", "d"), ("d", "inherit", "d"),
    ("d", "path('M 0 0') none", "d"),
    # The words the sweeps refused, in the company they are written with.
    ("initial-letter", "drop 2.5", "word"), ("initial-letter", "raise 0", "word"),
    ("initial-letter", "2 drop", "word"), ("initial-letter", "drop", "word"),
    ("initial-letter", "drop 2 raise 3", "word"), ("initial-letter", "normal", "word"),
    ("forced-color-adjust", "preserve-parent-color auto", "word"),
    ("mix-blend-mode", "plus-lighter multiply", "word"),
    ("background-blend-mode", "plus-lighter", "word"),
    ("text-decoration-line", "blink underline", "word"),
    ("text-decoration-line", "blink blink", "word"),
    ("text-decoration-line", "underline line-through", "word"),
    ("text-decoration", "underline overline line-through blink", "word"),
    ("image-rendering", "-webkit-optimize-contrast pixelated", "word"),
    ("image-rendering", "optimizecontrast", "word"),
    ("-webkit-rtl-ordering", "numeric", "word"), ("-webkit-rtl-ordering", "logical numeric", "word"),
    ("-webkit-text-combine", "digits", "word"), ("-webkit-text-combine", "vertical", "word"),
    ("-webkit-text-combine", "horizontal digits 2", "word"),
    # What the accepted value is printed as when the page wrote it the other way round.
    ("cx", "10PX", "spell"), ("r", "5.50", "spell"), ("rx", "5.0", "spell"),
    ("initial-letter", "DROP 2", "spell"), ("d", "NONE", "spell"),
    ("-webkit-text-combine", "NONE", "spell"), ("forced-color-adjust", "PRESERVE-PARENT-COLOR", "spell"),
    ("mix-blend-mode", "PLUS-LIGHTER", "spell"), ("text-decoration-line", "BLINK", "spell"),
    ("image-rendering", "-WEBKIT-OPTIMIZE-CONTRAST", "spell"),
]

# The second question an implementation cannot answer from a declaration alone: what a CHILD of a
# box that wrote the value says, which is the inheritance rule, and the shapes around the accepted
# one. '~' asks the same name of a child whose parent wrote the value in its 'value' column.
INHERIT = ["cx", "cy", "r", "x", "y", "rx", "ry", "d", "-webkit-text-combine",
           "-webkit-rtl-ordering", "image-rendering", "mix-blend-mode", "forced-color-adjust",
           "initial-letter", "text-decoration-line", "stroke-width"]

SEEDS2 = [
    # 'p' rows: the value is written on the parent, read on the child ('~'), and read on the parent.
    ("cx", "5", "inherited"), ("cy", "5", "inherited"), ("r", "5", "inherited"),
    ("x", "5", "inherited"), ("y", "5", "inherited"), ("rx", "5", "inherited"),
    ("ry", "5", "inherited"), ("d", "path('M 0 0')", "inherited"),
    ("-webkit-text-combine", "horizontal", "inherited"),
    ("-webkit-rtl-ordering", "logical", "inherited"),
    ("image-rendering", "pixelated", "inherited"),
    ("mix-blend-mode", "multiply", "inherited"),
    ("forced-color-adjust", "none", "inherited"),
    ("initial-letter", "2 drop", "inherited"),
    ("text-decoration-line", "blink underline", "inherited"),
    ("stroke-width", "3", "inherited"),
    # The geometry family's remaining shapes.
    ("rx", "auto", "geo2"), ("ry", "auto", "geo2"), ("rx", "50%", "geo2"),
    ("cx", "1in", "geo2"), ("cx", "1.5", "geo2"), ("cx", "0%", "geo2"), ("cx", "1e2", "geo2"),
    ("cx", "calc(2)", "geo2"), ("cx", "min(1px, 2px)", "geo2"), ("cx", "1ch", "geo2"),
    ("cx", "5 5px", "geo2"), ("cx", "auto 5", "geo2"), ("r", "-1", "geo2"),
    ("d", "path(\"M0\")", "geo2"), ("d", "path(M 0 0)", "geo2"), ("d", "path('a')", "geo2"),
    ("d", "path('M 0 0', none)", "geo2"), ("d", "none none", "geo2"),
    # The word sets, once more.
    ("text-decoration-line", "grammar-error blink", "word2"),
    ("text-decoration-line", "blink none", "word2"),
    ("text-decoration-line", "underline blink overline", "word2"),
    ("text-decoration", "blink red", "word2"),
    ("initial-letter", "raise 2 drop 3", "word2"), ("initial-letter", "0.5 drop", "word2"),
    ("initial-letter", "drop -1", "word2"), ("initial-letter", "2 raise", "word2"),
    ("forced-color-adjust", "preserve-parent-color none", "word2"),
    ("mix-blend-mode", "PLUS-LIGHTER plus-lighter", "word2"),
    ("image-rendering", "-webkit-OPTIMIZE-contrast", "word2"),
    ("-webkit-text-combine", "none horizontal", "word2"),
]

READ = ("  if (cases[i][1] !== '~') el.style.setProperty(cases[i][0], cases[i][1]);\n"
        "  rows.push(i + '|' + cases[i][2] + '|' + cases[i][0] + '|' + cases[i][1]\n"
        "      + '|sup=' + (cases[i][1] === '~' ? '-' : (CSS.supports(cases[i][0], cases[i][1]) ? 1 : 0))\n"
        "      + '|echo=' + el.style.getPropertyValue(cases[i][0])\n"
        "      + '|comp=' + getComputedStyle(el).getPropertyValue(cases[i][0]));")

# The inheritance rows build a parent that writes the value and read the name on both boxes.
READ2 = ("  var p = document.createElement('div'); host.appendChild(p);\n"
         "  var kid = document.createElement('div'); p.appendChild(kid);\n"
         "  p.style.setProperty(cases[i][0], cases[i][1]);\n"
         "  rows.push(i + '|' + cases[i][2] + '|' + cases[i][0] + '|' + cases[i][1]\n"
         "      + '|sup=1'\n"
         "      + '|echo=' + getComputedStyle(kid).getPropertyValue(cases[i][0])\n"
         "      + '|comp=' + getComputedStyle(p).getPropertyValue(cases[i][0]));")


def _js(text):
    return text.replace("\\", "\\\\").replace('"', '\\"')


def _indent(text):
    return "\n".join("  " + line for line in text.split("\n"))


def read_for(tag):
    if tag != "seeds2":
        return READ
    return ("  if (cases[i][2] === 'inherited') {\n" + _indent(READ2)
            + "\n  } else {\n" + _indent(READ) + "\n  }")


def page(tag, cases):
    literal = "[[" + "],[".join(f'"{_js(p)}","{_js(v)}","{_js(g)}"' for p, v, g in cases) + "]]"
    return """<!doctype html>
<html><head><meta charset="utf-8"><title>b308 %s sweep</title></head>
<body>
<div id="host"></div>
<script>
// %s
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
""" % (tag, tag, literal, read_for(tag))


def reference(cases, cases_tag=""):
    literal = "[[" + "],[".join(f'"{_js(p)}","{_js(v)}","{_js(g)}"' for p, v, g in cases) + "]]"
    return """() => {
const host = document.createElement('div');
document.body.appendChild(host);
const rows = [];
const cases = %s;
for (let i = 0; i < cases.length; i++) {
  const el = document.createElement('div');
  host.appendChild(el);
%s
}
host.remove();
return rows.join('\\n');
}""" % (literal, read_for(cases_tag))


def main():
    for tag, cases in (("svg", SVG), ("words", WORDS), ("seeds", SEEDS), ("seeds2", SEEDS2)):
        titled = [c if len(c) == 3 else (c[0], c[1], tag) for c in cases]
        with open(os.path.join(ROOT, "snapshots", f"_probe_b308_{tag}.html"), "w", encoding="utf-8") as f:
            f.write(page(tag, titled))
        with open(os.path.join(ROOT, "snapshots", f"_b308_{tag}_ref_script.js"), "w", encoding="utf-8") as f:
            f.write(reference(titled, tag))
        print(f"{tag}: {len(cases)} rows -> snapshots/_probe_b308_{tag}.html + _b308_{tag}_ref_script.js")


if __name__ == "__main__":
    main()
