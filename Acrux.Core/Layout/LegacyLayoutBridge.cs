using Acrux.Core.Dom;
using SkiaSharp;

namespace Acrux.Core.Layout;

/// <summary>
/// [ARCHIVED 2026-08 — P2-1 pipeline decision]
/// Legacy bridge between the older LayoutObject hierarchy and the
/// Dom.LayoutBox painting pipeline. Builds a LayoutView from the DOM tree,
/// computes box geometry bottom-up, and exports Dom.LayoutBox values that
/// the PaintVisitor consumes.
///
/// INTENTIONALLY NOT WIRED: every frame routes through the modern layout
/// pipeline (see <see cref="IncrementalLayoutEngine"/>).
/// Kept compilable for reference and possible revival, but do NOT re-enable
/// without a fresh parity audit against the modern path — two-pipeline drift
/// was a root cause of the original snapshot-vs-browser mismatches (see
/// docs/porting_tracker.md §五).
/// </summary>
public static class LegacyLayoutBridge
{
    /// <summary>
    /// Build a LayoutView tree from the document and compute box geometry for
    /// every element. Populates each Element.LayoutBox so the painter works.
    /// </summary>
    public static LayoutView BuildAndLayout(Document document, float viewportWidth, float viewportHeight, float dpiScale = 1.0f)
    {
        var view = new LayoutView(document);
        var root = document.DocumentElement ?? document.Body;
        if (root != null)
            BuildTreeInternal(root, view, viewportWidth, viewportHeight);
        if (root != null)
            ComputeGeometryInternal(root, view, viewportWidth, viewportHeight, dpiScale, 0, 0, viewportWidth);
        if (root != null)
            ExportLayoutBoxes(root, view, null);
        return view;
    }

    private static void BuildTreeInternal(Element element, LayoutObject parent, float width, float height)
    {
        var lo = LayoutObject.CreateObject(element, element.ComputedStyle);
        if (lo != null)
        {
            lo.Parent = parent;
            parent.AddChild(lo);

            foreach (var child in element.Children)
            {
                if (child is TextNode textNode)
                {
                    var textLayout = new LayoutText(textNode, textNode.TextContent ?? "");
                    textLayout.Parent = lo;
                    lo.AddChild(textLayout);
                }
                else if (child is Element childEl)
                {
                    BuildTreeInternal(childEl, lo, width, height);
                }
            }
        }
    }

    private static void ComputeGeometryInternal(
        Element element,
        LayoutObject layoutObject,
        float viewportWidth, float viewportHeight, float dpiScale,
        float containingX, float containingY, float containingWidth)
    {
        // Match layout object children to DOM element children for recursion.
        int objIdx = 0;
        foreach (var child in element.Children)
        {
            if (objIdx >= layoutObject.Children.Count) break;

            var childObj = layoutObject.Children[objIdx];
            if (child is TextNode textNode && childObj is LayoutText textLayout)
            {
                ComputeTextGeometry(textLayout, containingX, containingY);
                objIdx++;
            }
            else if (child is Element childEl)
            {
                var box = ComputeBoxGeometry(childEl, containingX, containingY, containingWidth,
                    viewportWidth, viewportHeight);
                objIdx++;

                if (box == null)
                    continue;

                // Recurse with the child's content-box position.
                float childContentY = box.ContentBox.Bottom;
                float childContentX = box.ContentBox.Left;
                float childContentWidth = box.ContentBox.Width;
                ComputeGeometryInternal(childEl, childObj, viewportWidth, viewportHeight, dpiScale,
                    childContentX, childContentY, childContentWidth);
            }
        }
    }

