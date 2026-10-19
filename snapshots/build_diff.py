#!/usr/bin/env python3
"""Diff two builds' answers to the same probe page.

The marker gate only sees pixels; the value-text probes see grammar and printer answers, and a round
that changes what a declaration is stored as can move a row without moving a single pixel. Both
binaries run the same page and the two JS dumps are compared line by line, so every movement this
round caused is listed and can be read against the measured reference.
"""
import os, subprocess, sys

PAGES = sys.argv[1:] or [
    "b294_micro", "b294_followups", "b294_keyword_candidates", "b294_property_bogus_sweep",
    "b294_shorthand_names", "b294_value_matrix", "b294_inherited_lengths",
    "b294_font_relative_geometry",
    "b295_clip", "b295_biglength", "b295_number", "b295_digits", "b295_comment",
    "b295_printer", "b295_printer2", "b296_tie",
    "b298_mathcomment", "b298_roads",
    "b293_svg_and_legacy", "b292_shape_legacy",
]
ENV = dict(os.environ)
ENV.setdefault("DOTNET_ROOT", os.path.dirname(os.path.realpath(
    subprocess.run(["which", "dotnet"], capture_output=True, text=True).stdout.strip())))


def dump(binary, page):
    try:
        r = subprocess.run([binary, "--dumplayout", f"snapshots/_probe_{page}.html", "800", "600", "1"],
                           capture_output=True, text=True, timeout=900, env=ENV)
    except subprocess.TimeoutExpired:
        print(f"=== {page}: TIMED OUT with {binary}")
        return {}
    out = r.stdout
    i = out.find("[JS] ")
    if i < 0:
        return {}
    t = out[i + 5:]
    k = t.find("<HTML>")
    if k > 0:
        t = t[:k]
    rows = {}
    for line in t.splitlines():
        line = line.strip()
        if line.count("|") >= 4:
            rows[line.split("|", 1)[0]] = line
    return rows


total = 0
for page in PAGES:
    path = f"snapshots/_probe_{page}.html"
    if not os.path.exists(path):
        continue
    a = dump("/tmp/apprun_base/Acrux", page)
    b = dump("/tmp/apprun/Acrux", page)
    moved = [k for k in sorted(set(a) | set(b), key=lambda x: int(x) if x.isdigit() else 0)
             if a.get(k) != b.get(k)]
    total += len(moved)
    print(f"=== {page}: {len(moved)} moved rows (base {len(a)} / new {len(b)})")
    for k in moved:
        print(f"  base: {a.get(k)}")
        print(f"  new : {b.get(k)}")
print(f"TOTAL MOVED: {total}")
