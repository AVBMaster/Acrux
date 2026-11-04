#!/bin/bash
# Re-measure the recent probe pages against their captured reference rows.
cd /home/master/Acrux || exit 1
export DOTNET_ROOT=$(dirname $(readlink -f $(which dotnet)))
for ref in /home/master/harness/b31{0,1,2,3}_*_ref.txt; do
  base=$(basename "$ref" _ref.txt)
  # b312_pctgroup_ref -> _probe_b312_pctgroup.html, falling back to the _b3NN_ record pages
  for cand in "snapshots/_${base}.html" "snapshots/_probe_${base}.html"; do
    if [ -f "$cand" ]; then page="$cand"; break; fi
  done
  if [ -z "$page" ]; then echo "$base SKIP no page"; unset page; continue; fi
  line=$(python3 snapshots/keyword_diff.py "$ref" --page "$page" --cols sup,echo,comp 2>&1 | tail -1)
  echo "$base $(basename "$page") -> $line"
  unset page
done
