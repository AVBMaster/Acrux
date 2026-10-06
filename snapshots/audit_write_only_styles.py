"""List ComputedStyle properties that nothing outside the style plumbing ever reads.

The applier can parse and store a value perfectly well; if no layout/paint/JS
consumer ever reads the field, the declaration is still discarded — the same defect
shape as an empty `case "x": break;`, only invisible to a grep for stubs. This walks
every public auto-property of ComputedStyle and counts reads elsewhere in the repo,
ignoring the places that merely carry the value around (the declaration itself,
Clone(), the inheritance table, the CSS-wide-keyword Copy/SetInitial table, the
animation tables and the CSSOM name list).
"""
import os, re, sys

CORE = ["Acrux.Core", "Acrux.Rendering", "Acrux.PageHost", "Acrux.Platform",
        "Acrux.Input", "Acrux.Native", "Acrux", "Acrux.SmokeTest", "Acrux.BrowserUi"]

full = open("Acrux.Core/Dom/ComputedStyle.cs").read()
# Only the ComputedStyle class body: the file also declares LayoutBox, LineBox,
# InlineRun, enums and helpers, whose properties are consumed everywhere.
start = full.index("public class ComputedStyle")
rest = full[start + 10:]
depth, end = 0, len(rest)
for i, ch in enumerate(rest):
    if ch == "{": depth += 1
    elif ch == "}":
        depth -= 1
        if depth == 0:
            end = i
            break
style_src = rest[:end]
props = re.findall(r"public\s+(?:[^\s{;]+(?:<[^>]+>)?(?:\?)?)\s+(\w+)\s*\{\s*get;", style_src)

# Files/lines that only transport a value: the style class itself, the inheritance
# table, the CSS-wide keyword table, clone/copy helpers and the animation metadata.
IGNORE_FILE_HINTS = ("ComputedStyle.cs", "CssInheritance.cs", "CssPropertyTraits.cs",
                     "AnimatableProperties.cs", "ComputedValueSerializer.cs",
                     "CssStyleDeclaration.cs")

def transport_only(line):
    return re.search(r"\b(?:Clone|CopyStyle|Apply|Copy|dest\.|to\.|from\.)\b", line) is not None


counts = {}
reads = {}

# One pass over every file with a single alternation: scanning per property made the
# tool quadratic and it stopped being usable inside a session.
alternation = re.compile(r"\.(" + "|".join(sorted(props, key=len, reverse=True)) + r")(?!\w)(?!\s*=[^=])")
for root_dir in CORE:
    for dirpath, dirs, files in os.walk(root_dir):
        if "bin" in dirpath or "obj" in dirpath or "/.git" in dirpath:
            continue
        for fn in files:
            if not fn.endswith(".cs"):
                continue
            path = os.path.join(dirpath, fn)
            if any(h in path for h in IGNORE_FILE_HINTS):
                continue
            try:
                text = open(path, encoding="utf-8", errors="ignore").read()
            except OSError:
                continue
            for m in alternation.finditer(text):
                name = m.group(1)
                line_no = text.count("\n", 0, m.start()) + 1
                line = text.split("\n")[line_no - 1].strip()
                # skip the style plumbing itself: 'Prop = Prop' inside Clone/Copy helpers
                if re.match(r"^(?:dest|to|src|child|parent)\." + name + r"\s*=", line):
                    continue
                counts[name] = counts.get(name, 0) + 1
                reads.setdefault(name, []).append(f"{path}:{line_no}: {line[:100]}")

dead = [p for p in props if counts.get(p, 0) == 0]
print(f"{len(props)} computed-style properties; {len(dead)} with no read outside the style plumbing:")
for i in range(0, len(dead), 6):
    print("  " + "  ".join(dead[i:i+6]))
if "-v" in sys.argv:
    thin = sorted((p for p in props if counts.get(p, 0) <= 2), key=lambda p: counts.get(p, 0))
    print(f"\n{len(thin)} properties with at most two reads (verify each by hand):")
    for p in thin:
        print(f"  {p} = {counts.get(p, 0)}")
        for r in reads.get(p, [])[:2]:
            print(f"      {r}")
