"""Audit which declared CSS properties the engine can actually apply.

Every CssPropertyId member is turned into its CSS name with the same kebab-casing rule
the engine uses (CssPropertyIdExtensions), then matched against the property names the
applier and the shorthand expander really handle. Anything in the gap is a declaration
that parses, matches, and is then thrown away — no warning, no trace.

Three such name lists exist (the applier's switch, ShorthandExpander's dispatch, and the
IsExpandableShorthand gate), which is why this check has to be mechanical.

Usage: python snapshots/audit_property_ids.py
"""
import os
import re
import sys

root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def read(*parts):
    with open(os.path.join(root, *parts), encoding="utf-8") as fh:
        return fh.read()


def css_name(member):
    return re.sub(r"([a-z0-9])([A-Z])", r"\1-\2", member).lower()


src = read("Acrux.Core", "Css", "Properties", "CssPropertyId.cs")
body = src[src.index("enum CssPropertyId"):src.index("static class CssPropertyIdExtensions")]
ids = re.findall(r"^\s{4}([A-Za-z][A-Za-z0-9]*)\s*(?:=\s*\d+)?\s*,?\s*$", body, re.M)

handled = set()
for path in (("Acrux.Core", "Css", "Resolver", "CssPropertyApplier.cs"),
             ("Acrux.Core", "Css", "ShorthandExpander.cs")):
    text = read(*path)
    handled |= set(re.findall(r'case "([a-z0-9-]+)"', text))
    handled |= set(re.findall(r'"([a-z0-9-]+)" =>', text))

missing = sorted(
    css_name(n) for n in ids
    if n not in ("Invalid", "MaxProperties") and css_name(n) not in handled
)

print(f"{len(ids)} property ids, {len(missing)} with no applier or expander case:")
for i in range(0, len(missing), 6):
    print("  " + "  ".join(missing[i:i + 6]))
sys.exit(0)
