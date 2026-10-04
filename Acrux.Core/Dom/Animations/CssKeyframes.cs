using Acrux.Core.Css;
using Acrux.Core.Css.Properties;
using Acrux.Core.Css.Rules;

namespace Acrux.Core.Dom.Animations;

/// <summary>One keyframe: a normalized offset plus the declarations at that offset.</summary>
public sealed class CssKeyframe
{
    /// <summary>Normalized position in [0,1].</summary>
    public double Offset { get; set; }

    /// <summary>Property name (lowercase, longhand) to CSS value text.</summary>
    public Dictionary<string, string> Declarations { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Per-keyframe <c>animation-timing-function</c>: it shapes the interval that
    /// <em>starts</em> at this keyframe (Web Animations §keyframe easing).
    /// </summary>
    public AnimationTimingFunction? Easing { get; set; }

    public bool IsExplicit { get; set; }
}

/// <summary>
/// A <c>@keyframes</c> rule normalized into an offset-sorted, de-duplicated
/// keyframe list, ready to be sampled by an animation effect.
///
/// The normalization follows Web Animations §5.9: selector lists are split,
/// <c>from</c>/<c>to</c> become 0/1, duplicate offsets are merged with the later
/// declaration winning, and 0% / 100% are materialized implicitly from the
/// element's underlying value when the author omitted them.
/// </summary>
public sealed class CssKeyframes
{
    public string Name { get; }
    public IReadOnlyList<CssKeyframe> Frames { get; }

    private CssKeyframes(string name, IReadOnlyList<CssKeyframe> frames)
    {
        Name = name;
        Frames = frames;
    }

    /// <summary>
    /// Set ACRUX_ANIM_TRACE=1 to have every sample print the interval it
    /// chose and the easing it applied. The two-easing interaction is subtle
    /// enough that being able to see the decision is worth the seam.
    /// </summary>
    internal static readonly bool TraceSampling =
        Environment.GetEnvironmentVariable("ACRUX_ANIM_TRACE") == "1";

    /// <summary>True when the rule declares at least one animatable property.</summary>
    public bool IsUsable => Frames.Count > 0;

    /// <summary>
    /// Build a normalized keyframe set from every <c>@keyframes</c> rule with the
    /// given name. Later rules with the same name append to the earlier one
    /// (CSS Animations 1 §4), and within a rule later blocks override earlier
    /// blocks at a shared offset.
    /// </summary>
    public static CssKeyframes? Build(string name, IReadOnlyList<StyleRuleKeyframes> rules)
    {
        if (rules == null || rules.Count == 0) return null;

        // offset -> merged declarations, insertion order preserved.
        var byOffset = new SortedDictionary<double, CssKeyframe>();
        var order = new List<double>();
        var easings = new Dictionary<double, AnimationTimingFunction>();

        foreach (var rule in rules)
        {
            if (!string.Equals(rule.Name, name, StringComparison.Ordinal)) continue;

            foreach (var block in rule.Keyframes)
            {
                var offsets = ParseSelector(block.Key);
                if (offsets.Count == 0) continue;

                AnimationTimingFunction? blockEasing = null;
                var decls = new List<KeyValuePair<string, string>>();

                foreach (var prop in block.Properties.Properties)
                {
                    string propName = prop.Name.ToCssString().ToLowerInvariant();
                    if (propName.Length == 0) continue;
                    if (propName.StartsWith("--", StringComparison.Ordinal)) continue;

                    if (propName == "animation-timing-function")
                    {
                        // Extracted, not animated: the keyframe's own easing.
                        blockEasing ??= AnimationTimingFunction.Parse(prop.Value.CssText());
                        continue;
                    }

                    if (!AnimatableProperties.IsAnimatable(propName)) continue;

                    decls.Add(new KeyValuePair<string, string>(propName, prop.Value.CssText().Trim()));
                }

                foreach (double offset in offsets)
                {
                    if (!byOffset.TryGetValue(offset, out var frame))
                    {
                        frame = new CssKeyframe { Offset = offset, IsExplicit = true };
                        byOffset[offset] = frame;
                        order.Add(offset);
                    }
                    // Later declarations at the same offset win.
                    foreach (var d in decls) frame.Declarations[d.Key] = d.Value;
                    if (blockEasing != null) easings[offset] = blockEasing;
                }
            }
        }

        if (byOffset.Count == 0) return null;

        foreach (var (offset, easing) in easings)
            if (byOffset.TryGetValue(offset, out var frame))
                frame.Easing = easing;

        // Materialize the implicit 0% / 100% endpoints. They hold no
        // declarations; sampling falls back to the element's underlying value.
        if (!byOffset.ContainsKey(0)) byOffset[0] = new CssKeyframe { Offset = 0 };
        if (!byOffset.ContainsKey(1)) byOffset[1] = new CssKeyframe { Offset = 1 };

        var frames = byOffset.Values.OrderBy(f => f.Offset).ToList();
        return new CssKeyframes(name, frames);
    }

