using Acrux.Core.Dom;
using Acrux.Core.Layout.Geometry;

namespace Acrux.Core.Layout;

public class LayoutFieldset : LayoutBlockFlow
{
    public LayoutFieldset(Element? element) : base(element)
    {
    }

    public override string GetName() => "LayoutFieldset";

    public override bool CreatesNewFormattingContext => true;

    private Element? ElementNode => Node as Element;

    public LayoutBlock? FindAnonymousFieldsetContentBox()
    {
        LayoutObject? firstChild = SlowFirstChild();
        if (firstChild is null)
            return null;
        if (firstChild.IsAnonymous)
            return firstChild as LayoutBlock;
        LayoutObject? lastChild = firstChild.NextSibling;
        if (lastChild is not null && lastChild.IsAnonymous)
            return lastChild as LayoutBlock;
        return null;
    }

    public static LayoutBoxModelObject? FindInFlowLegend(LayoutBlock fieldset)
    {
        for (LayoutObject? legend = fieldset.SlowFirstChild(); legend is not null; legend = legend.NextSibling)
        {
            if (legend.Node is Element el && el.TagName == "LEGEND")
                return legend as LayoutBoxModelObject;
        }
        return null;
    }

    public LayoutBoxModelObject? FindInFlowLegend() => FindInFlowLegend(this);

    public void AddChild(LayoutObject newChild, LayoutObject? beforeChild = null)
    {
        if (!newChild.IsText && !newChild.IsAnonymous)
        {
            // Adding a child LayoutObject always causes reattach of <fieldset>.
            // |beforeChild| is always null in this case.
        }
        else if (beforeChild is not null && beforeChild.Node is Element el && el.TagName == "LEGEND")
        {
            // Whitespace changes resulting from removed nodes. Adjust the insert
            // position past the rendered legend.
            List<LayoutObject> children = Children;
            int idx = children.IndexOf(beforeChild);
            beforeChild = idx >= 0 && idx + 1 < children.Count ? children[idx + 1] : null;
        }

        // https://html.spec.whatwg.org/C/#the-fieldset-and-legend-elements
        // > * If the element has a rendered legend, then that element is expected
        // >   to be the first child box.
        // > * The anonymous fieldset content box is expected to appear after the
        // >   rendered legend and is expected to contain the content (including
        // >   the '::before' and '::after' pseudo-elements) of the fieldset
        // >   element except for the rendered legend, if there is one.

        if (newChild.Node is Element newEl && newEl.TagName == "LEGEND" && FindInFlowLegend() is null)
        {
            AddChildFirst(newChild);
            return;
        }
        LayoutBlock? fieldsetContent = FindAnonymousFieldsetContentBox();
        if (fieldsetContent is not null)
            fieldsetContent.AddChild(newChild);
    }

    private void AddChildFirst(LayoutObject child)
    {
        child.Parent = this;
        if (Children.Count > 0)
        {
            Children[0].PreviousSibling = child;
            child.NextSibling = Children[0];
        }
        Children.Insert(0, child);
    }

    public void InsertedIntoTree()
    {
        if (FindAnonymousFieldsetContentBox() is not null)
            return;

        // We wrap everything inside an anonymous child, which will take care of the
        // fieldset contents. This parent will only be responsible for the fieldset
        // border and the rendered legend, if there is one.
        DisplayType display = DisplayType.Block;
        switch (ElementNode?.ComputedStyle?.Display)
        {
        case DisplayType.Flex:
        case DisplayType.InlineFlex:
            display = DisplayType.Flex;
            break;
        case DisplayType.Grid:
        case DisplayType.InlineGrid:
            display = DisplayType.Grid;
            break;
        }

        var fieldsetContent = new LayoutBlock(null);
        AddChild(fieldsetContent);
    }

    public void UpdateAnonymousChildStyle(LayoutObject child, ComputedStyle childStyle)
    {
        var style = ElementNode?.ComputedStyle;
        if (style is null)
            return;
        childStyle.Display = style.Display;
        childStyle.AlignContent = style.AlignContent;
        childStyle.AlignItems = style.AlignItems;
        childStyle.JustifyContent = style.JustifyContent;
        childStyle.JustifyItems = style.JustifyItems;
        childStyle.PaddingTop = style.PaddingTop;
        childStyle.PaddingRight = style.PaddingRight;
        childStyle.PaddingBottom = style.PaddingBottom;
        childStyle.PaddingLeft = style.PaddingLeft;
        childStyle.BorderTopLeftRadius = style.BorderTopLeftRadius;
        childStyle.BorderTopRightRadius = style.BorderTopRightRadius;
        childStyle.BorderBottomLeftRadius = style.BorderBottomLeftRadius;
        childStyle.BorderBottomRightRadius = style.BorderBottomRightRadius;
        childStyle.OverflowX = style.OverflowX;
        childStyle.OverflowY = style.OverflowY;
        childStyle.UnicodeBidi = style.UnicodeBidi;
        childStyle.FlexDirection = style.FlexDirection;
        childStyle.FlexWrap = style.FlexWrap;
        childStyle.GridAutoColumns = style.GridAutoColumns;
        childStyle.GridAutoRows = style.GridAutoRows;
        childStyle.GridAutoFlow = style.GridAutoFlow;
        childStyle.GridColumnStart = style.GridColumnStart;
        childStyle.GridColumnEnd = style.GridColumnEnd;
        childStyle.GridRowStart = style.GridRowStart;
        childStyle.GridRowEnd = style.GridRowEnd;
        childStyle.GridTemplateColumns = style.GridTemplateColumns;
        childStyle.GridTemplateRows = style.GridTemplateRows;
        childStyle.GridTemplateAreas = style.GridTemplateAreas;
        childStyle.RowGap = style.RowGap;
        childStyle.ColumnGap = style.ColumnGap;
        childStyle.ColumnCount = style.ColumnCount;
        childStyle.ColumnWidth = style.ColumnWidth;
    }

    public bool BackgroundIsKnownToBeOpaqueInRect(PhysicalRect localRect)
    {
        if (FindInFlowLegend() is not null)
            return false;
        return true;
    }

    public float ScrollWidth()
    {
        var content = FindAnonymousFieldsetContentBox();
        if (content is not null)
        {
            float maxEnd = 0;
            foreach (var child in content.Children)
            {
                if (child is AuroraBox box)
                    maxEnd = Math.Max(maxEnd, box.X + box.Width);
            }
            return maxEnd;
        }
        return Width;
    }

    public float ScrollHeight()
    {
        var content = FindAnonymousFieldsetContentBox();
        if (content is not null)
        {
            float maxEnd = 0;
            foreach (var child in content.Children)
            {
                if (child is AuroraBox box)
                    maxEnd = Math.Max(maxEnd, box.Y + box.Height);
            }
            return maxEnd;
        }
        return Height;
    }
}