using UpBrowser.Core.Css.Resolver;
using UpBrowser.Core.Dom;

namespace UpBrowser.Core.Css.Cascade;

/// <summary>
/// Adjusts computed styles for certain elements, mirroring Blink's StyleAdjuster.
/// Handles special cases like table display adjustments, text-decoration suppression,
/// forced colors mode, and other element-specific style overrides.
/// </summary>
public class StyleAdjuster
{
    public void AdjustComputedStyle(ComputedStyle style, Element element, ComputedStyle? parentStyle)
    {
        AdjustDisplayForElement(style, element);
        DefaultTableBoxSizing(style);
        DefaultTableCellVerticalAlign(style);
        BlockifyFloatAndAbsolute(style);
        BlockifyFlexGridItems(style, element, parentStyle);
        AdjustOverflow(style);
        AdjustForTextElements(style, element);
        AdjustForReplacedElements(style, element);
        AdjustTouchAction(style, element);
        AdjustZIndex(style, parentStyle);
        ResolveCurrentColors(style);
        ApplyInitialBorderWidths(style);
    }

    /// <summary>
    /// The initial value of every border width and of 'outline-width' is medium
    /// (CSS Backgrounds 3 §4, CSS UI 4 §5). Those widths still start at zero in the style
    /// object so a box that never mentions borders takes no space, so once the cascade is
    /// done a box that *did* ask for a visible border style gets the medium width it was
    /// promised. An authored 'border-width: 0' keeps its zero because it is recorded as
    /// authored.
    /// </summary>
    private static void ApplyInitialBorderWidths(ComputedStyle style)
    {
        uint authored = style.AuthoredWidthSlots;
        float medium = CssPropertyApplier.MediumBorderWidth;
        if ((authored & (uint)ComputedStyle.CurrentColorSlot.BorderTop) == 0 && style.BorderTopStyle != BorderStyle.None)
            style.BorderTopWidth = medium;
        if ((authored & (uint)ComputedStyle.CurrentColorSlot.BorderRight) == 0 && style.BorderRightStyle != BorderStyle.None)
            style.BorderRightWidth = medium;
        if ((authored & (uint)ComputedStyle.CurrentColorSlot.BorderBottom) == 0 && style.BorderBottomStyle != BorderStyle.None)
            style.BorderBottomWidth = medium;
        if ((authored & (uint)ComputedStyle.CurrentColorSlot.BorderLeft) == 0 && style.BorderLeftStyle != BorderStyle.None)
            style.BorderLeftWidth = medium;
        // 'outline-width' keeps its initial medium even while 'outline-style: none', because
        // that is what its computed value reports. A border width reads back as zero once its
        // style is none, which is why the four sides above are conditional and this is not.
        if ((authored & (uint)ComputedStyle.CurrentColorSlot.Outline) == 0)
            style.OutlineWidth = medium;
    }

    /// <summary>
    /// CSS 2.1 §E.1: 'z-index' applies only to positioned elements; flex and grid
    /// items also honor it with position:static (css-flexbox §4.3, css-grid §6).
    /// Elsewhere it computes to auto so the element cannot escape the paint order.
    /// </summary>
    /// <summary>
    /// CSS 2.1 §9.4.1 / §9.4.2 (and CSS Positioned Layout §6.2): a floated or
    /// absolutely positioned box is blockified, so an inline element that is given
    /// 'float' or 'position:absolute' honors its width and height instead of
    /// shrinking to its text.
    /// </summary>
    /// <summary>
    /// A table box's specified 'width' is its border-box width (CSS 2.1 §17.5.2.1),
    /// which is what 'box-sizing: border-box' means for every other box. That is
    /// therefore the default for tables, unless the author declared 'box-sizing'.
    /// </summary>
    /// <summary>
    /// CSS 2.1 §17.5.2.6: a table cell that declares nothing aligns its content to
    /// the middle of the cell, not to the baseline of the line as other boxes do.
    /// </summary>
    private static void DefaultTableCellVerticalAlign(ComputedStyle style)
    {
        if (style.VerticalAlignIsAuthored) return;
        if (style.Display == DisplayType.TableCell)
            style.VerticalAlign = VerticalAlignType.Middle;
    }

    private static void DefaultTableBoxSizing(ComputedStyle style)
    {
        if (style.BoxSizingIsAuthored) return;
        if (style.Display == DisplayType.Table)
            style.BoxSizing = BoxSizingType.BorderBox;
    }

    private static void BlockifyFloatAndAbsolute(ComputedStyle style)
    {
        bool isFloat = style.Float != FloatType.None;
        bool isAbsolute = style.Position is PositionType.Absolute or PositionType.Fixed;
        if (!isFloat && !isAbsolute)
            return;
        style.Display = style.Display switch
        {
            DisplayType.Inline or DisplayType.InlineBlock => DisplayType.Block,
            DisplayType.InlineFlex => DisplayType.Flex,
            DisplayType.InlineGrid => DisplayType.Grid,
            _ => style.Display,
        };
    }

    private static void AdjustZIndex(ComputedStyle style, ComputedStyle? parentStyle)
    {
        if (style.Position == PositionType.Static)
        {
            bool parentIsFlexOrGrid = parentStyle?.Display is DisplayType.Flex or DisplayType.InlineFlex
                or DisplayType.Grid or DisplayType.InlineGrid;
            if (!parentIsFlexOrGrid)
                style.ZIndex = null;
        }
    }

