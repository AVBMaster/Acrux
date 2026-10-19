() => {
const host = document.createElement('div');
document.body.appendChild(host);
const rows = [];
const cases = [["width","calc(1px/*c*/+ 2px)"],["width","calc(1px+/*c*/2px)"],["width","calc(1px+2px)"],["width","calc(1px /*c*/ + 2px)"],["width","calc(1px/*c*/ + 2px)"],["height","calc(1px -/*c*/ 2px)"],["margin","1px/*c*/2px"],["margin","1px/*c*/2px/*c*/3px 4px"],["padding","1px/*c*/2px"],["clip","rect(0px/*c*/0px 0px 0px)"],["background-position","10px/*c*/20px"],["width","calc(1px/*c*/+ 2px) !important"],["--x","calc(1px/*c*/+ 2px)"],["--y","1px/*c*/2px"],["width","calc(var(--x)/*c*/+ 1px)"],["grid-template-columns","1px/*c*/2px"]];
for (let i = 0; i < cases.length; i++) {
  const el = document.createElement('div');
  host.appendChild(el);
  el.style.setProperty(cases[i][0], cases[i][1]);
  var a = document.createElement('div'); host.appendChild(a);
  a.setAttribute('style', cases[i][0] + ':' + cases[i][1]);
  var s = document.createElement('style');
  s.textContent = '#c' + i + '{' + cases[i][0] + ':' + cases[i][1] + '}';
  document.head.appendChild(s);
  var c = document.createElement('div'); c.id = 'c' + i; host.appendChild(c);
  var sheetText = '';
  for (var k = 0; k < document.styleSheets.length; k++) {
    var sh = document.styleSheets[k];
    if (!sh.ownerNode || sh.ownerNode !== s) continue;
    for (var m = 0; m < sh.cssRules.length; m++)
      sheetText = sh.cssRules[m].style.getPropertyValue(cases[i][0]);
  }
  rows.push(i + '|' + cases[i][0] + '|' + cases[i][1]
      + '|cssom=' + el.style.getPropertyValue(cases[i][0])
      + '|attr=' + a.style.getPropertyValue(cases[i][0])
      + '|rule=' + sheetText
      + '|compA=' + getComputedStyle(a).getPropertyValue(cases[i][0])
      + '|compC=' + getComputedStyle(c).getPropertyValue(cases[i][0]));
}
host.remove();
return rows.join('\n');
}