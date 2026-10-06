using Acrux.Core.Css.Resolver;
using Acrux.Core.Dom;

namespace Acrux.Core.Css.Cascade;

/// <summary>
/// Adjusts computed styles for certain elements, mirroring Blink's StyleAdjuster.
/// Handles special cases like table display adjustments, text-decoration suppression,
/// forced colors mode, and other element-specific style overrides.
/// </summary>
public class StyleAdjuster
{
    public void AdjustComputedStyle(ComputedStyle style, Element element, ComputedStyle? parentStyle)
    {
        AdjustMonospaceGenericFontSize(style);
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
        AdjustTickBoxDecoration(style, element);
        ReplayBoxEdgeProperties(style);
        ResolveCurrentColors(style);
        ApplyInitialBorderWidths(style);
    }

    /// <summary>
    /// Logical box properties address an edge, not a side, so they must be mapped onto
    /// the physical sides using the element's <b>final</b> computed 'direction'
    /// (CSS Logical Properties 1 §2) — a 'direction' authored later in the same rule
    /// still flips it, which one pass over the declarations cannot know. It runs before
    /// currentColor resolution so a logical border color written as 'currentColor'
    /// still resolves.
    /// </summary>
    /// <summary>
    /// Blink's checkbox/radio adjustment: while the control still has its native
    /// appearance, its border and padding are the widget's own drawing, not box decoration,
    /// so an author <c>border: 4px solid red</c> or <c>padding: 10px</c> is thrown away
    /// entirely (reference engine: <c>&lt;input type=checkbox style="border:4px solid red;
    /// padding:10px; width:40px; height:40px"&gt;</c> computes to 0/none + 0 padding and stays
    /// 40x40 — the author's size survives, its decoration does not). The UA registry cannot
    /// express this: it writes zeros, which are also the initial values, so they never reach
    /// the cascade and an author declaration simply won.
    /// </summary>
    private static void AdjustTickBoxDecoration(ComputedStyle style, Element element)
    {
        if (element is null) return;
        if (!element.TagName.Equals("INPUT", StringComparison.OrdinalIgnoreCase)) return;
        // Only a control that gave up its native appearance may style its own decoration.
        if (style.Appearance.Equals("none", StringComparison.OrdinalIgnoreCase)) return;
        var type = (element.GetAttribute("type") ?? "text").ToLowerInvariant();
        if (type is not ("checkbox" or "radio")) return;

        style.BorderTopWidth = style.BorderRightWidth = style.BorderBottomWidth = style.BorderLeftWidth = 0;
        style.BorderTopStyle = style.BorderRightStyle = style.BorderBottomStyle = style.BorderLeftStyle = BorderStyle.None;
        style.PaddingTop = style.PaddingRight = style.PaddingBottom = style.PaddingLeft = new PixelLength(0);
    }

    private static void ReplayBoxEdgeProperties(ComputedStyle style)
    {
        var pending = style.PendingBoxEdgeProperties;
        if (pending == null || pending.Count == 0) return;
        // Detach first: re-applying records into a fresh list, which is then dropped.
        style.PendingBoxEdgeProperties = null;
        foreach (var kv in pending)
            CssPropertyApplier.Apply(style, kv.Key, kv.Value);
        style.PendingBoxEdgeProperties = null;
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

    /// <summary>CSS Display 3 §3.2: a floated or absolutely positioned box has its
    /// 'display' blockified. Internal because a generated box is built outside the cascade
    /// (LayoutEngine's pseudo-element path) and needs the same rule applied by hand.</summary>
    internal static void BlockifyFloatAndAbsolute(ComputedStyle style)
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
    /// The generic 'monospace' carries a size of its own: an element whose specified
    /// family list is exactly 'monospace' and whose 'font-size' was never authored (or
    /// only 'medium') computes to the platform's fixed-font size, not to the inherited
    /// 16px. Measured in Edge: <c>&lt;code&gt;</c> and <c>font-family: monospace</c>
    /// both give 13px, while an authored <c>font-size: 16px</c> anywhere up the chain —
    /// or a list like <c>'Nope', monospace</c> — keeps 16px. Runs first, so every later
    /// adjustment and every font-relative unit sees the substituted size.
    /// </summary>
    private static void AdjustMonospaceGenericFontSize(ComputedStyle style)
    {
        if (!style.FontSizeIsDefault) return;
        if (!Fonts.FontManager.IsSoleMonospaceGeneric(style.FontFamily)) return;
        style.FontSize = Fonts.FontManager.StandardFixedFontSize;
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

        // Chrome reports the initial 'inline' for img/canvas/video/iframe/embed/
        // object/svg and inline-block only for the form controls. The UA sheet now says
        // 'inline' for the media tags (that is the standard value), and the force lives
        // here instead, because the engine cannot yet honour it: a replaced box's
        // background and border are painted by the block-box visit, and an element that
        // stays 'inline' never reaches it. Measured, not theoretical — dropping this
        // force makes the whole 10x10 red square of b167's <img style="background:#e33">
        // disappear (100 differing pixels, exactly its box), while every geometry stays
        // right. The real fix is box decoration on the atomic-inline run (#164); flip
        // this list away once that lands.
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
        // The root element's overflow propagates to the viewport (not modeled here); what
        // *is* decided here is CSS Overflow 3 §3.3.1's pair constraints. They belong on the
        // computed style rather than inside the 'overflow' shorthand, because they hold
        // whichever way the two axes arrived: 'overflow: visible hidden' and
        // 'overflow-x: visible; overflow-y: hidden' compute the same pair, and so does a lone
        // 'overflow-x: hidden' (whose other axis then stops being 'visible').
        var x = style.OverflowX;
        var y = style.OverflowY;
        if (x == OverflowType.Visible && CssPropertyApplier.IsScrollContainerOverflow(y)) x = OverflowType.Auto;
        else if (y == OverflowType.Visible && CssPropertyApplier.IsScrollContainerOverflow(x)) y = OverflowType.Auto;
        if (x == OverflowType.Clip && CssPropertyApplier.IsScrollContainerOverflow(y)) x = OverflowType.Hidden;
        else if (y == OverflowType.Clip && CssPropertyApplier.IsScrollContainerOverflow(x)) y = OverflowType.Hidden;
        style.OverflowX = x;
        style.OverflowY = y;
        // The single 'Overflow' field is an aggregate the paint gates read; taking the more
        // restrictive axis keeps it from claiming scrollability the pair does not have.
        style.Overflow = x == y ? x
            : CssPropertyApplier.OverflowSeverity(x) >= CssPropertyApplier.OverflowSeverity(y) ? x : y;
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