using SkiaSharp;
using Acrux.Core.Dom;

namespace Acrux.Core.Css.ElementStyles;

public static class RootElements
{
    public static void Apply(ComputedStyle style, string tagName)
    {
        switch (tagName)
        {
            case "html":
                style.Display = DisplayType.Block;
                style.MarginTop = new PixelLength(0);
                style.MarginBottom = new PixelLength(0);
                style.MarginLeft = new PixelLength(0);
                style.MarginRight = new PixelLength(0);
                style.FontFamily = "sans-serif";
                style.FontSize = 16;
                Fonts.LineBoxMetrics.SetNormal(style);
                // 透明背景，让 body 背景透出
                style.BackgroundColor = null;
                break;

            case "body":
                style.Display = DisplayType.Block;
                style.MarginTop = new PixelLength(8);
                style.MarginBottom = new PixelLength(8);
                style.MarginLeft = new PixelLength(8);
                style.MarginRight = new PixelLength(8);
                style.FontSize = 16;
                Fonts.LineBoxMetrics.SetNormal(style);
                style.Color = SKColors.Black;
                style.BackgroundColor = SKColors.White;
                style.FontFamily = "Arial, sans-serif";
                break;

            case "head":
                style.Display = DisplayType.None;
                break;

            case "title":
                style.Display = DisplayType.None;
                break;

            case "base":
                style.Display = DisplayType.None;
                break;

            case "link":
                style.Display = DisplayType.None;
                break;

            case "meta":
                style.Display = DisplayType.None;
                break;

            case "style":
                style.Display = DisplayType.None;
                break;

            case "script":
                style.Display = DisplayType.None;
                break;

            case "noscript":
                style.Display = DisplayType.Block;
                break;

            case "template":
                style.Display = DisplayType.None;
                break;

            case "doctype":
                style.Display = DisplayType.None;
                break;
        }
    }
}