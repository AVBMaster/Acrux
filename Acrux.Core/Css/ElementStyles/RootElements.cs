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
                // The initial value of 'font-family' is the browser's standard font,
                // which is a SERIF face in the reference engine (measured: an unstyled
                // document computes font-family: "Times New Roman"). Using 'sans-serif'
                // here made every page that declares no font render in the wrong face
                // and measure ~5% wide.
                style.FontFamily = Acrux.Core.Fonts.FontManager.StandardFontFamily;
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
                style.FontFamily = Acrux.Core.Fonts.FontManager.StandardFontFamily;
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