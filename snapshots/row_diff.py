"""Compare a probe page's rows between the two engines.

A probe page prints one row per case as
'index|property|value|n=..|names=..|echo=..|comp=..|sup=..|text=..', so the reference engine's dump
(a JSON string written by evaluate_script's filePath) and this engine's --dumplayout stdout can be
matched row by row. Only the cells after the value are compared: the index, the property and the
value are what identify the case, not what it answers.

Usage: python snapshots/row_diff.py <probe.html> <reference-dump.txt> [viewport-width]
"""
import json
import os
import re
import subprocess
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import _acrux_cli as cli


def our_rows(page, width):
    env = cli.environment()
    out = subprocess.run(cli.command(['--dumplayout', page, str(width), '800', '1']),
                         capture_output=True, text=True, env=env).stdout
    rows = []
    started = False
    for line in out.split('\n'):
        if not started:
            match = re.match(r'^\[JS\] (\S+\|.*)$', line)
            if match:
                rows.append(match.group(1))
                started = True
            continue
        if re.match(r'^\S+\|', line) and ('|n=' in line or '|names=' in line):
            rows.append(line)
        else:
            break
    return {row.split('|')[0]: row for row in rows}


def main():
    if len(sys.argv) < 3:
        print(__doc__)
        return 2
    page, ref_path = sys.argv[1], sys.argv[2]
    width = int(sys.argv[3]) if len(sys.argv) > 3 else 1200
    ref = {}
    for row in json.load(open(ref_path)).split('\n'):
        if row.strip():
            ref[row.split('|')[0]] = row
    ours = our_rows(page, width)
    diffs = 0
    for key in sorted(ref, key=lambda k: (0, int(k)) if k.isdigit() else (1, k)):
        a, b = ref[key], ours.get(key)
        if b is None:
            print('MISSING', key, a.split('|')[1], a.split('|')[2])
            diffs += 1
            continue
        left, right = a.split('|')[2:], b.split('|')[2:]
        if left != right:
            diffs += 1
            print('row', key, 'key', a.split('|')[1], '|', a.split('|')[2])
            print('  ref:', ' | '.join(left))
            print('  our:', ' | '.join(right))
    print('total rows', len(ref), 'diffs', diffs)
    return 0 if diffs == 0 else 1


if __name__ == '__main__':
    sys.exit(main())
