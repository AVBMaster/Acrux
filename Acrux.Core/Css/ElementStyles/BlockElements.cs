using SkiaSharp;
using Acrux.Core.Dom;

namespace Acrux.Core.Css.ElementStyles;

public static class BlockElements
{
    public static void Apply(ComputedStyle style, string tagName)
    {
        switch (tagName)
        {
            // 通用块级容器
            case "div":
                style.Display = DisplayType.Block;
                break;
            case "p":
                style.Display = DisplayType.Block;
                style.MarginTop = new PixelLength(12);
                style.MarginBottom = new PixelLength(12);
                break;

            // 标题元素
            case "h1":
                style.Display = DisplayType.Block;
                style.FontSize = 32;
                style.FontWeight = FontWeight.Bold;
                style.MarginTop = new PixelLength(21);
                style.MarginBottom = new PixelLength(0);
                break;
            case "h2":
                style.Display = DisplayType.Block;
                style.FontSize = 24;
                style.FontWeight = FontWeight.Bold;
                style.MarginTop = new PixelLength(20);
                style.MarginBottom = new PixelLength(0);
                break;
            case "h3":
                style.Display = DisplayType.Block;
                style.FontSize = 20;
                style.FontWeight = FontWeight.Bold;
                style.MarginTop = new PixelLength(20);
                style.MarginBottom = new PixelLength(0);
                break;
            case "h4":
                style.Display = DisplayType.Block;
                style.FontSize = 18;
                style.FontWeight = FontWeight.Bold;
                style.MarginTop = new PixelLength(24);
                style.MarginBottom = new PixelLength(0);
                break;
            case "h5":
                style.Display = DisplayType.Block;
                style.FontSize = 16;
                style.FontWeight = FontWeight.Bold;
                style.MarginTop = new PixelLength(27);
                style.MarginBottom = new PixelLength(0);
                break;
            case "h6":
                style.Display = DisplayType.Block;
                style.FontSize = 14;
                style.FontWeight = FontWeight.Bold;
                style.MarginTop = new PixelLength(33);
                style.MarginBottom = new PixelLength(0);
                break;

            // 列表元素
            case "ul":
                style.Display = DisplayType.Block;
                style.MarginTop = new PixelLength(16);
                style.MarginBottom = new PixelLength(16);
                style.PaddingLeft = new PixelLength(40);
                break;
            case "ol":
                style.Display = DisplayType.Block;
                style.MarginTop = new PixelLength(16);
                style.MarginBottom = new PixelLength(16);
                style.PaddingLeft = new PixelLength(40);
                style.ListStyleType = ListStyleType.Decimal;
                break;
            case "li":
                style.Display = DisplayType.ListItem;
                style.MarginTop = new PixelLength(0);
                style.MarginBottom = new PixelLength(0);
                break;
            case "dl":
                style.Display = DisplayType.Block;
                style.MarginTop = new PixelLength(12);
                style.MarginBottom = new PixelLength(12);
                break;
            case "dt":
                style.Display = DisplayType.Block;
                style.FontWeight = FontWeight.Bold;
                style.MarginTop = new PixelLength(12);
                break;
            case "dd":
                style.Display = DisplayType.Block;
                style.MarginLeft = new PixelLength(40);
                style.MarginBottom = new PixelLength(12);
                break;

            // 表格元素
            case "table":
                style.Display = DisplayType.Table;
                // The UA stylesheet owns border-collapse/-spacing and the table's
                // margins; a phantom 1px gray border here used to inflate every
                // table box by 2px on both axes.
                style.BorderSpacing = 2;
                break;
            case "thead":
                style.Display = DisplayType.TableHeaderGroup;
                style.VerticalAlign = VerticalAlignType.Middle;
                break;
            case "tbody":
                style.Display = DisplayType.TableRowGroup;
                style.VerticalAlign = VerticalAlignType.Middle;
                break;
            case "tfoot":
                style.Display = DisplayType.TableFooterGroup;
                style.VerticalAlign = VerticalAlignType.Middle;
                break;
            case "tr":
                style.Display = DisplayType.TableRow;
                style.VerticalAlign = VerticalAlignType.Middle;
                break;
            case "th":
                style.Display = DisplayType.TableCell;
                style.FontWeight = FontWeight.Bold;
                style.TextAlign = TextAlignType.Center;
                style.VerticalAlign = VerticalAlignType.Middle;
                style.PaddingTop = new PixelLength(1);
                style.PaddingBottom = new PixelLength(1);
                style.PaddingLeft = new PixelLength(1);
                style.PaddingRight = new PixelLength(1);
                // No border: HTML tables render without cell borders unless the page
                // asks for them, and a phantom 1px border inflates every cell box by
                // 2px on each axis (see the table layout tests in docs/).
                break;
            case "td":
                style.Display = DisplayType.TableCell;
                style.VerticalAlign = VerticalAlignType.Middle;
                style.PaddingTop = new PixelLength(1);
                style.PaddingBottom = new PixelLength(1);
                style.PaddingLeft = new PixelLength(1);
                style.PaddingRight = new PixelLength(1);
                break;
            case "caption":
                // HTML UA stylesheet: a caption is only centered; it carries no
                // padding of its own.
                style.Display = DisplayType.TableCaption;
                style.TextAlign = TextAlignType.Center;
                break;
            case "colgroup":
                style.Display = DisplayType.TableColumnGroup;
                break;
            case "col":
                style.Display = DisplayType.TableColumn;
                break;

            // 格式化文本
            case "pre":
                // HTML UA stylesheet defaults: monospace, preserved whitespace and a
                // one-em block margin. No padding, border or background (a page that
                // wants a code block look styles it).
                style.Display = DisplayType.Block;
                style.FontFamily = "monospace";
                style.WhiteSpace = WhiteSpaceMode.Pre;
                style.MarginTop = new PixelLength(16);
                style.MarginBottom = new PixelLength(16);
                break;

            // 引用和注释
            case "blockquote":
                style.Display = DisplayType.Block;
                style.MarginTop = new PixelLength(12);
                style.MarginBottom = new PixelLength(12);
                style.MarginLeft = new PixelLength(40);
                style.MarginRight = new PixelLength(40);
                style.PaddingTop = new PixelLength(8);
                style.PaddingBottom = new PixelLength(8);
                style.PaddingLeft = new PixelLength(16);
                style.PaddingRight = new PixelLength(16);
                style.BorderLeftWidth = 4;
                style.BorderLeftColor = SKColor.Parse("#E0E0E0");
                break;
            case "address":
                style.Display = DisplayType.Block;
                style.FontStyle = FontStyleType.Italic;
                style.MarginTop = new PixelLength(12);
                style.MarginBottom = new PixelLength(12);
                break;

            // HTML5 语义化元素
            case "article":
                style.Display = DisplayType.Block;
                style.MarginTop = new PixelLength(12);
                style.MarginBottom = new PixelLength(12);
                break;
            case "aside":
                style.Display = DisplayType.Block;
                style.MarginTop = new PixelLength(12);
                style.MarginBottom = new PixelLength(12);
                break;
            case "main":
                style.Display = DisplayType.Block;
                break;
            case "section":
                style.Display = DisplayType.Block;
                style.MarginTop = new PixelLength(12);
                style.MarginBottom = new PixelLength(12);
                break;
            case "nav":
                style.Display = DisplayType.Block;
                break;
            case "header":
                style.Display = DisplayType.Block;
                style.MarginBottom = new PixelLength(12);
                break;
            case "footer":
                style.Display = DisplayType.Block;
                style.MarginTop = new PixelLength(12);
                break;
            case "figure":
                style.Display = DisplayType.Block;
                style.MarginTop = new PixelLength(16);
                style.MarginBottom = new PixelLength(16);
                style.MarginLeft = new PixelLength(40);
                style.MarginRight = new PixelLength(40);
                break;
            case "figcaption":
                style.Display = DisplayType.Block;
                style.TextAlign = TextAlignType.Center;
                style.FontSize = 14;
                style.Color = SKColor.Parse("#808080");
                style.MarginTop = new PixelLength(8);
                break;

            // 交互元素
            case "details":
                style.Display = DisplayType.Block;
                style.PaddingTop = new PixelLength(8);
                style.PaddingBottom = new PixelLength(8);
                style.PaddingLeft = new PixelLength(8);
                style.PaddingRight = new PixelLength(8);
                style.BorderTopWidth = 1;
                style.BorderBottomWidth = 1;
                style.BorderLeftWidth = 1;
                style.BorderRightWidth = 1;
                style.BorderTopColor = SKColor.Parse("#CCCCCC");
                style.BorderBottomColor = SKColor.Parse("#CCCCCC");
                style.BorderLeftColor = SKColor.Parse("#CCCCCC");
                style.BorderRightColor = SKColor.Parse("#CCCCCC");
                break;
            case "summary":
                style.Display = DisplayType.Block;
                style.FontWeight = FontWeight.Bold;
                style.Cursor = "pointer";
                style.PaddingLeft = new PixelLength(16);
                break;
            case "dialog":
                style.Display = DisplayType.Block;
                style.Position = PositionType.Absolute;
                style.MarginTop = new PixelLength(0);
                style.MarginBottom = new PixelLength(0);
                style.MarginLeft = new PixelLength(0);
                style.MarginRight = new PixelLength(0);
                style.PaddingTop = new PixelLength(16);
                style.PaddingBottom = new PixelLength(16);
                style.PaddingLeft = new PixelLength(16);
                style.PaddingRight = new PixelLength(16);
                style.BorderTopWidth = 1;
                style.BorderBottomWidth = 1;
                style.BorderLeftWidth = 1;
                style.BorderRightWidth = 1;
                style.BorderTopStyle = BorderStyle.Solid;
                style.BorderRightStyle = BorderStyle.Solid;
                style.BorderBottomStyle = BorderStyle.Solid;
                style.BorderLeftStyle = BorderStyle.Solid;
                style.BorderTopColor = SKColors.Black;
                style.BorderBottomColor = SKColors.Black;
                style.BorderLeftColor = SKColors.Black;
                style.BorderRightColor = SKColors.Black;
                style.BackgroundColor = SKColors.White;
                break;

            // 分隔线
            case "hr":
                style.Display = DisplayType.Block;
                style.Height = new PixelLength(0);
                style.BorderTopWidth = 1;
                style.BorderTopStyle = BorderStyle.Inset;
                style.BorderTopColor = SKColor.Parse("#808080");
                style.BorderRightWidth = 0;
                style.BorderBottomWidth = 1;
                style.BorderBottomStyle = BorderStyle.Inset;
                style.BorderBottomColor = SKColor.Parse("#808080");
                style.BorderLeftWidth = 0;
                style.MarginTop = new PixelLength(8);
                style.MarginBottom = new PixelLength(8);
                style.MarginLeft = new PixelLength(0);
                style.MarginRight = new PixelLength(0);
                style.Overflow = OverflowType.Hidden;
                break;

            // 其他块级元素
            case "center":
                style.Display = DisplayType.Block;
                style.TextAlign = TextAlignType.Center;
                break;
            case "dir":
                style.Display = DisplayType.Block;
                style.MarginTop = new PixelLength(12);
                style.MarginBottom = new PixelLength(12);
                style.PaddingLeft = new PixelLength(40);
                break;
            case "fieldset":
                style.Display = DisplayType.Block;
                style.MarginTop = new PixelLength(0);
                style.MarginBottom = new PixelLength(8);
                style.MarginRight = new PixelLength(2);
                style.PaddingTop = new PixelLength(6);
                style.PaddingBottom = new PixelLength(10);
                style.PaddingLeft = new PixelLength(12);
                style.PaddingRight = new PixelLength(12);
                style.BorderTopWidth = 1;
                style.BorderBottomWidth = 1;
                style.BorderLeftWidth = 1;
                style.BorderRightWidth = 1;
                style.BorderTopStyle = BorderStyle.Groove;
                style.BorderRightStyle = BorderStyle.Groove;
                style.BorderBottomStyle = BorderStyle.Groove;
                style.BorderLeftStyle = BorderStyle.Groove;
                style.BorderTopColor = SKColor.Parse("#BABABA");
                style.BorderBottomColor = SKColor.Parse("#BABABA");
                style.BorderLeftColor = SKColor.Parse("#BABABA");
                style.BorderRightColor = SKColor.Parse("#BABABA");
                break;
            case "legend":
                style.Display = DisplayType.Block;
                style.PaddingLeft = new PixelLength(4);
                style.PaddingRight = new PixelLength(4);
                break;
            case "listing":
                style.Display = DisplayType.Block;
                style.FontFamily = "monospace";
                style.WhiteSpace = WhiteSpaceMode.Pre;
                style.MarginTop = new PixelLength(12);
                style.MarginBottom = new PixelLength(12);
                break;
            case "menu":
                style.Display = DisplayType.Block;
                style.MarginTop = new PixelLength(12);
                style.MarginBottom = new PixelLength(12);
                style.PaddingLeft = new PixelLength(40);
                break;
            case "plaintext":
                style.Display = DisplayType.Block;
                style.FontFamily = "monospace";
                style.WhiteSpace = WhiteSpaceMode.Pre;
                break;
            case "xmp":
                style.Display = DisplayType.Block;
                style.FontFamily = "monospace";
                style.WhiteSpace = WhiteSpaceMode.Pre;
                style.MarginTop = new PixelLength(12);
                style.MarginBottom = new PixelLength(12);
                break;

            // 表单相关块级元素
            case "form":
                style.Display = DisplayType.Block;
                style.MarginTop = new PixelLength(12);
                style.MarginBottom = new PixelLength(12);
                break;
            case "noscript":
                style.Display = DisplayType.Block;
                break;

            // 其他语义化元素
            case "search":
                style.Display = DisplayType.Block;
                break;
            case "hgroup":
                style.Display = DisplayType.Block;
                break;
        }
    }
}