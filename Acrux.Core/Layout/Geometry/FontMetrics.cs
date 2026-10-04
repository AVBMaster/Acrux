namespace Acrux.Core.Layout.Geometry;

/// <summary>
/// Represents the metrics of a font. Mirrors FontHeight in font_height.h.
/// </summary>
public readonly struct FontHeight
{
    public float Ascent { get; }
    public float Descent { get; }
    public float LineHeight => Ascent + Descent;

    public FontHeight(float ascent, float descent) { Ascent = ascent; Descent = descent; }
    public bool IsEmpty => Ascent == 0 && Descent == 0;

    public static readonly FontHeight Empty = new(0, 0);
    public static readonly FontHeight TextMetrics = new(0, 0);

    public FontHeight Union(FontHeight other)
    {
        if (IsEmpty) return other;
        if (other.IsEmpty) return this;
        return new FontHeight(Math.Max(Ascent, other.Ascent), Math.Max(Descent, other.Descent));
    }

    /// <summary>
    /// Unite an inline box's metrics into a line box's metrics. Alias of
    /// <see cref="Union"/> using the naming of the source algorithm.
    /// </summary>
    public FontHeight Unite(FontHeight other) => Union(other);

    /// <summary>True when both of <paramref name="other"/>'s edges fit inside this one.</summary>
    public bool Contains(FontHeight other) => other.Ascent <= Ascent && other.Descent <= Descent;

    /// <summary>
    /// Add half-leading above and below. Mirrors FontHeight::AddLeading().
    /// </summary>
    public FontHeight AddLeading(FontHeight leadingSpace) =>
        new(Ascent + leadingSpace.Ascent, Descent + leadingSpace.Descent);

    /// <summary>
    /// Shift the metrics along the block axis without changing the total height.
    /// Mirrors FontHeight::Move().
    /// </summary>
    public FontHeight Move(float delta) => new(Ascent - delta, Descent + delta);

    public static FontHeight operator +(FontHeight a, FontHeight b) =>
        new(a.Ascent + b.Ascent, a.Descent + b.Descent);

    /// <summary>
    /// Split the difference between the used 'line-height' and the text's own
    /// height into leading above and below the text. Mirrors
    /// CalculateLeadingSpace() in line_utils.cc: the half above is floored, and
    /// the remainder goes below, so an odd leading biases downwards.
    /// </summary>
    public static FontHeight CalculateLeadingSpace(float lineHeight, FontHeight currentHeight)
    {
        float extra = lineHeight - currentHeight.LineHeight;
        float ascentLeading = MathF.Floor(extra / 2f);
        float descentLeading = extra - ascentLeading;
        return new FontHeight(ascentLeading, descentLeading);
    }

    public override string ToString() => $"ascent={Ascent}, descent={Descent}";
}

/// <summary>
/// Aggregate metrics for a font. Mirrors FontHeightMetrics in font_metrics.h.
/// </summary>
public readonly struct FontHeightMetrics
{
    public FontHeight Metrics { get; }
    public float Ascent => Metrics.Ascent;
    public float Descent => Metrics.Descent;
    public float LineHeight => Metrics.LineHeight;
    public float CapHeight { get; }

    public FontHeightMetrics(float ascent, float descent, float capHeight)
    {
        Metrics = new FontHeight(ascent, descent);
        CapHeight = capHeight;
    }
}

/// <summary>
/// Style variant for layout objects. Mirrors style_variant.h.
/// </summary>
public enum StyleVariant
{
    Standard,
    FirstLineInherited,
    FirstLineOwned,
    MathML,
}
