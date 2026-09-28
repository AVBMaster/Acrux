using UpBrowser.Core.Dom;

namespace UpBrowser.Core.Css;

/// <summary>
/// Builds the style a ::first-line / ::first-letter pseudo-element inherits from
/// its originating box plus the declarations matched for the pseudo-element.
/// The cascade stores those declarations in a side-car on the element, so the
/// merge happens at layout/paint time where the base style is known.
/// </summary>
public static class PseudoStyleMerger
{
    public static ComputedStyle? Merge(ComputedStyle baseStyle, Dictionary<string, string>? declarations)
    {
        if (declarations == null || declarations.Count == 0)
            return null;

        var style = baseStyle.Clone();
        foreach (var entry in declarations)
        {
            var name = entry.Key.ToLowerInvariant();
            var value = entry.Value;
            switch (name)
            {
                case "content":
                    continue;
                // Font longhands are "high-priority" in the cascade (em/ch units
                // depend on the resolved font-size), so they are not in Apply.
                case "font-size":
                    style.FontSize = Css.Resolver.CssPropertyApplier.ParseFontSize(value, baseStyle);
                    continue;
                case "font-weight":
                    style.FontWeight = Css.Resolver.CssPropertyApplier.ParseFontWeight(value);
                    continue;
                case "font-style":
                    style.FontStyle = Css.Resolver.CssPropertyApplier.ParseFontStyle(value, style);
                    continue;
                case "line-height":
                    UpBrowser.Core.Fonts.LineBoxMetrics.ApplyLineHeight(style, value);
                    continue;
            }
            Css.Resolver.CssPropertyApplier.Apply(style, name, value);
        }
        return style;
    }
}
