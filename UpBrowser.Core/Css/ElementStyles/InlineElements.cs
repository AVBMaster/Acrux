using SkiaSharp;
using UpBrowser.Core.Dom;

namespace UpBrowser.Core.Css.ElementStyles;

public static class InlineElements
{
    public static void Apply(ComputedStyle style, string tagName)
    {
        switch (tagName)
        {
            // 通用内联元素
            case "span":
                style.Display = DisplayType.Inline;
                break;

            // 链接
            case "a":
                style.Display = DisplayType.Inline;
                style.Color = SKColor.Parse("#0000EE");
                style.TextDecoration = TextDecorationType.Underline;
                style.TextDecorationLine = TextDecorationLineType.Underline;
                // The underline is 'auto' colored, i.e. the link's own text color, so
                // author 'color' moves it (CSS 2.1 §16.3.1).
                style.Cursor = "pointer";
                break;

            // 文本格式化 - 粗体
            case "strong":
            case "b":
                style.Display = DisplayType.Inline;
                style.FontWeight = FontWeight.Bold;
                break;

            // 文本格式化 - 斜体
            case "em":
            case "i":
                style.Display = DisplayType.Inline;
                style.FontStyle = FontStyleType.Italic;
                break;

            // 文本格式化 - 下划线
            case "u":
            case "ins":
                style.Display = DisplayType.Inline;
                style.TextDecoration = TextDecorationType.Underline;
                style.TextDecorationLine = TextDecorationLineType.Underline;
                break;

            // 文本格式化 - 删除线
            case "s":
            case "del":
                style.Display = DisplayType.Inline;
                style.TextDecoration = TextDecorationType.LineThrough;
                style.TextDecorationLine = TextDecorationLineType.LineThrough;
                break;

            // 代码相关
            case "code":
                style.Display = DisplayType.Inline;
                style.FontFamily = "monospace";
                style.FontSize = 14;
                break;

            case "kbd":
                style.Display = DisplayType.Inline;
                style.FontFamily = "monospace";
                style.FontSize = 14;
                break;

            case "samp":
                style.Display = DisplayType.Inline;
                style.FontFamily = "monospace";
                style.FontSize = 14;
                break;

            case "var":
                style.Display = DisplayType.Inline;
                style.FontStyle = FontStyleType.Italic;
                style.FontFamily = "monospace";
                break;

            case "tt":
                style.Display = DisplayType.Inline;
                style.FontFamily = "monospace";
                break;

            // 文本样式
            case "small":
                style.Display = DisplayType.Inline;
                style.FontSize = 12;
                break;

            case "sub":
                style.Display = DisplayType.Inline;
                style.VerticalAlign = VerticalAlignType.Sub;
                style.FontSize = 12;
                break;

            case "sup":
                style.Display = DisplayType.Inline;
                style.VerticalAlign = VerticalAlignType.Super;
                style.FontSize = 12;
                break;

            case "mark":
                style.Display = DisplayType.Inline;
                style.BackgroundColor = SKColor.Parse("#FFFF00");
                style.Color = SKColors.Black;
                break;

            // 引用和术语
            case "cite":
                style.Display = DisplayType.Inline;
                style.FontStyle = FontStyleType.Italic;
                break;

            case "dfn":
                style.Display = DisplayType.Inline;
                style.FontStyle = FontStyleType.Italic;
                style.FontWeight = FontWeight.Bold;
                break;

            case "q":
                style.Display = DisplayType.Inline;
                break;

            case "abbr":
                style.Display = DisplayType.Inline;
                style.TextDecoration = TextDecorationType.Underline;
                style.TextDecorationLine = TextDecorationLineType.Underline;
                style.Cursor = "help";
                break;

            case "acronym":
                style.Display = DisplayType.Inline;
                style.Cursor = "help";
                break;

            case "time":
                style.Display = DisplayType.Inline;
                break;

            case "data":
                style.Display = DisplayType.Inline;
                break;

            // Ruby 注释（东亚文字注音）
            case "ruby":
                style.Display = DisplayType.Ruby;
                style.VerticalAlign = VerticalAlignType.TextTop;
                break;

            case "rt":
                style.Display = DisplayType.Inline;
                style.FontSize = 12;
                style.VerticalAlign = VerticalAlignType.TextTop;
                break;

            case "rp":
                style.Display = DisplayType.Inline;
                style.FontSize = 12;
                break;

            // 双向文本
            case "bdi":
                style.Display = DisplayType.Inline;
                break;

            case "bdo":
                style.Display = DisplayType.Inline;
                break;

            // 换行
            case "br":
                style.Display = DisplayType.Inline;
                break;

            case "wbr":
                style.Display = DisplayType.Inline;
                break;

            // 表单相关内联元素
            case "label":
                style.Display = DisplayType.Inline;
                style.Cursor = "pointer";
                break;

            case "output":
                style.Display = DisplayType.Inline;
                style.FontFamily = "monospace";
                break;

            // 其他内联元素
            case "big":
                style.Display = DisplayType.Inline;
                style.FontSize = 20;
                break;

            case "blink":
                style.Display = DisplayType.Inline;
                break;

            case "nobr":
                style.Display = DisplayType.Inline;
                style.WhiteSpace = WhiteSpaceMode.Nowrap;
                break;

            case "spacer":
                style.Display = DisplayType.Inline;
                break;

            case "strike":
                style.Display = DisplayType.Inline;
                style.TextDecoration = TextDecorationType.LineThrough;
                style.TextDecorationLine = TextDecorationLineType.LineThrough;
                break;
        }
    }
}
