"""Re-render every snapshots/css-standard-verifyNN*.html against its ref-bNN*.png and
report the worst pixel diffs. Refs were baked either at dpr 1.25 (device px = CSS px
x 1.25) or at dpr 1, so each page is tried both ways and the better match is kept."""
import glob, os, re, struct, subprocess, sys

EXE = "./Acrux/bin/Debug/net10.0-windows/Acrux.exe"
TMP = "snapshots/out/_sweep.png"


def dims(path):
    with open(path, "rb") as f:
        head = f.read(33)
    return struct.unpack(">II", head[16:24])


def pick_page(num, ref_name):
    suffix = re.match(r"ref-b(\d+)(.*)\.png", ref_name).group(2).lstrip("-")
    cands = sorted(glob.glob(f"snapshots/css-standard-verify{num}-*.html"))
    cands += sorted(glob.glob(f"snapshots/css-standard-verify{num}.html"))
    if not cands:
        return None
    if suffix:
        for c in cands:
            if suffix in os.path.basename(c):
                return c
    return cands[0]


def run(args):
    return subprocess.run([EXE] + args, capture_output=True, text=True, timeout=300).stdout


def diff_pct(ref, page, w, h, dpi):
    css_w, css_h = (w * 100 // 125, h * 100 // 125) if dpi == 1.25 else (w, h)
    if css_w <= 0 or css_h <= 0:
        return None
    run(["--snapshot", page, TMP, str(css_w), str(css_h), str(dpi)])
    out = run(["--diff", ref, TMP])
    m = re.search(r"(\d+)/(\d+) px differ \(([\d.]+)%\)", out)
    return float(m.group(3)) if m else None


results = []
for ref in sorted(glob.glob("snapshots/out/ref-b*.png")):
    name = os.path.basename(ref)
    m = re.match(r"ref-b(\d+)", name)
    if not m:
        continue
    num = m.group(1)
    page = pick_page(num, name)
    if not page:
        continue
    w, h = dims(ref)
    scores = {dpi: diff_pct(ref, page, w, h, dpi) for dpi in (1.25, 1.0)}
    scores = {k: v for k, v in scores.items() if v is not None}
    if not scores:
        continue
    best_dpi = min(scores, key=scores.get)
    results.append((scores[best_dpi], num, os.path.basename(page), best_dpi, w, h))

results.sort(reverse=True)
print(f"{'page':52} {'dpi':5} {'ref px':>10} {'diff %':>8}")
for pct, num, page, dpi, w, h in results:
    print(f"b{num:<51} {dpi:<5} {w}x{h:<6} {pct:>7.3f}  {page}")
print(f"\ntotal {len(results)} pages, "
      f"{sum(1 for r in results if r[0] > 0.5)} above 0.5%")
