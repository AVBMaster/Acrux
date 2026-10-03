const fs = require('fs');
let src = fs.readFileSync('D:/tmp/g1.html', 'utf8');
const dbg = [
  "  out.innerHTML = html;",
  "  var olq = out.querySelector('ol');",
  "  var d2 = document.createElement('div');",
  "  d2.textContent = 'ATTR=' + (olq ? olq.getAttribute('style') : 'NOQUERY')",
  "    + ' LEN=' + (olq ? String(olq.getAttribute('style').length) : '-')",
  "    + ' NOL=' + out.querySelectorAll('ol').length",
  "    + ' NOLI=' + out.querySelectorAll('li').length;",
  "  out.appendChild(d2);",
].join('\n');
src = src.replace('  out.innerHTML = html;', dbg);
fs.writeFileSync('C:/Users/Manager/AppData/Local/Temp/g1dbg.html', src);
console.log('ok');
