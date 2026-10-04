using Acrux.Core.Dom;

namespace Acrux.Core.Layout;

public interface ITextMeasurer
{
    float MeasureText(string text, string fontFamily, float fontSize, FontWeight weight = FontWeight.Normal);
    
    float MeasureTextAdvanced(string text, string fontFamily, float fontSize, FontWeight weight = FontWeight.Normal, FontStyleType style = FontStyleType.Normal);
    
    (float width, float height, float baseline) MeasureTextDetail(string text, string fontFamily, float fontSize, FontWeight weight = FontWeight.Normal);
}

public static class TextMeasurer
{
    public static ITextMeasurer? Instance { get; set; }
}
