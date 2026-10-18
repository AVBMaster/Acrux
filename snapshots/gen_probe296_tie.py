#!/usr/bin/env python3
"""Write the b296 tie probe page and the script that measures its reference dump.

The rows are values whose six-significant-digit print sits exactly on its own tie, read on a property
of the box family (which cuts the digits at seven places after the point before it prints) and on one
outside it (which prints the number as it stands), on both surfaces. The page and the reference
script share one row builder, so the two runs always line up row for row.
"""
import os

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

# Ties and near-ties, each written so that a truncating rule, a rounding rule and a rule that counts
# decimals answer different strings.
LONG = ["0.1234505", "0.1234515", "0.1234525", "0.1234535", "0.1234545", "0.1234555", "0.1234565",
        "0.1234575", "0.1234585", "0.1234595", "0.12345650", "1.1234565", "1.1234575", "12.1234565",
        "12.34565", "123.4565", "1234.565", "12345.55", "12345.65", "12345.75", "12345.85",
        "123456.5", "123456.75", "1234567.5", "9.999995", "9999.995", "10000.05", "100000.5",
        "1.0000005", "1.0000015", "2.5000005", "0.0123456", "0.01234565", "0.0199999999",
        "0.001234565", "0.001234555", "0.000123455", "0.000123456789", "0.0001234", "1.5",
        "12345.675"]
# The twin properties of each family carry the same rule, so they are read on a shorter set.
SHORT = ["0.1234525", "0.1234535", "0.1234565", "0.1234575", "0.1234585", "0.1234545", "0.0123456",
         "0.01234565", "0.0199999999", "0.000123455", "0.000123456789", "12345.65", "123456.5",
         "9.999995", "1.0000005", "1.0000015", "2.5000005", "12345.675"]

GROUPS = [["margin-left", LONG], ["padding-left", SHORT], ["shape-margin", SHORT],
          ["letter-spacing", LONG], ["text-indent", SHORT], ["word-spacing", SHORT]]

BUILDER = (
    "const T = [%s].map(function (v) { return v + 'px'; });\n"
    "const S = [%s].map(function (v) { return v + 'px'; });\n"
    "const cases = [];\n"
    "[%s].forEach(function (g) {\n"
    "  g[1].forEach(function (v) { cases.push([g[0], v]); });\n"
    "});\n"
    % ("'" + "','".join(LONG) + "'", "'" + "','".join(SHORT) + "'",
       ",".join(f"['{name}', {'T' if name in ('margin-left', 'letter-spacing') else 'S'}]"
                for name, _ in GROUPS))
)


def read_back():
    """One row: what the two surfaces answer for a value written on a property."""
    return ("  el.style.setProperty(cases[i][0], cases[i][1]);\n"
            "  rows.push(i + '|' + cases[i][0] + '|' + cases[i][1]"
            " + '|echo=' + el.style.getPropertyValue(cases[i][0])"
            " + '|comp=' + getComputedStyle(el).getPropertyValue(cases[i][0]));")


PAGE = """<!doctype html>
<html><head><meta charset="utf-8"><title>b296 tie probe</title></head>
<body>
<div id="host"></div>
<script>
// The six-digit print of a number that sits exactly on its own tie. The reference engine keeps two
// rules apart, and this page walks both on both surfaces: the box family (a margin side, a padding
// side, a shape's margin) first cuts the digits at seven places after the point and prints what is
// left, while everything else prints the number as it stands. The echo and the computed value are
// read on the same row because they are held in different precisions and so can land on opposite
// sides of one tie — 'letter-spacing: 0.1234565px' echoes '0.123456px' and computes '0.123457px'
// (both measured), while the same digits on a margin echo and compute '0.123457px' together.
function build() {
const host = document.createElement('div');
document.body.appendChild(host);
const rows = [];
%s
for (let i = 0; i < cases.length; i++) {
  const el = document.createElement('div');
  host.appendChild(el);
%s
}
console.log(rows.join('\\n'));
}
build();
</script>
</body></html>
""" % (BUILDER, read_back())

REFERENCE = """() => {
const host = document.createElement('div');
document.body.appendChild(host);
const rows = [];
%s
for (let i = 0; i < cases.length; i++) {
  const el = document.createElement('div');
  host.appendChild(el);
%s
}
host.remove();
return rows.join('\\n');
}""" % (BUILDER, read_back())


def main():
    with open(os.path.join(ROOT, "snapshots", "_probe_b296_tie.html"), "w", encoding="utf-8") as f:
        f.write(PAGE)
    with open(os.path.join(ROOT, "snapshots", "_b296_tie_ref_script.js"), "w", encoding="utf-8") as f:
        f.write(REFERENCE)
    count = sum(len(v) for _, v in GROUPS)
    print(f"{count} rows -> snapshots/_probe_b296_tie.html + snapshots/_b296_tie_ref_script.js")


if __name__ == "__main__":
    main()
