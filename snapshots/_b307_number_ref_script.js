() => {
const host = document.createElement('div');
document.body.appendChild(host);
const rows = [];
const cases = [["margin-left","12.px","point"],["margin-left","123456789.px","point"],["margin-left",".px","point"],["margin-left","12.5px","point"],["margin-left","12..5px","point"],["margin-left","12.px 4px","point"],["width","12.","point"],["opacity","12.","point"],["opacity",".5","point"],["line-height","12.","point"],["z-index","12.","point"],["flex-grow","12.","point"],["margin-left","calc(12.px + 1px)","point"],["margin-left","calc(12px + 1.)","point"],["margin-left","min(12.px, 4px)","point"],["transition-duration","12.s","point"],["transition-delay","12.s","point"],["border-width","12.px","point"],["font-size","12.px","point"],["letter-spacing","12.px","point"],["margin-left","12.PX","point"],["margin-left","+12.px","point"],["margin-left","-12.px","point"],["margin-left","12.e2px","point"],["rotate","12.deg","point"],["margin-left","12.5.px","point"],["width","12","bare"],["margin-left","12","bare"],["font-size","12","bare"],["border-width","12","bare"],["line-height","12","bare"],["flex-grow","12px","bare"],["width","12.5","bare"],["padding-top","12","bare"],["top","12","bare"],["stroke-width","12","bare"],["letter-spacing","12","bare"],["word-spacing","12","bare"],["min-width","12","bare"],["outline-width","12","bare"],["row-gap","12","bare"],["text-indent","12","bare"],["vertical-align","12","bare"],["height","12.5","bare"],["column-width","12","bare"],["gap","12","bare"],["flex-basis","12","bare"],["border-radius","12","bare"],["translate","12","bare"],["inset-inline-start","12","bare"],["shape-margin","12","bare"],["scroll-padding-left","12","bare"],["perspective","12","bare"],["border-spacing","12","bare"],["inset","12","bare"],["width","0","zero"],["margin-top","0","zero"],["top","0","zero"],["font-size","0","zero"],["flex-basis","0","zero"],["zoom","12","zero"],["tab-size","4","zero"],["border-image-width","2","zero"],["stroke-dashoffset","12","zero"],["margin","1px 2px 3px 4px","shape"],["padding","1px 2px 3px","shape"],["inset","1px 2px","shape"],["margin-left","1px 2px","shape"],["width","calc(2 * 1px)","shape"],["margin-left","calc(1 + 1px)","shape"],["rotate","x 12deg","shape"],["column-width","auto","shape"],["gap","12px","shape"]];
for (let i = 0; i < cases.length; i++) {
  const el = document.createElement('div');
  host.appendChild(el);
  el.style.setProperty(cases[i][0], cases[i][1]);
  rows.push(i + '|' + cases[i][2] + '|' + cases[i][0] + '|' + cases[i][1]
      + '|sup=' + (CSS.supports(cases[i][0], cases[i][1]) ? 1 : 0)
      + '|echo=' + el.style.getPropertyValue(cases[i][0])
      + '|comp=' + getComputedStyle(el).getPropertyValue(cases[i][0]));
}
host.remove();
return rows.join('\n');
}