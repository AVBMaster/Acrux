"""Re-bake refs from the gate report: each ref is re-rendered at the device-pixel
ratio the gate matched it at, so its pixel size stays stable. The previous image is
kept as old-<name>.png for evidence. Usage: python rebake_from_gate.py [_gate_all.txt]
"""
import glob, os, re, shutil, struct, subprocess, sys

import _acrux_cli as cli

ENV = cli.environment()
SRC = "snapshots/out/" + (sys.argv[1] if len(sys.argv) > 1 else "_gate_all.txt")
LINE = re.compile(r"^b(\d+)([^:]*): .*?(\d+)/(\d+) px differ \(([\d.]+)%\) @dpi([\d.]+)$")


def dims(path):
    with open(path, "rb") as f:
        head = f.read(33)
    return struct.unpack(">II", head[16:24])


def page_for(num, suffix):
    exact = f"snapshots/css-standard-verify{num}{suffix}.html"
    if suffix and os.path.exists(exact):
        return exact
    cands = sorted(glob.glob(f"snapshots/css-standard-verify{num}-*.html"))
    cands += sorted(glob.glob(f"snapshots/css-standard-verify{num}.html"))
    return cands[0] if cands else None


baked = skipped = 0
for raw in open(SRC, encoding="utf-8"):
    m = LINE.match(raw.strip())
    if not m:
        continue
    num, suffix, diff, total, pct, dpi = m.group(1), m.group(2), int(m.group(3)), int(m.group(4)), float(m.group(5)), float(m.group(6))
    if pct == 0.0:
        continue
    ref = f"snapshots/out/ref-b{num}{suffix}.png"
    page = page_for(num, suffix)
    if not page or not os.path.exists(ref):
        print(f"skip b{num}{suffix}: missing page or ref")
        skipped += 1
        continue
    w, h = dims(ref)
    css_w, css_h = (round(w / dpi), round(h / dpi)) if dpi != 1.0 else (w, h)
    shutil.copy(ref, f"snapshots/out/old-b{num}{suffix}.png")
    subprocess.run(cli.command(["--snapshot", page, ref, str(css_w), str(css_h), str(dpi)]),
                   capture_output=True, timeout=300, env=ENV)
    baked += 1
    print(f"b{num}{suffix}: {css_w}x{css_h}@{dpi} ({pct}% -> ref)")
print(f"re-baked {baked}, skipped {skipped}")
