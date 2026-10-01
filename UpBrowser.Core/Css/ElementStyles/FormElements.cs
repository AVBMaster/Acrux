using SkiaSharp;
using UpBrowser.Core.Dom;

namespace UpBrowser.Core.Css.ElementStyles;

public static class FormElements
{
    /// <summary>
    /// Whether this option or optgroup belongs to a select that renders a single line.
    ///
    /// Such a control paints its selected text and nothing else, so its options are not page
    /// text: they must not answer Ctrl+A or end up in the clipboard. They stay laid out
    /// though, because the control's own width is the widest options and an inline-block
    /// measures itself from its children — which is why this is a question for the consumers
    /// of text, not for layout. A multiple or sized select is a list box, and there the
    /// options really are rows of content.
    /// </summary>
    public static bool BelongsToClosedSelect(Element? element)
    {
        for (var p = element?.ParentElement; p != null; p = p.ParentElement)
        {
            var tag = p.TagName?.ToUpperInvariant();
            if (tag == "OPTGROUP") continue;
            if (tag != "SELECT") return false;
            if (p.HasAttribute("multiple")) return false;
            return !(int.TryParse(p.GetAttribute("size"), out var rows) && rows > 1);
        }
        return false;
    }

    public static void Apply(ComputedStyle style, string tagName, Element? element = null)
    {
        string inputType = "";
        if (tagName == "input" && element != null)
            inputType = (element.GetAttribute("type") ?? "text").ToLowerInvariant();

        switch (tagName.ToLowerInvariant())
        {
            case "input":
                style.Display = DisplayType.InlineBlock;
                style.Appearance = "auto";
                style.PaddingTop = new PixelLength(1);
                style.PaddingBottom = new PixelLength(1);
                style.PaddingLeft = new PixelLength(2);
                style.PaddingRight = new PixelLength(2);
                style.BorderTopWidth = 2;
                style.BorderRightWidth = 2;
                style.BorderBottomWidth = 2;
                style.BorderLeftWidth = 2;
                style.BorderTopStyle = BorderStyle.Inset;
                style.BorderRightStyle = BorderStyle.Inset;
                style.BorderBottomStyle = BorderStyle.Inset;
                style.BorderLeftStyle = BorderStyle.Inset;
                style.BackgroundColor = SKColors.White;
                style.Color = SKColors.Black;
                style.FontSize = 13.3333f;
                style.FontFamily = "Arial";
                style.FontWeight = FontWeight.Normal;
                style.Cursor = "text";
                style.BoxSizing = BoxSizingType.BorderBox;
                style.LineHeight = 1.0f;
                Fonts.LineBoxMetrics.SetMultiplier(style, 1.0f);
                style.LetterSpacing = 0;
                style.WordSpacing = 0;
                style.TextIndent = 0;
                style.TextTransform = "none";

                switch (inputType)
                {
                    case "checkbox":
                        style.Width = new PixelLength(13);
                        style.Height = new PixelLength(13);
                        style.BorderTopWidth = 0;
                        style.BorderRightWidth = 0;
                        style.BorderBottomWidth = 0;
                        style.BorderLeftWidth = 0;
                        style.PaddingTop = new PixelLength(0);
                        style.PaddingBottom = new PixelLength(0);
                        style.PaddingLeft = new PixelLength(0);
                        style.PaddingRight = new PixelLength(0);
                        style.Cursor = "default";
                        style.BackgroundColor = SKColors.Transparent;
                        break;
                    case "radio":
                        style.Width = new PixelLength(13);
                        style.Height = new PixelLength(13);
                        style.BorderTopWidth = 0;
                        style.BorderRightWidth = 0;
                        style.BorderBottomWidth = 0;
                        style.BorderLeftWidth = 0;
                        style.PaddingTop = new PixelLength(0);
                        style.PaddingBottom = new PixelLength(0);
                        style.PaddingLeft = new PixelLength(0);
                        style.PaddingRight = new PixelLength(0);
                        style.Cursor = "default";
                        style.BackgroundColor = SKColors.Transparent;
                        break;
                    case "button":
                    case "submit":
                    case "reset":
                        style.BorderTopStyle = BorderStyle.Outset;
                        style.BorderRightStyle = BorderStyle.Outset;
                        style.BorderBottomStyle = BorderStyle.Outset;
                        style.BorderLeftStyle = BorderStyle.Outset;
                        style.PaddingTop = new PixelLength(1);
                        style.PaddingBottom = new PixelLength(1);
                        style.PaddingLeft = new PixelLength(6);
                        style.PaddingRight = new PixelLength(6);
                        style.BackgroundColor = SKColor.Parse("#E1E1E1");
                        style.Cursor = "default";
                        style.WhiteSpace = WhiteSpaceMode.Nowrap;
                        break;
                    case "file":
                        style.Cursor = "default";
                        break;
                    case "image":
                        style.Cursor = "pointer";
                        style.BorderTopWidth = 0;
                        style.BorderRightWidth = 0;
                        style.BorderBottomWidth = 0;
                        style.BorderLeftWidth = 0;
                        style.PaddingTop = new PixelLength(0);
                        style.PaddingBottom = new PixelLength(0);
                        style.PaddingLeft = new PixelLength(0);
                        style.PaddingRight = new PixelLength(0);
                        style.BackgroundColor = SKColors.Transparent;
                        break;
                    case "hidden":
                        style.Display = DisplayType.None;
                        break;
                    case "range":
                        style.BorderTopWidth = 0;
                        style.BorderRightWidth = 0;
                        style.BorderBottomWidth = 0;
                        style.BorderLeftWidth = 0;
                        style.PaddingTop = new PixelLength(0);
                        style.PaddingBottom = new PixelLength(0);
                        style.PaddingLeft = new PixelLength(0);
                        style.PaddingRight = new PixelLength(0);
                        style.Cursor = "default";
                        style.BackgroundColor = SKColors.Transparent;
                        break;
                    case "color":
                        style.Width = new PixelLength(44);
                        style.Height = new PixelLength(23);
                        style.BorderTopWidth = 1;
                        style.BorderRightWidth = 1;
                        style.BorderBottomWidth = 1;
                        style.BorderLeftWidth = 1;
                        style.BorderTopStyle = BorderStyle.Solid;
                        style.BorderRightStyle = BorderStyle.Solid;
                        style.BorderBottomStyle = BorderStyle.Solid;
                        style.BorderLeftStyle = BorderStyle.Solid;
                        style.PaddingTop = new PixelLength(1);
                        style.PaddingBottom = new PixelLength(1);
                        style.PaddingLeft = new PixelLength(2);
                        style.PaddingRight = new PixelLength(2);
                        style.BackgroundColor = SKColor.Parse("#E1E1E1");
                        style.Cursor = "default";
                        break;
                    case "search":
                        break;
                    case "password":
                        style.Cursor = "text";
                        break;
                }
                break;

            case "textarea":
                style.Display = DisplayType.InlineBlock;
                style.Appearance = "auto";
                style.BoxSizing = BoxSizingType.BorderBox;
                style.BorderTopWidth = 1;
                style.BorderRightWidth = 1;
                style.BorderBottomWidth = 1;
                style.BorderLeftWidth = 1;
                style.BorderTopStyle = BorderStyle.Inset;
                style.BorderRightStyle = BorderStyle.Inset;
                style.BorderBottomStyle = BorderStyle.Inset;
                style.BorderLeftStyle = BorderStyle.Inset;
                style.PaddingTop = new PixelLength(2);
                style.PaddingBottom = new PixelLength(2);
                style.PaddingLeft = new PixelLength(2);
                style.PaddingRight = new PixelLength(2);
                style.BackgroundColor = SKColors.White;
                style.Color = SKColors.Black;
                style.FontSize = 13.3333f;
                style.FontFamily = "monospace";
                style.WhiteSpace = WhiteSpaceMode.PreWrap;
                style.OverflowWrap = OverflowWrapMode.BreakWord;
                style.Cursor = "text";
                style.Resize = ResizeType.Both;
                break;

            case "select":
                style.Display = DisplayType.InlineBlock;
                style.Appearance = "auto";
                style.BoxSizing = BoxSizingType.BorderBox;
                style.BorderTopWidth = 1;
                style.BorderRightWidth = 1;
                style.BorderBottomWidth = 1;
                style.BorderLeftWidth = 1;
                style.BorderTopStyle = BorderStyle.Inset;
                style.BorderRightStyle = BorderStyle.Inset;
                style.BorderBottomStyle = BorderStyle.Inset;
                style.BorderLeftStyle = BorderStyle.Inset;
                style.PaddingTop = new PixelLength(1);
                style.PaddingBottom = new PixelLength(1);
                style.PaddingLeft = new PixelLength(2);
                style.PaddingRight = new PixelLength(2);
                style.BackgroundColor = SKColors.White;
                style.Color = SKColors.Black;
                style.FontSize = 13.3333f;
                style.FontFamily = "Arial";
                style.Cursor = "default";
                style.WhiteSpace = WhiteSpaceMode.Nowrap;
                break;

            case "button":
                style.Display = DisplayType.InlineBlock;
                style.Appearance = "auto";
                style.BoxSizing = BoxSizingType.BorderBox;
                style.BorderTopWidth = 2;
                style.BorderRightWidth = 2;
                style.BorderBottomWidth = 2;
                style.BorderLeftWidth = 2;
                style.BorderTopStyle = BorderStyle.Outset;
                style.BorderRightStyle = BorderStyle.Outset;
                style.BorderBottomStyle = BorderStyle.Outset;
                style.BorderLeftStyle = BorderStyle.Outset;
                style.PaddingTop = new PixelLength(1);
                style.PaddingBottom = new PixelLength(1);
                style.PaddingLeft = new PixelLength(6);
                style.PaddingRight = new PixelLength(6);
                style.BackgroundColor = SKColor.Parse("#E1E1E1");
                style.Color = SKColors.Black;
                style.FontSize = 13.3333f;
                style.FontWeight = FontWeight.Normal;
                style.FontFamily = "Arial";
                style.TextAlign = TextAlignType.Center;
                style.Cursor = "default";
                style.WhiteSpace = WhiteSpaceMode.Nowrap;
                break;

            case "fieldset":
                style.Display = DisplayType.Block;
                style.BorderTopWidth = 2;
                style.BorderRightWidth = 2;
                style.BorderBottomWidth = 2;
                style.BorderLeftWidth = 2;
                style.BorderTopStyle = BorderStyle.Groove;
                style.BorderRightStyle = BorderStyle.Groove;
                style.BorderBottomStyle = BorderStyle.Groove;
                style.BorderLeftStyle = BorderStyle.Groove;
                style.PaddingTop = new PixelLength(6);
                style.PaddingBottom = new PixelLength(12);
                style.PaddingLeft = new PixelLength(10);
                style.PaddingRight = new PixelLength(10);
                style.MarginLeft = new PixelLength(2);
                style.MarginRight = new PixelLength(2);
                break;

            case "legend":
                style.Display = DisplayType.Block;
                style.PaddingLeft = new PixelLength(2);
                style.PaddingRight = new PixelLength(2);
                break;

            case "label":
                style.Display = DisplayType.Inline;
                style.Cursor = "default";
                break;

            case "output":
                style.Display = DisplayType.Inline;
                break;

            case "datalist":
                style.Display = DisplayType.None;
                break;

            case "optgroup":
                style.Display = DisplayType.Block;
                style.FontWeight = FontWeight.Bold;
                break;

            case "option":
                style.Display = DisplayType.Block;
                break;

            case "form":
                style.Display = DisplayType.Block;
                style.MarginTop = new PixelLength(0);
                style.MarginBottom = new PixelLength(0);
                break;

            case "progress":
                style.Display = DisplayType.InlineBlock;
                style.Appearance = "auto";
                style.BoxSizing = BoxSizingType.BorderBox;
                style.VerticalAlign = VerticalAlignType.Middle;
                style.Width = new PixelLength(160);
                style.Height = new PixelLength(16);
                style.BorderTopWidth = 0;
                style.BorderRightWidth = 0;
                style.BorderBottomWidth = 0;
                style.BorderLeftWidth = 0;
                style.BackgroundColor = SKColors.Transparent;
                break;

            case "meter":
                style.Display = DisplayType.InlineBlock;
                style.Appearance = "auto";
                style.BoxSizing = BoxSizingType.BorderBox;
                style.VerticalAlign = VerticalAlignType.Middle;
                style.Width = new PixelLength(160);
                style.Height = new PixelLength(16);
                style.BorderTopWidth = 0;
                style.BorderRightWidth = 0;
                style.BorderBottomWidth = 0;
                style.BorderLeftWidth = 0;
                style.BackgroundColor = SKColors.Transparent;
                break;
        }
    }
}
