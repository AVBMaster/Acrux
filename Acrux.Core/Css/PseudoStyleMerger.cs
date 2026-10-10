using Acrux.Core.Dom;

namespace Acrux.Core.Css;

/// <summary>
/// Builds the style of a generated box — the one a <c>::first-line</c> / <c>::first-letter</c>
/// layout reads, and the one <c>getComputedStyle(element, '::before')</c> reports. The cascade
/// stores a pseudo-element's declarations in a side-car on the element rather than in a box of
/// their own, so the merge happens wherever the base style is known: at layout, and at the read.
/// </summary>
public static class PseudoStyleMerger
{
    /// <summary>The base style plus the pseudo's declarations, keeping every non-inherited value
    /// the originating element had. Used by the layout, which resets the box properties itself
    /// because it builds the generated box in place.</summary>
    public static ComputedStyle? Merge(ComputedStyle baseStyle, Dictionary<string, string>? declarations)
    {
        if (declarations == null || declarations.Count == 0)
            return null;
        var style = baseStyle.Clone();
        Apply(style, baseStyle, declarations, withContent: false);
        return style;
    }

    /// <summary>
    /// The style a generated box is read with by a script. It differs from <see cref="Merge"/> in
    /// two measured ways: a box that the page never styled still exists as far as the CSSOM is
    /// concerned (so an empty declaration list answers the inherited values, not nothing), and
    /// every non-inherited property starts at its initial value — a <c>::before</c> of a
    /// <c>display:block</c> element computes <c>inline</c>, and its <c>width</c> is <c>auto</c>
    /// rather than the element's used box (both measured).
    /// <c>content</c> is applied here because a script reads the property itself; the layout
    /// derives the generated text from the same declaration by another route.
    /// </summary>
    public static ComputedStyle MergeForGeneratedBox(ComputedStyle baseStyle,
        Dictionary<string, string>? declarations, Element? element = null,
        bool generatesBox = true)
    {
        var style = baseStyle.Clone();
        ResetNonInheritedBoxProperties(style);
        // 'display' is not in the box reset (the layout sets it from the declaration), and the
        // initial value of a generated box's display is 'inline' (CSS Pseudo-Elements 4 §3.1).
        style.Display = DisplayType.Inline;
        style.DisplayIsFlowRoot = false;
        // A generated box with no 'content' declaration generates nothing, and that is what the
        // reference engine reports for it (measured: 'none', where the element itself reads
        // 'normal').
        // A '::before' or '::after' with no 'content' of its own generates no box, and that is the
        // word the reference engine uses for it; a marker or a line fragment is not a generated box
        // in the same sense and stays at the property's initial 'normal' (both measured).
        style.Content = generatesBox ? "none" : "normal";
        Apply(style, baseStyle, declarations, withContent: true, element);
        return style;
    }

    private static void Apply(ComputedStyle style, ComputedStyle baseStyle,
        Dictionary<string, string>? declarations, bool withContent, Element? element = null)
    {
        if (declarations == null) return;
        foreach (var entry in declarations)
        {
            var name = entry.Key.ToLowerInvariant();
            var value = entry.Value;
            // A generated box's 'content' is read through the same half-evaluation the cascade
            // gives it: 'attr()' becomes the attribute's value and 'var()' its substitution, while
            // a counter stays the function the page wrote (CssFunctionEvaluator
            // .EvaluateForContent). The layout builds the painted text from the raw declaration
            // instead, so this only serves the reading surface.
            if (withContent && name == "content")
                value = CssFunctionEvaluator.EvaluateForContent(value, element,
                    style.FontSize, 16f, 0f, 0f);
            switch (name)
            {
                case "content" when !withContent:
                    continue;
                // Font longhands are "high-priority" in the cascade (em/ch units
                // depend on the resolved font-size), so they are not in Apply.
                case "font-size":
                    Css.Resolver.CssPropertyApplier.SetFontSize(style, value, Css.Resolver.CssPropertyApplier.ParseFontSize(value, baseStyle));
                    continue;
                case "font-weight":
                    style.FontWeight = Css.Resolver.CssPropertyApplier.ParseFontWeight(value);
                    continue;
                case "font-style":
                    style.FontStyle = Css.Resolver.CssPropertyApplier.ParseFontStyle(value, style);
                    continue;
                case "line-height":
                    Acrux.Core.Fonts.LineBoxMetrics.ApplyLineHeight(style, value);
                    continue;
            }
            Css.Resolver.CssPropertyApplier.Apply(style, name, value);
        }
    }

    /// <summary>CSS Pseudo-Elements 4 §3.1: a generated box takes the <em>initial</em> value of
    /// every non-inherited property its own rule does not declare. Measured against the reference
    /// engine, a <c>::before</c> inside <c>margin:20px; border:5px solid; background:red;
    /// position:relative; top:3px; z-index:5; opacity:.5</c> computes every one of those to its
    /// initial value — but the style is built by cloning the originating element, so each has to be
    /// written back by hand or it leaks into the generated box.
    ///
    /// <para>'auto' is the initial value of width/height, and the rest of the engine tests for
    /// AutoLength (not for null) to decide whether a size is definite — writing null made an
    /// auto-sized generated box look definite, so it stretched to its containing block instead of
    /// shrinking to fit.</para></summary>
    public static void ResetNonInheritedBoxProperties(ComputedStyle style)
    {
        style.Width = AutoLength.Instance;
        style.Height = AutoLength.Instance;
        style.MinWidth = null;
        style.MaxWidth = null;
        style.MinHeight = null;
        style.MaxHeight = null;

        style.MarginTop = style.MarginRight = style.MarginBottom = style.MarginLeft = new PixelLength(0);
        style.PaddingTop = style.PaddingRight = style.PaddingBottom = style.PaddingLeft = new PixelLength(0);

        style.BorderTopWidth = style.BorderRightWidth = style.BorderBottomWidth = style.BorderLeftWidth = 0;
        style.BorderTopStyle = style.BorderRightStyle = style.BorderBottomStyle = style.BorderLeftStyle = BorderStyle.None;

        style.Position = PositionType.Static;
        style.Top = style.Right = style.Bottom = style.Left = AutoLength.Instance;
        style.Float = FloatType.None;
        style.Clear = ClearType.None;
        // The flow-root marker belongs to the same property as 'display' and has to be
        // reset with it, or a generated box inside a flow-root parent keeps the parent's
        // formatting context.
        style.DisplayIsFlowRoot = false;
        style.ZIndex = null;
        style.Opacity = 1f;
        style.Overflow = style.OverflowX = style.OverflowY = OverflowType.Visible;
        style.BackgroundColor = null;
        style.BackgroundImage = null;
        style.OutlineWidth = 0;
        style.OutlineStyle = BorderStyle.None;
        style.Transform = null;
        style.BoxShadow = null;
        style.TextShadow = new();
        style.Contain = ContainType.None;
    }
}
