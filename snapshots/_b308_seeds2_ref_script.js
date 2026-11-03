() => {
const host = document.createElement('div');
document.body.appendChild(host);
const rows = [];
const cases = [["cx","5","inherited"],["cy","5","inherited"],["r","5","inherited"],["x","5","inherited"],["y","5","inherited"],["rx","5","inherited"],["ry","5","inherited"],["d","path('M 0 0')","inherited"],["-webkit-text-combine","horizontal","inherited"],["-webkit-rtl-ordering","logical","inherited"],["image-rendering","pixelated","inherited"],["mix-blend-mode","multiply","inherited"],["forced-color-adjust","none","inherited"],["initial-letter","2 drop","inherited"],["text-decoration-line","blink underline","inherited"],["stroke-width","3","inherited"],["rx","auto","geo2"],["ry","auto","geo2"],["rx","50%","geo2"],["cx","1in","geo2"],["cx","1.5","geo2"],["cx","0%","geo2"],["cx","1e2","geo2"],["cx","calc(2)","geo2"],["cx","min(1px, 2px)","geo2"],["cx","1ch","geo2"],["cx","5 5px","geo2"],["cx","auto 5","geo2"],["r","-1","geo2"],["d","path(\"M0\")","geo2"],["d","path(M 0 0)","geo2"],["d","path('a')","geo2"],["d","path('M 0 0', none)","geo2"],["d","none none","geo2"],["text-decoration-line","grammar-error blink","word2"],["text-decoration-line","blink none","word2"],["text-decoration-line","underline blink overline","word2"],["text-decoration","blink red","word2"],["initial-letter","raise 2 drop 3","word2"],["initial-letter","0.5 drop","word2"],["initial-letter","drop -1","word2"],["initial-letter","2 raise","word2"],["forced-color-adjust","preserve-parent-color none","word2"],["mix-blend-mode","PLUS-LIGHTER plus-lighter","word2"],["image-rendering","-webkit-OPTIMIZE-contrast","word2"],["-webkit-text-combine","none horizontal","word2"]];
for (let i = 0; i < cases.length; i++) {
  const el = document.createElement('div');
  host.appendChild(el);
  if (cases[i][2] === 'inherited') {
    var p = document.createElement('div'); host.appendChild(p);
    var kid = document.createElement('div'); p.appendChild(kid);
    p.style.setProperty(cases[i][0], cases[i][1]);
    rows.push(i + '|' + cases[i][2] + '|' + cases[i][0] + '|' + cases[i][1]
        + '|sup=1'
        + '|echo=' + getComputedStyle(kid).getPropertyValue(cases[i][0])
        + '|comp=' + getComputedStyle(p).getPropertyValue(cases[i][0]));
  } else {
    if (cases[i][1] !== '~') el.style.setProperty(cases[i][0], cases[i][1]);
    rows.push(i + '|' + cases[i][2] + '|' + cases[i][0] + '|' + cases[i][1]
        + '|sup=' + (cases[i][1] === '~' ? '-' : (CSS.supports(cases[i][0], cases[i][1]) ? 1 : 0))
        + '|echo=' + el.style.getPropertyValue(cases[i][0])
        + '|comp=' + getComputedStyle(el).getPropertyValue(cases[i][0]));
  }
}
host.remove();
return rows.join('\n');
}