    /// <summary>
    /// Parse a keyframe selector (<c>"0%, 50%"</c>, <c>"from"</c>, <c>"to"</c>).
    /// Offsets outside [0,1] are clamped, per Web Animations §5.9.
    /// </summary>
    public static List<double> ParseSelector(string key)
    {
        var result = new List<double>();
        if (string.IsNullOrWhiteSpace(key)) return result;

        foreach (var part in AnimationTimingFunction.SplitTopLevel(key))
        {
            var t = part.Trim().ToLowerInvariant();
            if (t.Length == 0) continue;
            if (t == "from") { result.Add(0); continue; }
            if (t == "to") { result.Add(1); continue; }
            if (t.EndsWith('%') && AnimationTimingFunction.TryNumber(t.TrimEnd('%'), out var pct))
            {
                result.Add(Math.Clamp(pct / 100.0, 0, 1));
                continue;
            }
            // A bare number in a keyframe selector is not valid CSS, but treating
            // it as a fraction is harmless and keeps malformed rules animating.
            if (AnimationTimingFunction.TryNumber(t, out var num))
                result.Add(Math.Clamp(num, 0, 1));
        }
        return result;
    }

    /// <summary>
    /// Sample the keyframe set at an iteration progress, writing each animated
    /// property's interpolated CSS value into <paramref name="values"/>.
    ///
    /// The caller has already applied the effect's direction to
    /// <paramref name="iterationProgress"/>, because the direction belongs to the
    /// progress value rather than to the keyframe list.
    ///
    /// <paramref name="effectEasing"/> is the element's
    /// <c>animation-timing-function</c>. It is the *default* for every interval:
    /// an interval whose starting keyframe declares its own timing function uses
    /// that instead, overriding the element's (CSS Animations 1 §4). So the
    /// easing applied here is always the starting keyframe's, never both.
    /// </summary>
    public void Sample(
        double iterationProgress,
        AnimationTimingFunction effectEasing,
        Func<string, string?> underlyingValue,
        Dictionary<string, string> values)
    {
        values.Clear();
        if (Frames.Count == 0) return;

        double p = double.IsNaN(iterationProgress) ? 0 : Math.Clamp(iterationProgress, 0, 1);

        // Locate the keyframe interval that contains p.
        CssKeyframe? before = null, after = null;
        double beforeOffset = 0, afterOffset = 1;

        for (int i = 0; i < Frames.Count - 1; i++)
        {
            var a = Frames[i];
            var b = Frames[i + 1];
            if (p >= a.Offset && p < b.Offset)
            {
                before = a; after = b; beforeOffset = a.Offset; afterOffset = b.Offset;
                break;
            }
        }

        if (before == null)
        {
            // At or past the last keyframe: hold the final value. Before the
            // first keyframe, hold the initial one.
            if (p >= Frames[^1].Offset)
            {
                var last = Frames[^1];
                before = last; after = last; beforeOffset = 1; afterOffset = 1;
            }
            else
            {
                var first = Frames[0];
                before = first; after = first; beforeOffset = 0; afterOffset = 0;
            }
        }

        double span = afterOffset - beforeOffset;
        double local = span > 0 ? (p - beforeOffset) / span : 0;
        local = Math.Clamp(local, 0, 1);

        // The interval's shaping function is the starting keyframe's, falling
        // back to the element's. Applying both would compose two easings, which
        // the spec explicitly does not do.
        var easing = before?.Easing ?? effectEasing;
        double specific = easing.Apply(local);

        if (TraceSampling)
        {
            Console.WriteLine($"[trace] {Name} p={p:F4} before={before?.Offset.ToString() ?? "-"} " +
                              $"keyEase={before?.Easing?.CssText ?? "-"} effect={effectEasing.CssText} " +
                              $"local={local:F4} specific={specific:F4}");
        }

        if (before == null || after == null) return;

        // Iterate the union of both frames' properties so a property present on
        // only one side still animates (the other side uses the underlying value).
        foreach (var prop in before.Declarations.Keys)
        {
            if (!values.ContainsKey(prop)) values[prop] = Resolve(prop, before, after, specific, underlyingValue);
        }
        foreach (var prop in after.Declarations.Keys)
        {
            if (!values.ContainsKey(prop)) values[prop] = Resolve(prop, before, after, specific, underlyingValue);
        }
    }

    private static string Resolve(
        string property,
        CssKeyframe before,
        CssKeyframe after,
        double specific,
        Func<string, string?> underlyingValue)
    {
        string? from = before.Declarations.TryGetValue(property, out var f) ? f : underlyingValue(property);
        string? to = after.Declarations.TryGetValue(property, out var t) ? t : underlyingValue(property);

        if (string.IsNullOrEmpty(from) && string.IsNullOrEmpty(to)) return "";
        if (string.IsNullOrEmpty(from)) return to!;
        if (string.IsNullOrEmpty(to)) return from!;
        if (specific <= 0) return from!;
        if (specific >= 1) return to!;

        // A property whose value type cannot be blended still animates: it flips
        // from the "from" value to the "to" value at the midpoint of the interval.
        return CssValueInterpolator.Interpolate(property, from, to, specific)
            ?? (specific < 0.5 ? from : to);
    }

    /// <summary>Union of every animatable property this keyframe set touches.</summary>
    public IEnumerable<string> DeclaredProperties()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var f in Frames)
            foreach (var k in f.Declarations.Keys)
                if (seen.Add(k)) yield return k;
    }

    public override string ToString() =>
        $"@keyframes {Name} {{ {string.Join(" ", Frames.Select(f => $"{f.Offset * 100:F1}%"))} }}";
}
