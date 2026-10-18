"""Build the b294 value-matrix probe from the names a 'bogus' sweep found ungrammared.

One generic set of values is asked of every name, so the accept/refuse profile of a property is a
measurement rather than a guess: which of a keyword, a length, a number, a percentage, a colour, a
time, an image, a pair and a list it takes is what a grammar arm has to reproduce.

Usage: python3 snapshots/gen_probe294_matrix.py <diff-file> <out-html>
"""
import json
import re
import sys

diff_path, out_path = sys.argv[1], sys.argv[2]
names = sorted(set(re.findall(r'^row \d+ key ([^ |]+) \|', open(diff_path, encoding='utf-8').read(), re.M)))
VALUES = ["bogus-zzz", "auto", "none", "normal", "1px", "1", "50%", "red", "2s",
          "url(a.png)", "1px 2px", "1px, 2px", "calc(1px + 2px)", "thin", "0"]

rows = []
i = 0
for name in names:
    for value in VALUES:
        rows.append((i, name, value))
        i += 1

html = """<!DOCTYPE html>
<html lang="en"><head><meta charset="utf-8"><title>b294 probe D &#8212; the value set each ungrammared property actually takes</title>
<style>body { font: 14px/18px monospace; color: rgb(9,9,9); margin: 8px; background: #fff; }
#box { width: 200px; } #box > div { height: 20px; }</style></head><body>
<div id="box"></div><pre id="report"></pre>
<script>
/* One generic set of values asked of every name the sweep found without a grammar: the profile of
 * yes and no is what the property's arm has to reproduce, and nothing here is derived from a rule. */
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
    + '|echo=' + d.style.getPropertyValue(prop));
}
text = out.join('\\n');
document.getElementById('report').textContent = text;
console.log(text);
</script></body></html>
"""
body = json.dumps([[r[0], r[1], r[2]] for r in rows], ensure_ascii=False)
open(out_path, 'w', encoding='utf-8').write(html.replace('@@CASES@@', body))
print(out_path, 'rows', len(rows), 'names', len(names))
