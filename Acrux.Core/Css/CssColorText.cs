using SkiaSharp;

namespace Acrux.Core.Css;

/// <summary>
/// The one serialisation of a colour for a computed value (CSS Color 4 §5: an opaque
/// colour is printed as rgb(), only a colour that carries an alpha component as rgba()).
///
/// Every engine-facing print of a colour used to build its own string, so the same colour
/// came out as 'rgb(0, 0, 255)', 'rgba(0, 0, 255, 1)' or 'rgba(0, 0, 255, 0.33333334)'
/// depending on which channel asked — and a page that compares the text of a style it just
/// read sees the difference.
/// </summary>
public static class CssColorText
{
    public static string FromColor(SKColor c) => c.Alpha >= 255
        ? $"rgb({c.Red}, {c.Green}, {c.Blue})"
        : $"rgba({c.Red}, {c.Green}, {c.Blue}, {Dom.Animations.CssValueTokenizer.AlphaText(c.Alpha)})";

    /// <summary>A missing background colour is 'transparent', which is rgba(0, 0, 0, 0) —
    /// the value the CSSOM reports for it.</summary>
    public static string Transparent => "rgba(0, 0, 0, 0)";
}
