const fs = require('fs');
const lines = fs.readFileSync(process.argv[2], 'utf8').split(/\r?\n/);
const seen = new Set();
const ops = [];
for (const line of lines) {
  const m = /^\[text\] '(.*)' x=([-\d.]+) y=([-\d.]+) size=([\d.]+)/.exec(line);
  if (!m) continue;
  const o = { t: m[1], x: parseFloat(m[2]), y: parseFloat(m[3]), s: parseFloat(m[4]) };
  const key = o.x + '/' + o.y + '/' + o.t;
  if (seen.has(key)) continue;
  seen.add(key);
  ops.push(o);
}
// Each style block is its own column, one item per line, so a (row, column)
// bucket holds exactly one marker and the grey ordinal it belongs to.
const buckets = new Map();
for (const o of ops) {
  const key = Math.round(o.y) + '#' + Math.floor(o.x / 300);
  if (!buckets.has(key)) buckets.set(key, []);
  buckets.get(key).push(o);
}
const rows = [];
for (const key of [...buckets.keys()].sort((a, b) => {
  const [ay, ax] = a.split('#').map(Number), [by, bx] = b.split('#').map(Number);
  return ay - by || ax - bx;
})) {
  const items = buckets.get(key).sort((p, q) => p.x - q.x);
  const mark = items.find(o => o.s > 12);
  const val = items.find(o => o.s <= 12);
  if (!val) continue;
  rows.push((val.t + ' ').padEnd(12) + '=> ' + (mark ? mark.t : '(none)'));
}
console.log(rows.join('\n'));
