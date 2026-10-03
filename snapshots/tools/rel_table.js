// Compares our engine's --dumplayout geometry for the position:relative probe against
// the Edge baseline (measured with getBoundingClientRect on the same markup).
// Usage: node snapshots/tools/rel_table.js <dumplayout-file>
// Lines look like: <div#c01t> ... border=(30.0,20.0 40.0x20.0) ...
// dx/dy are (target - reference), so the static flow gap is part of every number.
const fs = require('fs');
const text = fs.readFileSync(process.argv[2], 'utf8');

const boxes = {};
for (const line of text.split(/\r?\n/)) {
    const m = line.match(/<(\w+)#([A-Za-z0-9_]+)>.*border=\(([-\d.]+),([-\d.]+) ([-\d.]+)x([-\d.]+)\)/);
    if (m) boxes[m[2]] = { l: parseFloat(m[3]), t: parseFloat(m[4]), w: parseFloat(m[5]), h: parseFloat(m[6]) };
}

// Edge baseline, measured on css-standard-verify125-relative(-static).html.
const baseline = {
    c01: [30, 40],      // top:20 left:30
    c02: [-25, 10],     // bottom:10 right:25
    c03: [0, 30],       // top:10 + bottom:40 -> top wins
    c04: [10, 20],      // left:10 + right:40 -> left wins in LTR
    c05: [0, 20],       // auto/auto -> no displacement
    c06: [200, 20],     // left:50% of the 400px containing block
    c07: [0, 70],       // top:25% of the 200px containing block
    c08: [32, 20],      // left:2em
    c09: [26, 20],      // left:calc(10px + 1em)
    c10: [-30, 5],      // negative insets
    c11: [0, 20],       // z-index only
    c12: [33.9, 5],     // inline span (verified through --textops)
    c13: [30.9, 13.6],  // inline-block
    c15: [5, 25],       // descendants move with the box
    c16: [7, 26],       // display:table-cell
    c17: [40, 60],      // the next sibling keeps its static position
};

const out = [];
for (const id of Object.keys(baseline)) {
    const r = boxes[id + 'r'], t = boxes[id + 't'];
    if (!r || !t) { out.push(id + ' NO-BOX ref=' + !!r + ' tgt=' + !!t); continue; }
    const dx = t.l - r.l, dy = t.t - r.t;
    const [wx, wy] = baseline[id];
    const ok = Math.abs(dx - wx) < 0.6 && Math.abs(dy - wy) < 0.6;
    out.push(id + ' dx=' + dx.toFixed(1) + '/' + wx + ' dy=' + dy.toFixed(1) + '/' + wy + (ok ? ' OK' : ' **'));
}
if (boxes['c15k'] && boxes['c15t']) {
    const k = boxes['c15k'], t = boxes['c15t'];
    const rel = [k.l - t.l, k.t - t.t];
    out.push('c15 kidRel=' + rel[0].toFixed(1) + ',' + rel[1].toFixed(1) + '/0,0' +
        (Math.abs(rel[0]) < 0.6 && Math.abs(rel[1]) < 0.6 ? ' OK' : ' **'));
}
if (boxes['c17a'] && boxes['c17r']) {
    const a = boxes['c17a'], r = boxes['c17r'];
    out.push('c17 afterDy=' + (a.t - r.t).toFixed(1) + '/40' +
        (Math.abs((a.t - r.t) - 40) < 0.6 ? ' OK' : ' **'));
}
console.log(out.join('\n'));
