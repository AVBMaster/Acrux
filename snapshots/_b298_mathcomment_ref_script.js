() => {
const host = document.createElement('div');
document.body.appendChild(host);
const rows = [];
const cases = [["width","calc(1px/*c*/+ 2px)"],["width","calc(1px +/*c*/2px)"],["width","calc(1px /*c*/ + 2px)"],["width","calc(/*c*/1px + 2px)"],["width","calc(1px + 2px/*c*/)"],["width","calc(1px+/*c*/2px)"],["width","calc(1px -/*c*/ 2px)"],["width","calc(1px/*c*/- 2px)"],["width","calc(1px /*c*/+ 2px)"],["width","calc(1px/*x*/ - 2px)"],["width","calc( 1px /*c*/ -/*c*/ 2px )"],["width","min(1px/*c*/,2px)"],["width","clamp(1px/*c*/,2px,3px)"],["width","calc(100%/*c*/- 10px)"],["width","calc(1px*/*c*/2)"],["width","calc(1px +/*c*/ 2px)"],["width","calc(1px/*c*/ +/*c*/ 2px)"],["width","calc(1px + 2px)"],["width","calc(1px+2px)"],["height","calc(1px/*c*/- 2px)"],["top","calc(1px/*c*/+ 2px)"],["left","calc(1px +/*c*/2px)"],["padding-left","calc(1px/*c*/+ 1px)"],["margin-left","calc(1px/*c*/+ 1px)"],["flex-basis","calc(1px/*c*/+ 2px)"],["font-size","calc(1em/*c*/+ 2px)"],["line-height","calc(1/*c*/+ 1)"],["transform","translateX(calc(1px/*c*/+ 2px))"],["width","calc(1px + calc(2px/*c*/+ 3px))"],["margin","1px/*c*/2px"],["margin","1px/*c*/2px/*c*/3px 4px"],["padding","1px/*c*/2px"],["border-spacing","1px/*c*/2px"],["clip","rect(0px/*c*/0px 0px 0px)"],["box-shadow","1px/*c*/2px 3px rgb(0 0 0)"],["background-position","10px/*c*/20px"],["grid-template-columns","1px/*c*/2px"],["width","calc(1px/*c*/+ 2px) !important"],["--x","calc(1px/*c*/+ 2px)"],["width","calc(var(--x)/*c*/+ 1px)"]];
for (let i = 0; i < cases.length; i++) {
  const el = document.createElement('div');
  host.appendChild(el);
  el.style.setProperty(cases[i][0], cases[i][1]);
  rows.push(i + '|' + cases[i][0] + '|' + cases[i][1]
      + '|sup=' + (CSS.supports(cases[i][0], cases[i][1]) ? 1 : 0)
      + '|echo=' + el.style.getPropertyValue(cases[i][0])
      + '|comp=' + getComputedStyle(el).getPropertyValue(cases[i][0]));
}
host.remove();
return rows.join('\n');
}