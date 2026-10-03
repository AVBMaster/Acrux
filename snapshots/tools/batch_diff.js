// Batch regression sweep: re-shoots each ref-bN.png at its own pixel size (trying both
// device-pixel-ratio hypotheses, since the original css width is not recorded) and diffs
// against the reference. The smaller-diff hypothesis is the size the ref was baked at.
// Usage: node snapshots/tools/batch_diff.js <exe> [--bake] 2 5 8 ...
// --bake copies the best-matching re-render onto ref-bN.png (only for pages whose
// change has already been confirmed against Edge numbers).
const { execFileSync } = require('child_process');
const fs = require('fs');
const path = require('path');

const exe = process.argv[2];
const bake = process.argv.includes('--bake');
const batches = process.argv.slice(3).filter(a => a !== '--bake');

function pngSize(file) {
    const b = fs.readFileSync(file);
    return { w: b.readUInt32BE(16), h: b.readUInt32BE(20) };
}

function diff(a, b) {
    try {
        const out = execFileSync(exe, ['--diff', a, b], { encoding: 'utf8', maxBuffer: 1 << 26 });
        const m = out.match(/([\d.]+)\s*%/);
        return { pct: m ? parseFloat(m[1]) : null, text: out.trim().split(/\r?\n/).pop() };
    } catch (e) {
        const text = String(e.stdout || '').trim().split(/\r?\n/).pop() || String(e.message);
        const m = text.match(/([\d.]+)\s*%/);
        return { pct: m ? parseFloat(m[1]) : null, text };
    }
}

const rows = [];
for (const n of batches) {
    const ref = path.join('snapshots/out', 'ref-b' + n + '.png');
    const src = path.join('snapshots', 'css-standard-verify' + n + '.html');
    if (!fs.existsSync(ref) || !fs.existsSync(src)) { rows.push('b' + n + ' SKIP (missing ref or html)'); continue; }
    const size = pngSize(ref);
    let best = null;
    for (const dpr of [2, 1]) {
        const out = path.join('snapshots/out', 'new-b' + n + '.png');
        try {
            execFileSync(exe, ['--snapshot', src, out, String(Math.round(size.w / dpr)), String(Math.round(size.h / dpr)), String(dpr)],
                { encoding: 'utf8', stdio: 'ignore', maxBuffer: 1 << 24 });
        } catch (e) { rows.push('b' + n + ' RENDER-FAIL dpr=' + dpr + ' ' + e.message); continue; }
        if (pngSize(out).w !== size.w) continue;
        const d = diff(ref, out);
        if (best === null || (d.pct !== null && d.pct < best.pct)) best = { dpr, pct: d.pct, text: d.text, out };
    }
    rows.push('b' + n + ' ' + (best ? 'dpr=' + best.dpr + ' diff=' + (best.pct === null ? '?' : best.pct + '%')
        : 'NO-MATCHING-SIZE ' + size.w + 'x' + size.h));
    if (bake && best && best.pct !== null && best.pct > 0) {
        fs.copyFileSync(best.out, ref);
        rows[rows.length - 1] += ' -> ref re-baked';
    }
}
console.log(rows.join('\n'));
