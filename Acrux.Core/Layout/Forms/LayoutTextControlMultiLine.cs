using Acrux.Core.Dom;
using Acrux.Core.Layout.Geometry;

namespace Acrux.Core.Layout;

public class LayoutTextControlMultiLine : LayoutBlockFlow
{
    public LayoutTextControlMultiLine(Element? element) : base(element)
    {
    }

    public override string GetName() => "LayoutTextControlMultiLine";

    public override bool CreatesNewFormattingContext => true;

    private Element? ElementNode => Node as Element;

    public Element? InnerEditorElement()
    {
        var el = ElementNode;
        if (el is null)
            return null;
        foreach (var child in el.Children)
        {
            if (child is Element ce && ce.TagName == "DIV" && ce.GetAttribute("role") == "textbox")
                return ce;
        }
        return null;
    }

    public void StyleDidChange(ComputedStyle? oldStyle)
    {
        var el = ElementNode;
        if (el is not null)
            LayoutTextControl.StyleDidChange(InnerEditorElement(), oldStyle, el.ComputedStyle ?? new ComputedStyle());
    }

    public bool NodeAtPoint(HitTestResult result, HitTestLocation hitTestLocation, PhysicalOffset accumulatedOffset, HitTestPhase phase)
    {
        if (result.InnerNode is null)
            return false;

        Element? innerEditor = InnerEditorElement();
        if (innerEditor is null)
            return false;

        if (result.InnerNode == Node || result.InnerNode == innerEditor)
        {
            var el = ElementNode;
            if (el?.LayoutBox is not null)
                LayoutTextControl.HitInnerEditorElement(el.LayoutBox, innerEditor, result, hitTestLocation, accumulatedOffset);
        }
        return false;
    }
}