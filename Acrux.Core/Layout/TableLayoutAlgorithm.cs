using System;
using Acrux.Core.Dom;
using SkiaSharp;

namespace Acrux.Core.Layout;

/// <summary>
/// CSS Table Layout Algorithm 鈥?legacy compat fa莽ade. The actual column/row
/// computation lives in <see cref="Acrux.Core.Layout.Table.TableLayoutAlgorithm"/>;
/// this class builds the fragment tree with the modern table algorithm and converts
/// it into legacy <see cref="LayoutBox"/>es for the painting pipeline.
/// </summary>
public class TableLayoutAlgorithm
{
    private readonly Func<Element, float, float, float, LayoutBox?, LayoutBox?> _createLayoutBox;
    private readonly LayoutEngine _engine;

    /// <summary>Static convenience method for backward compatibility.</summary>
    public static void LayoutTable(Element tableElement, LayoutBox box, float availableWidth)
    {
        var algo = new TableLayoutAlgorithm();
        algo.Layout(tableElement, box, availableWidth);
    }

    public TableLayoutAlgorithm(
        Func<Element, float, float, float, LayoutBox?, LayoutBox?>? createLayoutBox = null,
        LayoutEngine? engine = null)
    {
        _createLayoutBox = createLayoutBox ?? DefaultBoxFactory;
        _engine = engine ?? new LayoutEngine();
    }

    private static LayoutBox DefaultBoxFactory(Element e, float x, float y, float w, LayoutBox? parent)
    {
        var box = new LayoutBox
        {
            Parent = parent,
            MarginBox = new SKRect(x, y, x + w, y + 16),
            BorderBox = new SKRect(x, y, x + w, y + 16),
            PaddingBox = new SKRect(x, y, x + w, y + 16),
            ContentBox = new SKRect(x, y, x + w, y + 16),
        };
        box.Dimensions = new BoxDimensions { Style = e.ComputedStyle, Element = e };
        return box;
    }

    public void Layout(Element tableElement, LayoutBox box, float availableWidth)
    {
        var style = tableElement.ComputedStyle;
        if (style == null) return;

        float avail = Math.Max(0, float.IsNaN(availableWidth) ? 0 : availableWidth);

        var builder = ConstraintSpace.Builder(avail, float.PositiveInfinity);
        builder.SetIsFixedInlineSize(true);
        builder.SetIsNewFormattingContext(true);
        builder.SetViewportSize(avail, avail);

        var result = new Acrux.Core.Layout.Table.TableLayoutAlgorithm(tableElement, builder.ToConstraintSpace()).Layout();
        var frag = result.Fragment;

        float tableWidth = frag.InlineSize;
        float tableHeight = frag.BlockSize;
        if (float.IsNaN(tableWidth) || tableWidth < 0) tableWidth = avail;
        if (float.IsNaN(tableHeight) || tableHeight < 0) tableHeight = 0;

        float bpInline = frag.BorderLeft + frag.BorderRight + frag.PaddingLeft + frag.PaddingRight;
        float bpBlock = frag.BorderTop + frag.BorderBottom + frag.PaddingTop + frag.PaddingBottom;
        float contentWidth = Math.Max(0, tableWidth - bpInline);
        float contentHeight = Math.Max(0, tableHeight - bpBlock);

        float left = box.ContentBox.Left;
        float top = box.ContentBox.Top;

        box.ContentBox = new SKRect(left, top, left + contentWidth, top + contentHeight);
        box.PaddingBox = new SKRect(left - frag.PaddingLeft, top - frag.PaddingTop,
            left + contentWidth + frag.PaddingRight, top + contentHeight + frag.PaddingBottom);
        box.BorderBox = new SKRect(left - frag.PaddingLeft - frag.BorderLeft, top - frag.PaddingTop - frag.BorderTop,
            left + contentWidth + frag.PaddingRight + frag.BorderRight,
            top + contentHeight + frag.PaddingBottom + frag.BorderBottom);
        box.MarginBox = new SKRect(box.BorderBox.Left, box.BorderBox.Top, box.BorderBox.Right, box.BorderBox.Bottom);
        box.LineHeight = Fonts.LineBoxMetrics.GetLineHeight(style);
        if (box.Dimensions == null)
            box.Dimensions = new BoxDimensions { Style = style, Element = tableElement };
        box.Children.Clear();
        tableElement.LayoutBox = box;

        // Convert the fragment tree (sections 鈫?rows 鈫?cells 鈫?content) into
        // absolute-positioned legacy LayoutBoxes. Fragment offsets are relative
        // to the parent's content box, which AuroraFragmentConverter resolves.
        foreach (var child in frag.Children)
        {
            var childBox = AuroraFragmentConverter.ToLayoutBox(child, child.Element, box);
            box.Children.Add(childBox);
        }
    }
}