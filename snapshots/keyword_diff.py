"""Compare a probe page's cells between the two engines, one column at a time.

A probe page writes one row per case as 'index|property|value|sup=..|n=..|echo=..|comp=..'.
`sup=` is a question the grammar answers and `n=` is the CSSOM's longhand count, so both engines
print them; `n=` is what a shorthand's expansion is measured by, and `echo=`/`comp=` are the two
surfaces a value text can be wrong on. Every column is comparable — which is the point: a grammar
arm is only finished when the row agrees on all four.

The reference dump is the JSON string written by evaluate_script's filePath. This engine's rows come
either from a file captured earlier (a 600-row page takes minutes, and one code change should be
measured against every row) or from a fresh --dumplayout run.

Usage: python snapshots/keyword_diff.py <reference-dump.txt> [--ours <captured-stdout>]
                                          [--page <probe.html>] [--cols sup,n,echo,comp]
"""
import json
import os
import re
import subprocess
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import _acrux_cli as cli

CELL = re.compile(r'^([a-z]+)=')


def parse(text):
    """index -> {column -> cell}; a row without a '=' cell keeps its bare value as 'raw'."""
    rows = {}
    for line in text.split('\n'):
        if not line.strip():
            continue
        parts = line.split('|')
        head = [p for p in parts if not CELL.match(p)]
        cells = {}
        for p in parts:
            match = CELL.match(p)
            if match:
                cells[match.group(1)] = p[match.end():].strip()
        rows[parts[0]] = (head[1] if len(head) > 1 else '', head[2] if len(head) > 2 else '', cells)
    return rows


def our_rows(page):
    env = cli.environment()
    out = subprocess.run(cli.command(['--dumplayout', page, '1200', '800', '1']),
                         capture_output=True, text=True, env=env).stdout
    started = False
    kept = []
    for line in out.split('\n'):
        if not started:
            if re.match(r'^\[JS\] \S+\|', line):
                started = True
                kept.append(line[5:])
            continue
        if re.match(r'^\S+\|', line):
            kept.append(line)
    return '\n'.join(kept)


def main():
    args = sys.argv[1:]
    ref_path = args[0]

    def option(name, default=None):
        return args[args.index(name) + 1] if name in args else default

    cols = (option('--cols', 'sup,n,echo,comp') or '').split(',')
    ours = option('--ours')
    if ours is None:
        ours = our_rows(option('--page'))
    else:
        ours = open(ours).read()
        if ours.startswith('[JS] '):
            ours = '\n'.join([line[5:] if i == 0 else line
                              for i, line in enumerate(ours.rstrip('\n').split('\n'))])

    ref = parse(json.load(open(ref_path)))
    mine = parse(ours)
    by_col = {c: 0 for c in cols}
    for key in sorted(ref, key=lambda k: int(k) if k.isdigit() else 0):
        prop, value, left = ref[key]
        right = mine.get(key)
        if right is None:
            print('MISSING', key, prop, value)
            continue
        right = right[2]
        for col in cols:
            a, b = left.get(col, '<none>'), right.get(col, '<none>')
            if a != b:
                by_col[col] += 1
                print(f'row {key} {prop}: {value} [{col}]')
                print(f'  ref: {a!r}')
                print(f'  our: {b!r}')
    print('rows', len(ref), 'diffs', by_col)
    return 0 if sum(by_col.values()) == 0 else 1


if __name__ == '__main__':
    sys.exit(main())
