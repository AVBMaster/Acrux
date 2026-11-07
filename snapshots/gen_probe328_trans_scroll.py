"""Generate the b328 P2 sweep: the transition and animation longhands, the
scroll/overscroll family, and the overflow family — value by value.

Every longhand of the two animated shorthands gets the keyword set its spec
names, a list form, a multi-value list, and a bogus word. The scroll and
overflow families get their keyword sets, the per-axis pairs, and the forms
only the shorthand carries. One fresh element per row (measured in b283: a
reused host answers a dropped declaration with the previous row's value).
"""
import json
import os

CASES = [
    # transition-property: none, all, a name, a list, bogus.
    ("transition-property", "none"), ("transition-property", "all"),
    ("transition-property", "opacity"), ("transition-property", "width"),
    ("transition-property", "opacity, width"), ("transition-property", "bogus-prop"),
    ("transition-property", "opacity, all"), ("transition-property", "background-color"),
    ("transition-property", "COLOR"), ("transition-property", "--custom"),
    # transition-duration / transition-delay: seconds, ms, 0, negative, list, bogus.
    ("transition-duration", "0s"), ("transition-duration", "0.3s"),
    ("transition-duration", "300ms"), ("transition-duration", "1s, 2s"),
    ("transition-duration", "-1s"), ("transition-duration", "1"), ("transition-duration", "bogus"),
    ("transition-delay", "0s"), ("transition-delay", "0.5s"), ("transition-delay", "-0.5s"),
    ("transition-delay", "100ms"), ("transition-delay", "bogus"),
    # transition-timing-function: the standard keywords, steps(), cubic-bezier().
    ("transition-timing-function", "ease"), ("transition-timing-function", "linear"),
    ("transition-timing-function", "ease-in"), ("transition-timing-function", "ease-out"),
    ("transition-timing-function", "ease-in-out"), ("transition-timing-function", "step-start"),
    ("transition-timing-function", "step-end"),
    ("transition-timing-function", "steps(4)"), ("transition-timing-function", "steps(4, end)"),
    ("transition-timing-function", "steps(4, start)"), ("transition-timing-function", "steps(0)"),
    ("transition-timing-function", "steps(-1)"), ("transition-timing-function", "steps(2, bogus)"),
    ("transition-timing-function", "cubic-bezier(0, 0, 1, 1)"),
    ("transition-timing-function", "cubic-bezier(0.5, 0, 0.5, 1)"),
    ("transition-timing-function", "cubic-bezier(0, 2, 1, -1)"),
    ("transition-timing-function", "cubic-bezier(0, 0)"), ("transition-timing-function", "bogus"),
    ("transition-timing-function", "ease, linear"), ("transition-timing-function", "ease linear"),
    # The transition shorthand in its common shapes.
    ("transition", "all 0.3s"), ("transition", "opacity 1s ease-in"),
    ("transition", "width 0.3s, height 0.3s"), ("transition", "none"), ("transition", "bogus"),
    ("transition", "all"), ("transition", "0.3s"),
    # animation-name: none, a name, a list.
    ("animation-name", "none"), ("animation-name", "slide"), ("animation-name", "slide, fade"),
    ("animation-name", "bogus"), ("animation-name", "SLIDE"),
    # animation-duration / delay.
    ("animation-duration", "0s"), ("animation-duration", "1s"), ("animation-duration", "500ms"),
    ("animation-duration", "1s, 2s"), ("animation-duration", "-1s"), ("animation-duration", "bogus"),
    ("animation-delay", "0s"), ("animation-delay", "0.5s"), ("animation-delay", "-1s"),
    ("animation-delay", "bogus"),
    # animation-iteration-count.
    ("animation-iteration-count", "1"), ("animation-iteration-count", "0"),
    ("animation-iteration-count", "infinite"), ("animation-iteration-count", "-1"),
    ("animation-iteration-count", "2.5"), ("animation-iteration-count", "1, infinite"),
    ("animation-iteration-count", "bogus"),
    # animation-direction.
    ("animation-direction", "normal"), ("animation-direction", "reverse"),
    ("animation-direction", "alternate"), ("animation-direction", "alternate-reverse"),
    ("animation-direction", "normal, reverse"), ("animation-direction", "bogus"),
    # animation-fill-mode.
    ("animation-fill-mode", "none"), ("animation-fill-mode", "forwards"),
    ("animation-fill-mode", "backwards"), ("animation-fill-mode", "both"),
    ("animation-fill-mode", "none, forwards"), ("animation-fill-mode", "bogus"),
    # animation-play-state.
    ("animation-play-state", "running"), ("animation-play-state", "paused"),
    ("animation-play-state", "running, paused"), ("animation-play-state", "bogus"),
    # animation-timing-function.
    ("animation-timing-function", "ease"), ("animation-timing-function", "linear"),
    ("animation-timing-function", "steps(3)"), ("animation-timing-function", "bogus"),
    # The animation shorthand.
    ("animation", "none"), ("animation", "slide 1s ease-in forwards"),
    ("animation", "slide 1s 2s infinite alternate both paused"), ("animation", "bogus"),
    ("animation", "1s"),
    # overscroll-behavior / overscroll-behavior-x / -y.
    ("overscroll-behavior", "auto"), ("overscroll-behavior", "contain"),
    ("overscroll-behavior", "none"), ("overscroll-behavior", "auto contain"),
    ("overscroll-behavior", "contain none"), ("overscroll-behavior", "bogus"),
    ("overscroll-behavior-x", "auto"), ("overscroll-behavior-x", "contain"),
    ("overscroll-behavior-x", "none"), ("overscroll-behavior-x", "bogus"),
    ("overscroll-behavior-y", "auto"), ("overscroll-behavior-y", "contain"),
    ("overscroll-behavior-y", "none"), ("overscroll-behavior-y", "bogus"),
    # scroll-behavior.
    ("scroll-behavior", "auto"), ("scroll-behavior", "smooth"), ("scroll-behavior", "bogus"),
    # scroll-snap-type / scroll-snap-align / scroll-snap-stop.
    ("scroll-snap-type", "none"), ("scroll-snap-type", "x mandatory"),
    ("scroll-snap-type", "y proximity"), ("scroll-snap-type", "both mandatory"),
    ("scroll-snap-type", "x"), ("scroll-snap-type", "mandatory"), ("scroll-snap-type", "bogus"),
    ("scroll-snap-align", "none"), ("scroll-snap-align", "start"), ("scroll-snap-align", "end"),
    ("scroll-snap-align", "center"), ("scroll-snap-align", "start end"),
    ("scroll-snap-align", "center center"), ("scroll-snap-align", "bogus"),
    ("scroll-snap-stop", "normal"), ("scroll-snap-stop", "always"), ("scroll-snap-stop", "bogus"),
    # overflow / overflow-x / overflow-y / overflow-clip-margin / overflow-wrap.
    ("overflow", "visible"), ("overflow", "hidden"), ("overflow", "scroll"),
    ("overflow", "auto"), ("overflow", "clip"), ("overflow", "hidden auto"),
    ("overflow", "auto auto"), ("overflow", "bogus"), ("overflow", "hidden hidden hidden"),
    ("overflow-x", "visible"), ("overflow-x", "hidden"), ("overflow-x", "auto"),
    ("overflow-x", "clip"), ("overflow-x", "bogus"),
    ("overflow-y", "visible"), ("overflow-y", "hidden"), ("overflow-y", "auto"),
    ("overflow-y", "scroll"), ("overflow-y", "bogus"),
    ("overflow-clip-margin", "0px"), ("overflow-clip-margin", "10px"),
    ("overflow-clip-margin", "content-box"), ("overflow-clip-margin", "border-box"),
    ("overflow-clip-margin", "10px content-box"), ("overflow-clip-margin", "bogus"),
    ("overflow-wrap", "normal"), ("overflow-wrap", "break-word"), ("overflow-wrap", "anywhere"),
    ("overflow-wrap", "bogus"),
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
<html><head><meta charset="utf-8"><title>b328 P2 sweep</title></head>
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
with open(os.path.join(HERE, "_probe_b328_trans_scroll.html"), "w", encoding="utf-8") as handle:
    handle.write(page())
print("cases:", len(CASES))