    private static Dom.LayoutBox? ComputeBoxGeometry(
        Element element,
        float containingX, float containingY, float containingWidth,
        float viewportWidth, float viewportHeight)
    {
        var style = element.ComputedStyle;
        var parentStyle = element.ParentElement?.ComputedStyle;
        var box = new Dom.LayoutBox
        {
            Dimensions = new BoxDimensions { Style = style, Element = element }
        };

        if (style == null) return null;
        if (style.Display == DisplayType.None)
        {
            box.MarginBox = box.BorderBox = box.PaddingBox = box.ContentBox =
                new SKRect(containingX, containingY, containingX, containingY);
            element.LayoutBox = box;
            return box;
        }

        float font = style.FontSize;
        float rootFont = parentStyle?.FontSize ?? 16;

        float marginLeft = style.MarginLeft.ToPixels(font, rootFont, viewportWidth, viewportHeight);
        float marginRight = style.MarginRight.ToPixels(font, rootFont, viewportWidth, viewportHeight);
        float marginTop = style.MarginTop.ToPixels(font, rootFont, viewportWidth, viewportHeight);
        float marginBottom = style.MarginBottom.ToPixels(font, rootFont, viewportWidth, viewportHeight);

        float borderLeft = style.BorderLeftWidth;
        float borderRight = style.BorderRightWidth;
        float borderTop = style.BorderTopWidth;
        float borderBottom = style.BorderBottomWidth;

        float paddingLeft = style.PaddingLeft.ToPixels(font, rootFont, viewportWidth, viewportHeight);
        float paddingRight = style.PaddingRight.ToPixels(font, rootFont, viewportWidth, viewportHeight);
        float paddingTop = style.PaddingTop.ToPixels(font, rootFont, viewportWidth, viewportHeight);
        float paddingBottom = style.PaddingBottom.ToPixels(font, rootFont, viewportWidth, viewportHeight);

        float contentWidth;
        if (style.Width is PixelLength pw)
            contentWidth = pw.Value;
        else if (style.Width is PercentLength pct)
            contentWidth = pct.Value * containingWidth;
        else
            contentWidth = Math.Max(0, containingWidth - marginLeft - marginRight - borderLeft - borderRight - paddingLeft - paddingRight);

        float contentHeight = 0;
        if (style.Height is PixelLength ph)
            contentHeight = ph.Value;
        else if (style.Height is PercentLength pct2)
            contentHeight = pct2.Value * viewportHeight;

        if (style.BoxSizing == BoxSizingType.BorderBox)
        {
            contentWidth = Math.Max(0, contentWidth - borderLeft - borderRight - paddingLeft - paddingRight);
            contentHeight = Math.Max(0, contentHeight - borderTop - borderBottom - paddingTop - paddingBottom);
        }

        float borderBoxWidth = contentWidth + borderLeft + borderRight + paddingLeft + paddingRight;
        float borderBoxHeight = contentHeight + borderTop + borderBottom + paddingTop + paddingBottom;

        // Auto margins center block-level boxes.
        if (style.MarginLeft is AutoLength && style.MarginRight is AutoLength)
        {
            float remaining = containingWidth - marginLeft - marginRight - borderBoxWidth;
            if (remaining > 0 && style.Width is AutoLength)
            {
                marginLeft = remaining / 2;
                marginRight = remaining / 2;
            }
        }

        float x = containingX + marginLeft;
        float y = containingY + marginTop;

        box.MarginBox = new SKRect(x - marginLeft, y - marginTop,
            x + borderBoxWidth + marginRight, y + borderBoxHeight + marginBottom);
        box.BorderBox = new SKRect(x, y, x + borderBoxWidth, y + borderBoxHeight);
        box.PaddingBox = new SKRect(x + borderLeft, y + borderTop,
            x + borderBoxWidth - borderRight, y + borderBoxHeight - borderBottom);
        box.ContentBox = new SKRect(x + borderLeft + paddingLeft, y + borderTop + paddingTop,
            x + borderBoxWidth - borderRight - paddingRight, y + borderBoxHeight - borderBottom - paddingBottom);

        box.LineHeight = Fonts.LineBoxMetrics.GetLineHeight(style);

        // Create text runs for text node children.
        var lineRuns = new List<InlineRun>();
        var lines = new List<LineBox>();
        float textY = box.ContentBox.Top;
        float maxLineWidth = box.ContentBox.Width;
        foreach (var child in element.Children)
        {
            if (child is not TextNode textNode) continue;
            string textContent = textNode.TextContent ?? "";
            if (string.IsNullOrEmpty(textContent)) continue;

            float textWidth = TextMeasurer.Instance?.MeasureText(
                textContent, style.FontFamily, font, Core.Dom.FontWeight.Normal)
                ?? textContent.Length * font * 0.5f;

            var run = new InlineRun
            {
                Text = textContent,
                X = box.ContentBox.Left,
                Width = Math.Min(textWidth, Math.Max(0, maxLineWidth)),
                Height = font * 1.2f,
                Baseline = font,
                IsText = true,
                Node = textNode,
            };
            lineRuns.Add(run);

            var line = new LineBox
            {
                X = box.ContentBox.Left,
                Y = textY,
                Width = Math.Min(textWidth, Math.Max(0, maxLineWidth)),
                Height = font * 1.2f,
                Baseline = font,
            };
            line.Runs.Add(run);
            lines.Add(line);
            textY += font * 1.2f;
        }
        box.LineRuns = lineRuns.Count > 0 ? lineRuns : null;
        if (lines.Count > 0)
            box.Lines = lines;

        element.LayoutBox = box;
        return box;
    }

    private static void ComputeTextGeometry(LayoutText text, float x, float y)
    {
        if (text.Node is not TextNode node) return;
        string content = node.TextContent ?? "";
        float fontSize = 16;
        var parentEl = node.ParentElement;
        if (parentEl?.ComputedStyle != null)
            fontSize = parentEl.ComputedStyle.FontSize;

        float textWidth = content.Length * fontSize * 0.5f;
        if (Core.Layout.TextMeasurer.Instance != null)
        {
            textWidth = Core.Layout.TextMeasurer.Instance.MeasureText(
                content, parentEl?.ComputedStyle?.FontFamily ?? "Arial", fontSize, Core.Dom.FontWeight.Normal);
        }
        text.MinPreferredLogicalWidth = textWidth;
        text.MaxPreferredLogicalWidth = textWidth;
    }

    private static void ExportLayoutBoxes(Element root, LayoutObject layoutObject, Dom.LayoutBox? parentBox)
    {
        int objIdx = 0;
        foreach (var child in root.Children)
        {
            if (objIdx >= layoutObject.Children.Count) break;
            var childObj = layoutObject.Children[objIdx];
            if (child is Element childEl)
            {
                var childBox = childEl.LayoutBox;
                if (childBox != null)
                {
                    childBox.Parent = parentBox;
                    ExportLayoutBoxes(childEl, childObj, childBox);
                }
            }
            objIdx++;
        }
    }
}