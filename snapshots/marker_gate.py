"""Re-render the snapshot pages whose refs are trusted and diff them.

Used as the regression gate whenever layout or text metrics change. Refs were
baked at different device-pixel ratios, so each one is probed at the ratios this
project has used and the first exact match wins; a mismatch there is a real diff.
Usage: python marker_gate.py 166 179 | python marker_gate.py --all
"""
import glob, os, re, struct, subprocess, sys

EXE = "./Acrux/bin/Debug/net10.0-windows/Acrux.exe"
TMP = "snapshots/out/_gate.png"
DPIS = (1.25, 1.0, 2.0)


def dims(path):
    with open(path, "rb") as f:
        head = f.read(33)
    return struct.unpack(">II", head[16:24])


def run(args):
    return subprocess.run([EXE] + args, capture_output=True, text=True, timeout=300).stdout


def parse(ref):
    """ref-b166-multicol-flow-width.png -> ('166', '-multicol-flow-width')."""
    m = re.match(r"ref-b(\d+)(.*)\.png$", os.path.basename(ref))
    return (m.group(1), m.group(2)) if m else (None, None)


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


refs = sorted(glob.glob("snapshots/out/ref-b*.png"))
if len(sys.argv) > 1 and sys.argv[1] == "--all":
    selected = refs
else:
    wanted = set(sys.argv[1:])
    # A bare number matches that batch exactly: matching by prefix paired
    # ref-b22.png with the b2 page and reported a bogus 36% diff.
    selected = [r for r in refs if parse(r)[0] in wanted]

for ref in selected:
    num, suffix = parse(ref)
    page = page_for(num, suffix)
    if not page:
        print(f"b{num}{suffix}: no page for {os.path.basename(ref)}", flush=True)
        continue
    w, h = dims(ref)
    best = None
    for dpi in DPIS:
        css_w, css_h = (w * 100 // 125, h * 100 // 125) if dpi == 1.25 else (w, h)
        if dpi == 2.0:
            css_w, css_h = w // 2, h // 2
        if css_w <= 0 or css_h <= 0:
            continue
        run(["--snapshot", page, TMP, str(css_w), str(css_h), str(dpi)])
        m = re.search(r"(\d+)/(\d+) px differ \(([\d.]+)%\)", run(["--diff", ref, TMP]))
        if not m:
            continue
        pct = float(m.group(3))
        if best is None or pct < best[0]:
            best = (pct, dpi, m.group(0))
        if pct == 0.0:
            break
    print(f"b{num}{suffix}: " + (f"{best[2]} @dpi{best[1]}" if best else "no diff line"), flush=True)
