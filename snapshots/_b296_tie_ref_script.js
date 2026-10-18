() => {
const host = document.createElement('div');
document.body.appendChild(host);
const rows = [];
const T = ['0.1234505','0.1234515','0.1234525','0.1234535','0.1234545','0.1234555','0.1234565','0.1234575','0.1234585','0.1234595','0.12345650','1.1234565','1.1234575','12.1234565','12.34565','123.4565','1234.565','12345.55','12345.65','12345.75','12345.85','123456.5','123456.75','1234567.5','9.999995','9999.995','10000.05','100000.5','1.0000005','1.0000015','2.5000005','0.0123456','0.01234565','0.0199999999','0.001234565','0.001234555','0.000123455','0.000123456789','0.0001234','1.5','12345.675'].map(function (v) { return v + 'px'; });
const S = ['0.1234525','0.1234535','0.1234565','0.1234575','0.1234585','0.1234545','0.0123456','0.01234565','0.0199999999','0.000123455','0.000123456789','12345.65','123456.5','9.999995','1.0000005','1.0000015','2.5000005','12345.675'].map(function (v) { return v + 'px'; });
const cases = [];
[['margin-left', T],['padding-left', S],['shape-margin', S],['letter-spacing', T],['text-indent', S],['word-spacing', S]].forEach(function (g) {
  g[1].forEach(function (v) { cases.push([g[0], v]); });
});

for (let i = 0; i < cases.length; i++) {
  const el = document.createElement('div');
  host.appendChild(el);
  el.style.setProperty(cases[i][0], cases[i][1]);
  rows.push(i + '|' + cases[i][0] + '|' + cases[i][1] + '|echo=' + el.style.getPropertyValue(cases[i][0]) + '|comp=' + getComputedStyle(el).getPropertyValue(cases[i][0]));
}
host.remove();
return rows.join('\n');
}