    /// <summary>
    /// `currentcolor` can only be resolved once inheritance has produced the
    /// used 'color' (CSS Color 3 §4.4): the cascade records which color slots
    /// carried the keyword and the values are substituted here.
    /// </summary>
    private static void ResolveCurrentColors(ComputedStyle style)
    {
        uint slots = style.CurrentColorSlots;
        if (slots == 0) return;
        var c = style.Color;
        if ((slots & (uint)ComputedStyle.CurrentColorSlot.BorderTop) != 0) style.BorderTopColor = c;
        if ((slots & (uint)ComputedStyle.CurrentColorSlot.BorderRight) != 0) style.BorderRightColor = c;
        if ((slots & (uint)ComputedStyle.CurrentColorSlot.BorderBottom) != 0) style.BorderBottomColor = c;
        if ((slots & (uint)ComputedStyle.CurrentColorSlot.BorderLeft) != 0) style.BorderLeftColor = c;
        if ((slots & (uint)ComputedStyle.CurrentColorSlot.Outline) != 0) style.OutlineColor = c;
        if ((slots & (uint)ComputedStyle.CurrentColorSlot.TextDecoration) != 0) style.TextDecorationColor = c;
        if ((slots & (uint)ComputedStyle.CurrentColorSlot.ColumnRule) != 0) style.ColumnRuleColor = c;
        if ((slots & (uint)ComputedStyle.CurrentColorSlot.Caret) != 0) style.CaretColor = c;
    }

    /// <summary>
    /// CSS Flexbox §4.1 / Grid §5: in-flow children of a flex or grid container
    /// are blockified (inline → block, inline-flex → flex, …) and cannot be
    /// inline-level. Out-of-flow children keep their display.
    /// </summary>
    private static void BlockifyFlexGridItems(ComputedStyle style, Element element, ComputedStyle? parentStyle)
    {
        if (parentStyle == null) return;
        if (parentStyle.Display is not (DisplayType.Flex or DisplayType.Grid
            or DisplayType.InlineFlex or DisplayType.InlineGrid))
            return;
        if (style.Position is PositionType.Absolute or PositionType.Fixed) return;
        if (element.ParentNode is not Element parent || !ReferenceEquals(parent.ComputedStyle, parentStyle)) return;

        style.Display = style.Display switch
        {
            DisplayType.Inline or DisplayType.InlineBlock => DisplayType.Block,
            DisplayType.InlineFlex => DisplayType.Flex,
            DisplayType.InlineGrid => DisplayType.Grid,
            _ => style.Display,
        };
    }

    private static void AdjustDisplayForElement(ComputedStyle style, Element element)
    {
        string tag = element.TagName.ToUpperInvariant();

        // Table internal elements should not be display:none at the UA level
        // But if author set display:none, honor it

        // <td> defaults to table-cell
        if (tag is "TD" or "TH" && style.Display == DisplayType.Inline)
            style.Display = DisplayType.TableCell;

        // <tr> defaults to table-row
        if (tag == "TR" && style.Display == DisplayType.Inline)
            style.Display = DisplayType.TableRow;

        // <table> defaults to table
        if (tag == "TABLE" && style.Display == DisplayType.Inline)
            style.Display = DisplayType.Table;

        // <li> defaults to list-item
        if (tag == "LI" && style.Display == DisplayType.Inline)
            style.Display = DisplayType.ListItem;

        // <img>, <video>, <canvas> are inline-block by default
        if (tag is "IMG" or "VIDEO" or "CANVAS" or "IFRAME" or "EMBED" or "OBJECT" or "INPUT" or "TEXTAREA" or "SELECT" or "BUTTON")
        {
            if (style.Display == DisplayType.Inline)
                style.Display = DisplayType.InlineBlock;
        }

        // Positioned elements and floats create block formatting contexts
        if (style.Position != PositionType.Static && style.Position != PositionType.Relative)
        {
            if (style.Display == DisplayType.Inline)
                style.Display = DisplayType.InlineBlock;
        }

        // Top layer elements (dialog[open], fullscreen) get block display
        if (tag == "DIALOG" && element.HasAttribute("open"))
        {
            style.Display = DisplayType.Block;
        }
    }

    private static void AdjustOverflow(ComputedStyle style)
    {
        // Propagate visible overflow to the viewport
        // The root element's overflow becomes the viewport's overflow
    }

    private static void AdjustForTextElements(ComputedStyle style, Element element)
    {
        // Replaced elements and floated/positioned elements suppress text-decoration
        // propagation per CSS Text Decoration spec
        string tag = element.TagName.ToUpperInvariant();
        if (tag is "IMG" or "VIDEO" or "CANVAS" or "IFRAME" or "EMBED" or "OBJECT" or "INPUT" or "TEXTAREA" or "SELECT")
        {
            // These elements are not affected by ancestor text-decoration
        }
    }

    private static void AdjustForReplacedElements(ComputedStyle style, Element element)
    {
        // Ensure replaced elements have intrinsic sizing behavior
        string tag = element.TagName.ToUpperInvariant();
        if (tag is "IMG" or "VIDEO" or "CANVAS" or "IFRAME")
        {
            // Set intrinsic aspect ratio if applicable
        }
    }

    private static void AdjustTouchAction(ComputedStyle style, Element element)
    {
        // Touch-action: manipulation for root elements
        if (element.Parent is Document)
        {
            // Default touch-action for root is manipulation
        }
    }
}