using SkiaSharp;

namespace Acrux.Core.Css.Values;

/// <summary>Represents a CSS color value, wrapping an SKColor.</summary>
public sealed class CssColorValue : CssValue
{
    public SKColor Color { get; }
    public string? OriginalText { get; }

    public CssColorValue(SKColor color, string? originalText = null) : base(CssClassType.Color)
    {
        Color = color;
        OriginalText = originalText;
    }

    public override string CssText()
    {
        if (OriginalText != null) return OriginalText;
        return $"#{Color.Red:x2}{Color.Green:x2}{Color.Blue:x2}";
    }

    public override bool Equals(CssValue other) =>
        other is CssColorValue c && c.Color == Color;

    public override int GetHashCode() => HashCode.Combine(Color.Red, Color.Green, Color.Blue, Color.Alpha);
}

public sealed class CssStringValue : CssValue
{
    public string Value { get; }

    public CssStringValue(string value) : base(CssClassType.String)
    {
        Value = value;
    }

    public override string CssText() => $"\"{Value}\"";
}

public sealed class CssCustomIdentValue : CssValue
{
    public string Value { get; }

    public CssCustomIdentValue(string value) : base(CssClassType.CustomIdent)
    {
        Value = value;
    }

    public override string CssText() => $"—{Value}" is var _ && Value.StartsWith("--") ? Value : Value;
    public override bool Equals(CssValue other) => other is CssCustomIdentValue v && v.Value == Value;
    public override int GetHashCode() => Value.GetHashCode();
}

public sealed class CssQuadValue : CssValue
{
    public CssValue Top { get; }
    public CssValue Right { get; }
    public CssValue Bottom { get; }
    public CssValue Left { get; }

    public CssQuadValue(CssValue top, CssValue right, CssValue bottom, CssValue left) : base(CssClassType.Quad)
    {
        Top = top; Right = right; Bottom = bottom; Left = left;
    }

    public override string CssText() =>
        $"{Top.CssText()} {Right.CssText()} {Bottom.CssText()} {Left.CssText()}";
}

public sealed class CssValuePair : CssValue
{
    public CssValue First { get; }
    public CssValue Second { get; }

    public CssValuePair(CssValue first, CssValue second) : base(CssClassType.ValuePair)
    {
        First = first;
        Second = second;
    }

    public override string CssText() => $"{First.CssText()} {Second.CssText()}";
}

public sealed class CssShadowValue : CssValue
{
    public CssValue? X { get; }
    public CssValue? Y { get; }
    public CssValue? Blur { get; }
    public CssValue? Spread { get; }
    public CssValue? Style { get; }
    public CssValue? Color { get; }

    public CssShadowValue(CssValue? x, CssValue? y, CssValue? blur, CssValue? spread, CssValue? style, CssValue? color)
        : base(CssClassType.Shadow)
    {
        X = x; Y = y; Blur = blur; Spread = spread; Style = style; Color = color;
    }

    public override string CssText()
    {
        var parts = new List<string>();
        if (X != null) parts.Add(X.CssText());
        if (Y != null) parts.Add(Y.CssText());
        if (Blur != null) parts.Add(Blur.CssText());
        if (Spread != null) parts.Add(Spread.CssText());
        if (Style != null) parts.Add(Style.CssText());
        if (Color != null) parts.Add(Color.CssText());
        return string.Join(" ", parts);
    }
}