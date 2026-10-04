using System.Globalization;
using Acrux.Core.Dom;

namespace Acrux.Rendering;

/// <summary>
/// Classifies a <see cref="ComputedStyle"/> into compositing reasons. This is a
/// pure, stateless classifier ported from the reference painting pipeline's
/// classification predicates. It maps only the reasons that are computable from
/// the engine's <c>ComputedStyle</c>; predicates that require information the
/// style model does not carry (element kind for video/iframe, live animation
/// state, viewport-overflow/containing-scroll-container context) are documented
/// and skipped rather than invented.
/// </summary>
public static class CompositingReasonFinder
{
    /// <summary>
    /// Returns the compositing reasons that are computably attributable to
    /// <paramref name="style"/>. Returns <see cref="CompositingReason.None"/> when
    /// nothing applies.
    /// </summary>
    public static CompositingReason ReasonsFor(ComputedStyle style)
    {
        if (style is null) return CompositingReason.None;

        CompositingReason reasons = CompositingReason.None;

        reasons |= ReasonsFor3DTransform(style);
        reasons |= ReasonsForWillChange(style);
        reasons |= ReasonsForScrollDependentPosition(style);
        reasons |= ReasonsForBlendMode(style);
        reasons |= ReasonsForEffects(style);
        reasons |= ReasonsForAnimation(style);

        return reasons;
    }

    /// <summary>
    /// True if the computed transform contains a non-trivial 3D operation (any
    /// operation that projects content out of the x/y plane).
    /// </summary>
    internal static bool HasNonTrivial3DTransform(ComputedStyle? style)
    {
        if (style is null || string.IsNullOrWhiteSpace(style.Transform)) return false;
        string t = string.Concat(style.Transform.Where(c => !char.IsWhiteSpace(c)));
        if (t.Length == 0) return false;

        // Basic-3D transform functions that are always three-dimensional.
        if (t.Contains("matrix3d", StringComparison.Ordinal)
            || t.Contains("translate3d", StringComparison.Ordinal)
            || t.Contains("translateZ", StringComparison.Ordinal)
            || t.Contains("scale3d", StringComparison.Ordinal)
            || t.Contains("scaleZ", StringComparison.Ordinal)
            || t.Contains("rotate3d", StringComparison.Ordinal))
            return true;

        // rotateX / rotateY are 3D only when the angle is non-zero; a lone
        // rotateZ is a 2D rotation and is intentionally not treated as 3D.
        return HasNonZeroAngle(t, "rotateX") || HasNonZeroAngle(t, "rotateY");
    }

    private static bool HasNonZeroAngle(string transform, string function)
    {
        int idx = transform.IndexOf(function, StringComparison.Ordinal);
        if (idx < 0) return false;
        int open = transform.IndexOf('(', idx);
        if (open < 0) return false;
        int close = transform.IndexOf(')', open);
        if (close < 0) return false;
        string arg = transform.Substring(open + 1, close - open - 1);
        if (arg.EndsWith("deg", StringComparison.OrdinalIgnoreCase))
            arg = arg[..^3];
        else if (arg.EndsWith("rad", StringComparison.OrdinalIgnoreCase))
            arg = arg[..^3];
        return float.TryParse(arg, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) && v != 0f;
    }

    /// <summary>
    /// 3D transform -> <see cref="CompositingReason.Transform3D"/>. Mirrors
    /// PotentialCompositingReasonsFor3DTransform (non-perspective 3D op).
    /// </summary>
    private static CompositingReason ReasonsFor3DTransform(ComputedStyle style)
    {
        if (HasNonTrivial3DTransform(style))
            return CompositingReason.Transform3D;
        return CompositingReason.None;
    }

    /// <summary>
    /// will-change hints. Mirrors CompositingReasonsForWillChange: each composited
    /// hint gets a dedicated reason; a generic compositable hint yields
    /// <see cref="CompositingReason.WillChangeOther"/> only when no explicit reason
    /// was produced.
    /// </summary>
    private static CompositingReason ReasonsForWillChange(ComputedStyle style)
    {
        var reasons = CompositingReason.None;
        var hints = ParseWillChangeHints(style.WillChange);
        if (hints.Count == 0) return reasons;

        if (hints.Contains("transform")) reasons |= CompositingReason.WillChangeTransform;
        if (hints.Contains("scale")) reasons |= CompositingReason.WillChangeScale;
        if (hints.Contains("rotate")) reasons |= CompositingReason.WillChangeRotate;
        if (hints.Contains("translate")) reasons |= CompositingReason.WillChangeTranslate;
        if (hints.Contains("opacity")) reasons |= CompositingReason.WillChangeOpacity;
        if (hints.Contains("filter")) reasons |= CompositingReason.WillChangeFilter;
        if (hints.Contains("backdrop-filter")) reasons |= CompositingReason.WillChangeBackdropFilter;

        // Any other compositable property (left, top, width, ...) counts as a
        // generic compositing hint, but only when no explicit reason is set.
        if (reasons == CompositingReason.None && HasGenericCompositingHint(hints))
            reasons |= CompositingReason.WillChangeOther;

        return reasons;
    }

