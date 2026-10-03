// Reconstructs the marker table from a --textops dump of the counter-styles probe:
// prints "<column>\t<style name>\t<ordinal>\t<marker>".
const fs = require('fs');
const lines = fs.readFileSync(process.argv[2], 'utf8').split(/\r?\n/);
const ops = [];
for (const l of lines) {
    const m = l.match(/^\[text\] '(.*)' x=([-\d.]+) y=([-\d.]+)(?: w=[-\d.]+)? size=([\d.]+)/);
    if (m) ops.push({ text: m[1], x: parseFloat(m[2]), y: parseFloat(m[3]), size: parseFloat(m[4]) });
}
const seen = new Set();
const uniq = ops.filter(o => {
    const k = o.x + ':' + o.y + ':' + o.text;
    if (seen.has(k)) return false;
    seen.add(k); return true;
});
// Columns are 300px wide in the probe; group by column, then by row (baseline y).
const cols = new Map();
for (const o of uniq) {
    const c = Math.floor(o.x / 300);
    if (!cols.has(c)) cols.set(c, []);
    cols.get(c).push(o);
}
const out = [];
for (const [c, list] of [...cols].sort((a, b) => a[0] - b[0])) {
    list.sort((a, b) => a.y - b.y || a.x - b.x);
    let style = '?';
    for (const o of list) {
        if (o.size === 12) { style = o.text; continue; }
        if (o.size === 18) out.push(c + '\t' + style + '\t' + o.text);
        else if (o.size === 9 && out.length) out[out.length - 1] += '\t' + o.text;
    }
}
console.log(out.join('\n'));
