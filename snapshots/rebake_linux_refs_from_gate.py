"""Re-bake the Linux baseline refs from a marker_gate report.

rebake_from_gate.py rewrites snapshots/out/ref-b*.png, which are the Windows
baseline; running it on Linux would overwrite those with Linux rasterization.
This script only touches linux-ref-b*.png: each drifted ref is re-rendered at
its own pixel size (dpi 1.0) and the previous image is kept as
old-linux-ref-b*.png for evidence. Usage:
    python snapshots/rebake_linux_refs_from_gate.py /tmp/gate_final.txt
"""
import _acrux_cli as cli
import glob, os, re, shutil, struct, subprocess, sys

OUT = "snapshots/out"
ENV = cli.environment()
LINE = re.compile(r"^b(\d+)([^:]*): \d+/\d+ px differ \(([\d.]+)%\) @dpi([\d.]+)$")


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


report = sys.argv[1] if len(sys.argv) > 1 else None
if not report or not os.path.exists(report):
    raise SystemExit(f"usage: {sys.argv[0]} <gate report>  (got {report!r})")

drifted = [(n, s) for n, s, pct, _dpi in
           (m.groups() for m in (LINE.match(l.strip()) for l in open(report, encoding="utf-8")) if m)
           if float(pct) != 0.0]

baked = skipped = 0
for num, suffix in drifted:
    ref = f"{OUT}/linux-ref-b{num}{suffix}.png"
    page = page_for(num, suffix)
    if not os.path.exists(ref):
        print(f"skip b{num}{suffix}: no linux ref")
        skipped += 1
        continue
    if not page:
        print(f"skip b{num}{suffix}: no page")
        skipped += 1
        continue
    w, h = dims(ref)
    shutil.copy(ref, f"{OUT}/old-linux-ref-b{num}{suffix}.png")
    subprocess.run(cli.command(["--snapshot", page, ref, str(w), str(h), "1.0"]),
                   capture_output=True, timeout=300, env=ENV)
    print(f"baked {os.path.basename(ref)} ({w}x{h})")
    baked += 1
print(f"re-baked {baked}, skipped {skipped}")