    private static HashSet<string> ParseWillChangeHints(string willChange)
    {
        var hints = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(willChange) || willChange.Trim() == "auto") return hints;
        foreach (var part in willChange.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var p = part.Trim();
            if (p.Length == 0 || p == "auto") continue;
            hints.Add(p);
        }
        return hints;
    }

    private static bool HasGenericCompositingHint(HashSet<string> hints)
    {
        // Properties that historically trigger compositing for animations / layout.
        const string known = "top,left,right,bottom,width,height,z-index,position,background-color,color,outline-width,padding,margin,border-radius,box-shadow,clip-path,filter,opacity,transform,scale,rotate,translate,font-size,line-height,text-shadow,visibility";
        foreach (var h in hints)
        {
            if (known.Split(',').Contains(h, StringComparer.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Position-dependent compositing. Mirrors CompositingReasonsForScrollDependentPosition
    /// but is computed from the style alone. Note: the reference additionally requires
    /// the containing frame/viewport to actually overflow (scrollable) and, for sticky,
    /// a descendant-of-transform ancestor; neither is present in ComputedStyle, so this
    /// is a conservative style-level approximation.
    /// </summary>
    private static CompositingReason ReasonsForScrollDependentPosition(ComputedStyle style)
    {
        var reasons = CompositingReason.None;
        if (style.Position == PositionType.Fixed)
            reasons |= CompositingReason.FixedPosition;
        else if (style.Position == PositionType.Sticky)
            reasons |= CompositingReason.StickyPosition;

        if (IsScrollable(style.OverflowX) || IsScrollable(style.OverflowY))
            reasons |= CompositingReason.ScrollableContent;

        return reasons;
    }

    private static bool IsScrollable(OverflowType o) => o is OverflowType.Scroll or OverflowType.Auto;

    /// <summary>Blend modes other than normal force a composited blend surface.</summary>
    private static CompositingReason ReasonsForBlendMode(ComputedStyle style)
    {
        if (style.MixBlendMode != MixBlendModeType.Normal
            || style.BackgroundBlendMode != BackgroundBlendModeType.Normal)
            return CompositingReason.BlendMode;
        return CompositingReason.None;
    }

    /// <summary>Filter / backdrop-filter / mask / non-affine clip-path causes.</summary>
    private static CompositingReason ReasonsForEffects(ComputedStyle style)
    {
        var reasons = CompositingReason.None;
        if (!string.IsNullOrWhiteSpace(style.Filter))
            reasons |= CompositingReason.OpaqueFilter;
        if (!string.IsNullOrWhiteSpace(style.BackdropFilter))
            reasons |= CompositingReason.BackdropFilter;
        if (!string.IsNullOrWhiteSpace(style.Mask) || !string.IsNullOrWhiteSpace(style.MaskImage))
            reasons |= CompositingReason.Mask;
        if (!string.IsNullOrWhiteSpace(style.ClipPath))
            reasons |= CompositingReason.ClipPath;
        return reasons;
    }

    /// <summary>
    /// Active-compositor-friendly animation hints. The reference reads a live
    /// "current animation" runtime state (HasCurrentTransformAnimation etc.); the
    /// engine's style only records the declared animation, so a declared animation
    /// on a compositor-friendly property is used as the approximation.
    /// </summary>
    private static CompositingReason ReasonsForAnimation(ComputedStyle style)
    {
        if (string.IsNullOrWhiteSpace(style.Animation) || style.Animation.Trim() == "none")
            return CompositingReason.None;

        var reasons = CompositingReason.None;
        var names = (style.AnimationName ?? "").ToLowerInvariant();
        var all = (style.Animation ?? "").ToLowerInvariant();

        // transform-family: transform / translate / scale / rotate
        if (all.Contains("transform") || names.Contains("transform"))
            reasons |= CompositingReason.ActiveTransformAnimation;
        if (all.Contains("opacity"))
            reasons |= CompositingReason.ActiveOpacityAnimation;
        if (all.Contains("filter"))
            reasons |= CompositingReason.ActiveFilterAnimation;

        return reasons;
    }
}
