"""Re-bake the marker refs whose current output has been verified against Edge,
keeping the previous image as evidence. Usage: python rebake_ref.py 137 138 134"""
import glob, os, re, shutil, struct, subprocess, sys

import _acrux_cli as cli

ENV = cli.environment()


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


for num in sys.argv[1:]:
    for ref in sorted(glob.glob(f"snapshots/out/ref-b{num}*.png")):
        page = pick_page(num, os.path.basename(ref))
        if not page:
            print(f"b{num}: no page for {os.path.basename(ref)}")
            continue
        w, h = dims(ref)
        dpi = 1.25 if w * 100 % 125 == 0 and (w * 100 // 125) > 200 else 1.0
        for try_dpi in (dpi, 1.0 if dpi == 1.25 else 1.25):
            css_w, css_h = (w * 100 // 125, h * 100 // 125) if try_dpi == 1.25 else (w, h)
            if (css_w, css_h) != (w, h) or try_dpi == dpi:
                dpi = try_dpi
                break
        css_w, css_h = (w * 100 // 125, h * 100 // 125) if dpi == 1.25 else (w, h)
        evidence = os.path.join("snapshots/out", f"old-b{num}-{os.path.basename(page)[26:-5]}.png")
        shutil.copy(ref, evidence)
        subprocess.run(cli.command(["--diff", ref, ref]), capture_output=True, text=True, env=ENV)
        subprocess.run(cli.command(["--snapshot", page, ref, str(css_w), str(css_h), str(dpi)]),
                       capture_output=True, text=True, env=ENV)
        print(f"b{num} {os.path.basename(ref)}: {css_w}x{css_h}@{dpi} (old kept as {os.path.basename(evidence)})")
