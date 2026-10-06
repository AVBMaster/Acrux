"""Bake the Linux regression baseline.

Refs under snapshots/out/ref-b*.png were baked on Windows; Skia's text/border
rasterization differs per platform, so they cannot gate a Linux build. This
script renders every batch page on the current platform at dpi 1.0 into
snapshots/out/linux-ref-b*.png — a regression sentinel for Linux runs of
marker_gate.py (NOT a correctness proof; correctness stays with the Edge
measurements recorded in docs/CSS-HANDOFF.md).
"""
import _acrux_cli as cli
import glob, os, re, struct, subprocess, sys

OUT = "snapshots/out"
ENV = cli.environment()


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
    if not cands:
        return None
    if suffix:
        for c in cands:
            if suffix.strip("-") in os.path.basename(c):
                return c
    return cands[0]


def parse(name):
    m = re.match(r"ref-b(\d+)(.*)\.png$", name)
    return (m.group(1), m.group(2)) if m else (None, None)


only = set(sys.argv[1:])
refs = sorted(glob.glob(f"{OUT}/ref-b*.png"))
done = skipped = failed = 0
for ref in refs:
    num, suffix = parse(os.path.basename(ref))
    if only and num not in only:
        continue
    out = f"{OUT}/linux-ref-b{num}{suffix}.png"
    page = page_for(num, suffix)
    if not page:
        print(f"b{num}{suffix}: no page", flush=True)
        skipped += 1
        continue
    w, h = dims(ref)
    r = subprocess.run(cli.command(["--snapshot", page, out, str(w), str(h), "1.0"]),
                       capture_output=True, text=True, timeout=300, env=ENV)
    if os.path.exists(out) and os.path.getsize(out) > 0:
        done += 1
        print(f"baked {os.path.basename(out)} ({w}x{h})", flush=True)
    else:
        failed += 1
        print(f"FAILED b{num}{suffix}: {r.stdout[-200:]} {r.stderr[-200:]}", flush=True)
print(f"done={done} skipped={skipped} failed={failed}")
