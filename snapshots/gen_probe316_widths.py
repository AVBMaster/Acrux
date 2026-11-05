def main():
    import json, os
    CASES = [
        ("border", "2px 8px 12px 4px solid rgb(51,51,204)"),
        ("border", "2px 8px 12px 4px rgb(51,51,204)"),
        ("border", "2px 8px 12px 4px solid"),
        ("border", "2px 8px 12px 4px"),
        ("border", "1px 2px"),
        ("border", "1px 2px solid"),
        ("border", "1px 2px solid red"),
        ("border", "1px 2px 3px solid red"),
        ("border", "1px 2px 3px 4px solid red"),
        ("border", "1px 2px 3px 4px 5px solid red"),
        ("border", "thin thick solid red"),
        ("border", "thin medium thick solid red"),
        ("border", "1px 2px red"),
        ("border", "1px solid red medium"),
        ("border", "2px 8px 12px 4px hidden"),
        ("border", "2px 8px 12px 4px solid currentcolor"),
        ("border-top", "1px 2px"),
        ("border-top", "1px 2px solid"),
        ("border-block", "1px 2px"),
        ("border-block", "1px 2px solid red"),
        ("border-inline-start", "1px 2px"),
        ("border", "2px 8px 12px 4px solid red, 1px dotted blue"),
    ]

    def page():
        literal = "[" + ",".join(f'[{json.dumps(p)},{json.dumps(v)}]' for p, v in CASES) + "]"
        read = """  if (cases[i][1] !== '~') el.style.setProperty(cases[i][0], cases[i][1]);
  var cs = getComputedStyle(el);
  rows.push(i + '|' + cases[i][0] + '|' + cases[i][1]
      + '|sup=' + (cases[i][1] === '~' ? '-' : (CSS.supports(cases[i][0], cases[i][1]) ? 1 : 0))
      + '|n=' + el.style.length
      + '|echo=' + el.style.getPropertyValue(cases[i][0])
      + '|cssText=' + el.style.cssText
      + '|w=' + cs.borderTopWidth + ',' + cs.borderRightWidth + ','
      + cs.borderBottomWidth + ',' + cs.borderLeftWidth
      + '|s=' + cs.borderTopStyle + ',' + cs.borderRightStyle + ','
      + cs.borderBottomStyle + ',' + cs.borderLeftStyle);"""
        template = """<!doctype html>
<html><head><meta charset="utf-8"><title>b316 widths</title></head>
<body>
<div id="host"></div>
<script>
// b316: how many widths the border shorthands take
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
    with open(os.path.join(HERE, "_probe_b316_widths.html"), "w", encoding="utf-8") as handle:
        handle.write(page())
    print("cases:", len(CASES))


main()
