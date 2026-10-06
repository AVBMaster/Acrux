"""Dump the ::before text of a page, row by row, so it can be compared with Edge's
accessibility text. Usage: python counter_rows.py <html> [out]"""
import collections, io, re, subprocess, sys

import _acrux_cli as cli

ENV = cli.environment()
page = sys.argv[1]
out = sys.argv[2] if len(sys.argv) > 2 else "snapshots/out/_counter_rows.txt"

raw = subprocess.run(cli.command(["--textops", page]), capture_output=True, env=ENV).stdout
raw = raw.decode("utf-8", "replace")
rows = collections.OrderedDict()
for m in re.finditer(r"\[text\] '(.*)' x=([\d.]+) y=([\d.]+)", raw):
    rows.setdefault(round(float(m.group(3)), 1), []).append((float(m.group(2)), m.group(1)))

lines = []
for y in sorted(rows):
    lines.append("".join(t for _, t in sorted(rows[y])))
io.open(out, "w", encoding="utf-8").write("\n".join(lines) + "\n")
print(out, len(lines))
