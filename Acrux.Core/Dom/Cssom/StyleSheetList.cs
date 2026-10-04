namespace Acrux.Core.Dom.Cssom;

public class StyleSheetList : List<CSSStyleSheet>
{
    public new CSSStyleSheet? this[int index] => index >= 0 && index < Count ? base[index] : null;

    public int Length => Count;
}
