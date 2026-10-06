using SkiaSharp;
using Acrux.Core.Css;

namespace Acrux.Core.Dom;

public abstract class Length
{
    public abstract float ToPixels(float reference, float rootFontSize, float viewportWidth, float viewportHeight);

    /// <summary>
    /// Metrics of the element currently being resolved, when a font-relative
    /// unit (ch/ex/cap/ic/lh…) needs real glyph dimensions. Null outside a
    /// <see cref="FontUnitContext.Scope"/>, where callers fall back to the
    /// size-based approximation.
    /// </summary>
    protected static Acrux.Core.Fonts.FontMetrics? CurrentFontMetrics()
    {
        var style = FontUnitContext.Current;
        return style != null && style.FontSize > 0
            ? Acrux.Core.Fonts.FontMetricsProvider.GetForStyle(style)
            : null;
    }

    public static Length Parse(string value)
    {
        if (string.IsNullOrEmpty(value) || value == "auto" || value == "inherit" || value == "initial")
            return AutoLength.Instance;

        value = value.Trim();
        if (value.Equals("max-content", StringComparison.OrdinalIgnoreCase))
            return new IntrinsicLength(IntrinsicSizeKind.MaxContent);
        if (value.Equals("min-content", StringComparison.OrdinalIgnoreCase))
            return new IntrinsicLength(IntrinsicSizeKind.MinContent);
        if (value.Equals("fit-content", StringComparison.OrdinalIgnoreCase))
            return new IntrinsicLength(IntrinsicSizeKind.FitContent);

        try
        {
            // Check longest units first to avoid false matches
            if (value.EndsWith("cqmin"))
                return new CqMinLength(SafeFloat(value[..^5]));
            if (value.EndsWith("cqmax"))
                return new CqMaxLength(SafeFloat(value[..^5]));
            if (value.EndsWith("cqw"))
                return new CqWLength(SafeFloat(value[..^3]));
            if (value.EndsWith("cqh"))
                return new CqHLength(SafeFloat(value[..^3]));
            if (value.EndsWith("cqi"))
                return new CqILength(SafeFloat(value[..^3]));
            if (value.EndsWith("cqb"))
                return new CqBLength(SafeFloat(value[..^3]));
            if (value.EndsWith("dvw"))
                return new DVwLength(SafeFloat(value[..^3]));
            if (value.EndsWith("dvh"))
                return new DVhLength(SafeFloat(value[..^3]));
            if (value.EndsWith("svw"))
                return new SVwLength(SafeFloat(value[..^3]));
            if (value.EndsWith("svh"))
                return new SVhLength(SafeFloat(value[..^3]));
            if (value.EndsWith("lvw"))
                return new LVwLength(SafeFloat(value[..^3]));
            if (value.EndsWith("lvh"))
                return new LVhLength(SafeFloat(value[..^3]));
            if (value.EndsWith("vmin"))
                return new VminLength(SafeFloat(value[..^4]));
            if (value.EndsWith("vmax"))
                return new VmaxLength(SafeFloat(value[..^4]));
            if (value.EndsWith("vw"))
                return new VwLength(SafeFloat(value[..^2]));
            if (value.EndsWith("vh"))
                return new VhLength(SafeFloat(value[..^2]));
            if (value.EndsWith("vi"))
                return new ViLength(SafeFloat(value[..^2]));
            if (value.EndsWith("vb"))
                return new VbLength(SafeFloat(value[..^2]));
            if (value.EndsWith("rem"))
                return new RemLength(SafeFloat(value[..^3]));
            if (value.EndsWith("rex"))
                return new RexLength(SafeFloat(value[..^3]));
            if (value.EndsWith("ric"))
                return new RicLength(SafeFloat(value[..^3]));
            if (value.EndsWith("rlh"))
                return new RlhLength(SafeFloat(value[..^3]));
            if (value.EndsWith("cap"))
                return new CapLength(SafeFloat(value[..^3]));
            if (value.EndsWith("rcap"))
                return new RcapLength(SafeFloat(value[..^4]));
            if (value.EndsWith("lh"))
                return new LhLength(SafeFloat(value[..^2]));
            if (value.EndsWith("px"))
                return new PixelLength(SafeFloat(value[..^2]));
            if (value.EndsWith("em"))
                return new EmLength(SafeFloat(value[..^2]));
            if (value.EndsWith("ex"))
                return new ExLength(SafeFloat(value[..^2]));
            if (value.EndsWith("ch"))
                return new ChLength(SafeFloat(value[..^2]));
            if (value.EndsWith("%"))
                return new PercentLength(SafeFloat(value[..^1]) / 100f);
            if (value.EndsWith("pt"))
                return new PixelLength(SafeFloat(value[..^2]) * 1.33333f);
            if (value.EndsWith("pc"))
                return new PixelLength(SafeFloat(value[..^2]) * 16f);
            if (value.EndsWith("in"))
                return new PixelLength(SafeFloat(value[..^2]) * 96f);
            if (value.EndsWith("cm"))
                return new PixelLength(SafeFloat(value[..^2]) * 37.7953f);
            if (value.EndsWith("mm"))
                return new PixelLength(SafeFloat(value[..^2]) * 3.77953f);
            // CSS Values 3 §6.7: 1q = 0.25mm. Checked after the longer units so
            // it cannot swallow another suffix.
            if (value.EndsWith("q"))
                return new PixelLength(SafeFloat(value[..^1]) * 0.9448819f);
            if (value.EndsWith("ic"))
                return new IcLength(SafeFloat(value[..^2]));
            if (value == "0")
                return new PixelLength(0);

            if (value.StartsWith("calc(") || value.StartsWith("min(") || value.StartsWith("max(") || value.StartsWith("clamp(") || value.StartsWith("fit-content("))
            {
                return new MathLength(value);
            }
        }
        catch (FormatException)
        {
            // Gracefully handle malformed CSS values - return auto instead of crashing
        }
        catch (OverflowException)
        {
            // Handle extremely large/small values
        }

        return AutoLength.Instance;
    }

    private static float SafeFloat(string s)
    {
        s = s.Trim();
        if (float.TryParse(s, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var result))
            return result;
        return 0f;
    }

    public static bool TryParse(string value, out Length length)
    {
        var parsed = Parse(value);
        if (!IsLengthValue(value, parsed))
        {
            length = AutoLength.Instance;
            return false;
        }
        length = parsed;
        return true;
    }

    /// <summary>True when the token really carries a length value, which is what
    /// shorthand grammars need to tell a &lt;length&gt; apart from a keyword or a
    /// &lt;color&gt; (CSS Values 3 §6). <see cref="Parse"/> is deliberately lenient
    /// — anything unrecognised degrades to 'auto' and 'fiftyem' becomes 0em — so
    /// neither Parse nor the (historically always-true) TryParse can classify.</summary>
    public static bool IsLength(string? value)
        => !string.IsNullOrWhiteSpace(value) && IsLengthValue(value, Parse(value));

    private static bool IsLengthValue(string? value, Length parsed)
    {
        switch (parsed)
        {
            // 'max-content', 'calc()' and friends are legitimate length values.
            case IntrinsicLength:
            case MathLength:
                return true;
            case AutoLength:
                var keyword = value?.Trim() ?? string.Empty;
                return keyword is "auto" or "inherit" or "initial";
        }

        // A concrete length unit was matched; make sure the leading numeric
        // component actually parses, otherwise 'red' / 'fiftyem' look like lengths.
        var s = value!.Trim();
        int i = 0;
        if (s[i] is '+' or '-') i++;
        int start = i;
        while (i < s.Length && (char.IsAsciiDigit(s[i]) || s[i] == '.')) i++;
        return i > start
            && float.TryParse(s[..i], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out _);
    }
    /// <summary>'font-size' absolute keywords, expressed as ratios of the user
    /// default (medium). CSS Fonts 4 §7.1; matches the table every engine ships
    /// (9 / 10 / 13.33 / 16 / 18 / 24 / 32 / 48 for a 16px medium).</summary>
    public const float FontSizeMedium = 16f;

    public static float ParseFontSize(string value, float parentFontSize)
    {
        value = value?.Trim() ?? "";
        // The unit has to be stripped with its own length: slicing two characters
        // off "2rem" left a stray 'r' behind and made the parse fail silently.
        if (TryLength(value, "rem", out var rem)) return rem * FontSizeMedium;
        if (TryLength(value, "px", out var px)) return px;
        if (TryLength(value, "pt", out var pt)) return pt * 96f / 72f;
        if (TryLength(value, "pc", out var pc)) return pc * 16f;
        if (TryLength(value, "in", out var inch)) return inch * 96f;
        if (TryLength(value, "cm", out var cm)) return cm * 96f / 2.54f;
        if (TryLength(value, "mm", out var mm)) return mm * 96f / 25.4f;
        if (TryLength(value, "q", out var q)) return q * 96f / 101.6f;
        if (TryLength(value, "em", out var em)) return em * parentFontSize;
        if (TryLength(value, "ex", out var ex)) return ex * parentFontSize * 0.5f;
        if (TryLength(value, "ch", out var ch)) return ch * parentFontSize * 0.5f;
        if (TryLength(value, "vh", out var vh)) return vh / 100f * FontSizeMedium;
        if (TryLength(value, "vw", out var vw)) return vw / 100f * FontSizeMedium;
        if (value.EndsWith("%") && float.TryParse(value[..^1], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var pct))
            return pct / 100f * parentFontSize;
        if (float.TryParse(value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var bare))
            return bare > 0 ? bare : parentFontSize;

        float medium = FontSizeMedium;
        return value.ToLowerInvariant() switch
        {
            "xx-small" => medium * 9f / 16f,
            "x-small" => medium * 10f / 16f,
            "small" => medium * 13.3333f / 16f,
            "medium" => medium,
            "large" => medium * 18f / 16f,
            "x-large" => medium * 24f / 16f,
            "xx-large" => medium * 32f / 16f,
            "xxx-large" => medium * 48f / 16f,
            "larger" => parentFontSize * 1.2f,
            "smaller" => parentFontSize / 1.2f,
            _ => parentFontSize,
        };
    }

    private static bool TryLength(string value, string unit, out float number)
    {
        number = 0;
        if (unit.Length == 0 || !value.EndsWith(unit, StringComparison.OrdinalIgnoreCase))
            return false;
        return float.TryParse(value[..^unit.Length].Trim(), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out number) && value.Length > unit.Length;
    }

    public static float ToPixelsOrDefault(Length length, float defaultValue = 0, float viewportWidth = 0, float viewportHeight = 0)
    {
        if (length == null) return defaultValue;
        if (length is PixelLength p) return p.Value;
        if (length is EmLength e)
        {
            var reference = defaultValue > 0 ? defaultValue : 16f;
            return e.Value * reference;
        }
        if (length is RemLength r)
        {
            var root = defaultValue > 0 ? defaultValue : 16f;
            return r.Value * root;
        }
        if (length is PercentLength perc)
        {
            var reference = defaultValue > 0 ? defaultValue : 0f;
            return perc.Value * reference;
        }
        if (length is MathLength ml)
        {
            var reference = defaultValue > 0 ? defaultValue : 16f;
            return ml.ToPixels(reference, 16, viewportWidth, viewportHeight);
        }
        return defaultValue;
    }
}

public class AutoLength : Length
{
    public static readonly AutoLength Instance = new();
    public override float ToPixels(float reference, float rootFontSize, float viewportWidth, float viewportHeight) => float.NaN;
    public override string ToString() => "auto";
}

public enum IntrinsicSizeKind { MaxContent, MinContent, FitContent }

/// <summary>`width/height: max-content | min-content | fit-content` (CSS Sizing 3 §4).
/// Resolves from the box's own intrinsic contributions, so it carries no length.</summary>
public class IntrinsicLength : Length
{
    public IntrinsicSizeKind Kind { get; }
    public IntrinsicLength(IntrinsicSizeKind kind) => Kind = kind;
    public override float ToPixels(float reference, float rootFontSize, float viewportWidth, float viewportHeight) => float.NaN;
    public override string ToString() => Kind switch
    {
        IntrinsicSizeKind.MaxContent => "max-content",
        IntrinsicSizeKind.MinContent => "min-content",
        _ => "fit-content",
    };
}

public class PixelLength : Length
{
    public float Value { get; }
    public PixelLength(float value) => Value = value;
    public override float ToPixels(float reference, float rootFontSize, float viewportWidth, float viewportHeight) => Value;
    public override string ToString() => $"{Value}px";
}

public class EmLength : Length
{
    public float Value { get; }
    public EmLength(float value) => Value = value;
    public override float ToPixels(float reference, float rootFontSize, float viewportWidth, float viewportHeight) => Value * reference;
    public override string ToString() => $"{Value}em";
}

public class RemLength : Length
{
    public float Value { get; }
    public RemLength(float value) => Value = value;
    public override float ToPixels(float reference, float rootFontSize, float viewportWidth, float viewportHeight) => Value * rootFontSize;
    public override string ToString() => $"{Value}rem";
}

public class PercentLength : Length
{
    public float Value { get; }
    public PercentLength(float value) => Value = value;
    public override float ToPixels(float reference, float rootFontSize, float viewportWidth, float viewportHeight) => Value * reference;
    public override string ToString() => $"{Value * 100}%";
}

public class VwLength : Length
{
    public float Value { get; }
    public VwLength(float value) => Value = value;
    public override float ToPixels(float reference, float rootFontSize, float viewportWidth, float viewportHeight) => Value * viewportWidth / 100f;
    public override string ToString() => $"{Value}vw";
}

public class VhLength : Length
{
    public float Value { get; }
    public VhLength(float value) => Value = value;
    public override float ToPixels(float reference, float rootFontSize, float viewportWidth, float viewportHeight) => Value * viewportHeight / 100f;
    public override string ToString() => $"{Value}vh";
}

public class VminLength : Length
{
    public float Value { get; }
    public VminLength(float value) => Value = value;
    public override float ToPixels(float reference, float rootFontSize, float viewportWidth, float viewportHeight) => Value * Math.Min(viewportWidth, viewportHeight) / 100f;
    public override string ToString() => $"{Value}vmin";
}

public class VmaxLength : Length
{
    public float Value { get; }
    public VmaxLength(float value) => Value = value;
    public override float ToPixels(float reference, float rootFontSize, float viewportWidth, float viewportHeight) => Value * Math.Max(viewportWidth, viewportHeight) / 100f;
    public override string ToString() => $"{Value}vmax";
}

public class ExLength : Length
{
    public float Value { get; }
    public ExLength(float value) => Value = value;
    public override float ToPixels(float reference, float rootFontSize, float viewportWidth, float viewportHeight)
    {
        var m = CurrentFontMetrics();
        return Value * (m?.XHeight ?? reference * 0.5f);
    }
    public override string ToString() => $"{Value}ex";
}

public class ChLength : Length
{
    public float Value { get; }
    public ChLength(float value) => Value = value;
    public override float ToPixels(float reference, float rootFontSize, float viewportWidth, float viewportHeight)
    {
        // 'ch' is the advance of the '0' glyph (CSS Values 4 §6.3).
        var m = CurrentFontMetrics();
        return Value * (m?.ZeroWidth ?? reference * 0.5f);
    }
    public override string ToString() => $"{Value}ch";
}

public class IcLength : Length
{
    public float Value { get; }
    public IcLength(float value) => Value = value;
    public override float ToPixels(float reference, float rootFontSize, float viewportWidth, float viewportHeight)
    {
        // 'ic' is the advance of '水' in the element's font; 1em is the
        // fallback for fonts without the metric (CSS Values 4 §6.3).
        var m = CurrentFontMetrics();
        float ic = m?.IdeographicWidth ?? 0f;
        return Value * (ic > 0 ? ic : reference);
    }
    public override string ToString() => $"{Value}ic";
}

// Container query units (temporarily mapped to viewport until container support)
public class CqWLength : Length
{
    public float Value { get; }
    public CqWLength(float value) => Value = value;
    public override float ToPixels(float reference, float rootFontSize, float viewportWidth, float viewportHeight) => Value * viewportWidth / 100f;
    public override string ToString() => $"{Value}cqw";
}

public class CqHLength : Length
{
    public float Value { get; }
    public CqHLength(float value) => Value = value;
    public override float ToPixels(float reference, float rootFontSize, float viewportWidth, float viewportHeight) => Value * viewportHeight / 100f;
    public override string ToString() => $"{Value}cqh";
}

public class CqILength : Length
{
    public float Value { get; }
    public CqILength(float value) => Value = value;
    public override float ToPixels(float reference, float rootFontSize, float viewportWidth, float viewportHeight) => Value * viewportWidth / 100f;
    public override string ToString() => $"{Value}cqi";
}

public class CqBLength : Length
{
    public float Value { get; }
    public CqBLength(float value) => Value = value;
    public override float ToPixels(float reference, float rootFontSize, float viewportWidth, float viewportHeight) => Value * viewportHeight / 100f;
    public override string ToString() => $"{Value}cqb";
}

public class CqMinLength : Length
{
    public float Value { get; }
    public CqMinLength(float value) => Value = value;
    public override float ToPixels(float reference, float rootFontSize, float viewportWidth, float viewportHeight) => Value * Math.Min(viewportWidth, viewportHeight) / 100f;
    public override string ToString() => $"{Value}cqmin";
}

public class CqMaxLength : Length
{
    public float Value { get; }
    public CqMaxLength(float value) => Value = value;
    public override float ToPixels(float reference, float rootFontSize, float viewportWidth, float viewportHeight) => Value * Math.Max(viewportWidth, viewportHeight) / 100f;
    public override string ToString() => $"{Value}cqmax";
}

// Dynamic viewport units
public class DVwLength : Length
{
    public float Value { get; }
    public DVwLength(float value) => Value = value;
    public override float ToPixels(float reference, float rootFontSize, float viewportWidth, float viewportHeight) => Value * viewportWidth / 100f;
    public override string ToString() => $"{Value}dvw";
}

public class DVhLength : Length
{
    public float Value { get; }
    public DVhLength(float value) => Value = value;
    public override float ToPixels(float reference, float rootFontSize, float viewportWidth, float viewportHeight) => Value * viewportHeight / 100f;
    public override string ToString() => $"{Value}dvh";
}

// Small viewport units
public class SVwLength : Length
{
    public float Value { get; }
    public SVwLength(float value) => Value = value;
    public override float ToPixels(float reference, float rootFontSize, float viewportWidth, float viewportHeight) => Value * viewportWidth / 100f;
    public override string ToString() => $"{Value}svw";
}

public class SVhLength : Length
{
    public float Value { get; }
    public SVhLength(float value) => Value = value;
    public override float ToPixels(float reference, float rootFontSize, float viewportWidth, float viewportHeight) => Value * viewportHeight / 100f;
    public override string ToString() => $"{Value}svh";
}

// Large viewport units
public class LVwLength : Length
{
    public float Value { get; }
    public LVwLength(float value) => Value = value;
    public override float ToPixels(float reference, float rootFontSize, float viewportWidth, float viewportHeight) => Value * viewportWidth / 100f;
    public override string ToString() => $"{Value}lvw";
}

public class LVhLength : Length
{
    public float Value { get; }
    public LVhLength(float value) => Value = value;
    public override float ToPixels(float reference, float rootFontSize, float viewportWidth, float viewportHeight) => Value * viewportHeight / 100f;
    public override string ToString() => $"{Value}lvh";
}

// Inline/block-axis viewport units (vi = viewport inline, vb = viewport block)
public class ViLength : Length
{
    public float Value { get; }
    public ViLength(float value) => Value = value;
    public override float ToPixels(float reference, float rootFontSize, float viewportWidth, float viewportHeight) => Value * viewportWidth / 100f;
    public override string ToString() => $"{Value}vi";
}

public class VbLength : Length
{
    public float Value { get; }
    public VbLength(float value) => Value = value;
    public override float ToPixels(float reference, float rootFontSize, float viewportWidth, float viewportHeight) => Value * viewportHeight / 100f;
    public override string ToString() => $"{Value}vb";
}

// Font-relative units
public class RexLength : Length
{
    public float Value { get; }
    public RexLength(float value) => Value = value;
    public override float ToPixels(float reference, float rootFontSize, float viewportWidth, float viewportHeight)
    {
        var m = CurrentFontMetrics();
        return Value * (m?.XHeight ?? rootFontSize * 0.5f);
    }
    public override string ToString() => $"{Value}rex";
}

public class RicLength : Length
{
    public float Value { get; }
    public RicLength(float value) => Value = value;
    public override float ToPixels(float reference, float rootFontSize, float viewportWidth, float viewportHeight)
    {
        // 'ric' is the root font's ideographic advance; the root font family is
        // not reachable from here and CJK fonts use an exactly-1em advance.
        return Value * rootFontSize;
    }
    public override string ToString() => $"{Value}ric";
}

public class LhLength : Length
{
    public float Value { get; }
    public LhLength(float value) => Value = value;
    public override float ToPixels(float reference, float rootFontSize, float viewportWidth, float viewportHeight)
    {
        var style = FontUnitContext.Current;
        return Value * (style != null && style.FontSize > 0
            ? Acrux.Core.Fonts.LineBoxMetrics.GetLineHeight(style)
            : reference);
    }
    public override string ToString() => $"{Value}lh";
}

public class RlhLength : Length
{
    public float Value { get; }
    public RlhLength(float value) => Value = value;
    public override float ToPixels(float reference, float rootFontSize, float viewportWidth, float viewportHeight)
    {
        var style = FontUnitContext.Current;
        return Value * (style != null && style.FontSize > 0
            ? Acrux.Core.Fonts.LineBoxMetrics.GetLineHeight(style)
            : rootFontSize);
    }
    public override string ToString() => $"{Value}rlh";
}

public class CapLength : Length
{
    public float Value { get; }
    public CapLength(float value) => Value = value;
    public override float ToPixels(float reference, float rootFontSize, float viewportWidth, float viewportHeight)
    {
        var m = CurrentFontMetrics();
        return Value * (m?.CapHeight ?? reference * 0.7f);
    }
    public override string ToString() => $"{Value}cap";
}

public class RcapLength : Length
{
    public float Value { get; }
    public RcapLength(float value) => Value = value;
    public override float ToPixels(float reference, float rootFontSize, float viewportWidth, float viewportHeight)
    {
        var m = CurrentFontMetrics();
        return Value * (m?.CapHeight ?? rootFontSize * 0.7f);
    }
    public override string ToString() => $"{Value}rcap";
}

public class MathLength : Length
{
    public string Expression { get; }
    public MathLength(string expression) => Expression = expression;
    public override float ToPixels(float reference, float rootFontSize, float viewportWidth, float viewportHeight)
    {
        // 'reference' is the percentage base here; font-relative units come from the
        // ambient FontUnitContext, and percentages from the base published below.
        using var _pct = CssFunctionEvaluator.UsePercentageBase(reference);
        var evaluated = CssFunctionEvaluator.Evaluate(Expression, null, reference, rootFontSize, viewportWidth, viewportHeight, forceMath: true);
        if (evaluated.EndsWith("px") && float.TryParse(evaluated[..^2], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var epx))
            return epx;
        return float.NaN;
    }
    public override string ToString() => Expression;
}

public class ComputedStyle
{
    /// <summary>
    /// The computed -webkit-box-reflect value, if set. Mirrors StyleReflection
    /// in core/style/style_reflection.h.
    /// </summary>
    public StyleReflection? BoxReflect { get; set; }
    private readonly Dictionary<string, string> _customProperties = new(StringComparer.OrdinalIgnoreCase);

    public void SetCustomProperty(string name, string value) => _customProperties[name] = value;
    public string? GetCustomProperty(string name) => _customProperties.GetValueOrDefault(name);
    public bool HasCustomProperty(string name) => _customProperties.ContainsKey(name);
    public IEnumerable<KeyValuePair<string, string>> GetAllCustomProperties() => _customProperties;
    public Length Width { get; set; } = AutoLength.Instance;
    public Length Height { get; set; } = AutoLength.Instance;
    public Length Top { get; set; } = AutoLength.Instance;
    public Length Left { get; set; } = AutoLength.Instance;
    public Length Right { get; set; } = AutoLength.Instance;
    public Length Bottom { get; set; } = AutoLength.Instance;

    public Length MarginTop { get; set; } = new PixelLength(0);
    public Length MarginBottom { get; set; } = new PixelLength(0);
    public Length MarginLeft { get; set; } = new PixelLength(0);
    public Length MarginRight { get; set; } = new PixelLength(0);

    public Length PaddingTop { get; set; } = new PixelLength(0);
    public Length PaddingBottom { get; set; } = new PixelLength(0);
    public Length PaddingLeft { get; set; } = new PixelLength(0);
    public Length PaddingRight { get; set; } = new PixelLength(0);

    public float BorderTopWidth { get; set; }
    public float BorderBottomWidth { get; set; }
    public float BorderLeftWidth { get; set; }
    public float BorderRightWidth { get; set; }
    /// <summary>Which of the four border widths and the outline width a declaration
    /// actually wrote. Their initial value is 'medium', but a box that never mentions
    /// borders must take no space for them, so the widths start at zero and StyleAdjuster
    /// substitutes medium once a visible border style is known (CSS Backgrounds 3 §4).</summary>
    public uint AuthoredWidthSlots { get; set; }
    public SKColor BorderTopColor { get; set; } = SKColors.Black;
    public SKColor BorderBottomColor { get; set; } = SKColors.Black;
    public SKColor BorderLeftColor { get; set; } = SKColors.Black;
    public SKColor BorderRightColor { get; set; } = SKColors.Black;
    public BorderStyle BorderTopStyle { get; set; } = BorderStyle.None;
    public BorderStyle BorderBottomStyle { get; set; } = BorderStyle.None;
    public BorderStyle BorderLeftStyle { get; set; } = BorderStyle.None;
    public BorderStyle BorderRightStyle { get; set; } = BorderStyle.None;
    public float BorderTopLeftRadius { get; set; }
    public float BorderTopRightRadius { get; set; }
    public float BorderBottomLeftRadius { get; set; }
    public float BorderBottomRightRadius { get; set; }
    // Elliptical radii keep the vertical radius separately; both halves share the
    // same encoding (percentages stored negated, resolved against the box at paint).
    public float BorderTopLeftRadiusY { get; set; }
    public float BorderTopRightRadiusY { get; set; }
    public float BorderBottomLeftRadiusY { get; set; }
    public float BorderBottomRightRadiusY { get; set; }

    public DisplayType Display { get; set; } = DisplayType.Block;
    public PositionType Position { get; set; } = PositionType.Static;
    public FloatType Float { get; set; } = FloatType.None;
    public ClearType Clear { get; set; } = ClearType.None;

    public string FontFamily { get; set; } = Fonts.FontManager.StandardFontFamily;
    public float FontSize { get; set; } = 16;
    /// <summary>True while 'font-size' has never been given a real value anywhere up
    /// the chain — nothing declared, or only the initial keyword 'medium' (and
    /// descendants inheriting that). The generic 'monospace' carries its own size in a
    /// reference engine, and it substitutes only in this state; an authored
    /// 'font-size: 16px' blocks it even though it is numerically the default.
    /// Measured in Edge: parent 'font-size: medium' + child 'font-family: monospace'
    /// computes 13px, parent 'font-size: 16px' + the same child computes 16px.
    /// See <c>StyleAdjuster.AdjustMonospaceGenericFontSize</c>.</summary>
    public bool FontSizeIsDefault { get; set; } = true;
    public FontWeight FontWeight { get; set; } = FontWeight.Normal;
    public FontStyleType FontStyle { get; set; } = FontStyleType.Normal;
    /// <summary>Authored angle of 'font-style: oblique &lt;angle&gt;' (CSS Fonts 4 §3.2.2).
    /// Null means the style asked for the font family's own oblique/italic face.</summary>
    public float? FontStyleObliqueDegrees { get; set; }

    /// <summary>The 'font-style' value as the reference engine serializes it: a bare
    /// 'oblique' is reported as 'italic' (only an authored angle survives as 'oblique &lt;angle&gt;'),
    /// measured for b225 §D. Kept in one place so getComputedStyle and any dump agree.</summary>
    public string FontStyleCssText => FontStyle switch
    {
        FontStyleType.Italic => "italic",
        FontStyleType.Oblique => FontStyleObliqueDegrees is { } deg
            ? $"oblique {deg.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}deg"
            : "italic",
        _ => "normal",
    };
    public float LineHeight { get; set; } = 1.5f;

    /// <summary>
    /// True while 'line-height' still has its initial value of 'normal', meaning
    /// the used value comes from the primary font's own line spacing rather than
    /// from <see cref="LineHeight"/>. Any code that assigns an explicit
    /// line-height must clear this flag, otherwise the assignment is ignored by
    /// line box construction.
    /// </summary>
    public bool LineHeightIsNormal { get; set; } = true;

    /// <summary>
    /// Used value of 'line-height' in pixels when it was specified as a length
    /// (for example '24px'). Null when line-height is 'normal' or a number /
    /// percentage, in which case <see cref="LineHeight"/> holds the multiplier.
    /// </summary>
    public float? LineHeightPx { get; set; }

    public SKColor Color { get; set; } = SKColors.Black;
    public SKColor? BackgroundColor { get; set; }
    public List<string>? BackgroundImage { get; set; }
    public Length? BackgroundPositionX { get; set; }
    public Length? BackgroundPositionY { get; set; }
    public BackgroundRepeat BackgroundRepeat { get; set; } = BackgroundRepeat.Repeat;

    // ── Per-layer background geometry (CSS Backgrounds 3 §2) ────────────────
    // A background is a LIST of layers and every geometry longhand takes its own
    // comma-separated list, which cycles against the image count INDEPENDENTLY:
    // "three images, two positions, one size" means position 3 wraps to position 1
    // while all three share the single size. Flattening the lists into the scalars
    // above lost whole layers (a two-layer background-size parsed to a degenerate
    // box and the element painted nothing), so the lists are kept as written and
    // indexed at paint time. The scalars still hold the FIRST layer: every consumer
    // that has not been converted reads those, and one-layer backgrounds — the
    // overwhelming majority — are bit-identical either way.

    public List<BackgroundPositionLayer>? BackgroundPositionLayers { get; set; }
    public List<BackgroundSizeLayer>? BackgroundSizeLayers { get; set; }
    public List<BackgroundRepeatPair>? BackgroundRepeatLayers { get; set; }
    public List<BackgroundAttachment>? BackgroundAttachmentLayers { get; set; }
    public List<string>? BackgroundOriginLayers { get; set; }
    public List<string>? BackgroundClipLayers { get; set; }
    public BackgroundAttachment BackgroundAttachment { get; set; } = BackgroundAttachment.Scroll;

    public TextAlignType TextAlign { get; set; } = TextAlignType.Start;
    public TextAlignLastType TextAlignLast { get; set; } = TextAlignLastType.Auto;
    public TextDecorationType TextDecoration { get; set; } = TextDecorationType.None;
    public VerticalAlignType VerticalAlign { get; set; } = VerticalAlignType.Baseline;
    /// <summary>'vertical-align' given as a length or percentage: the box's baseline
    /// is raised by this amount (positive) relative to the parent's (CSS 2.1 §10.8.1).
    /// A percentage resolves against the element's own computed line-height.</summary>
    public float? VerticalAlignOffsetPx { get; set; }
    /// <summary>True when a 'vertical-align' declaration was actually cascaded.
    /// Table cells default to 'middle', and that default must not override an
    /// explicit declaration (CSS 2.1 §17.5.2.6).
    /// </summary>
    public bool VerticalAlignIsAuthored { get; set; }
    public WhiteSpaceMode WhiteSpace { get; set; } = WhiteSpaceMode.Normal;
    /// <summary>The modular pieces 'white-space' and 'text-wrap' are shorthands of
    /// (CSS Text 4 §1). <see cref="WhiteSpace"/> above stays the value layout consumes,
    /// and is always kept in sync with this triple by <c>CssPropertyApplier</c> —
    /// whichever of the shorthand and the longhands wins the cascade writes both.</summary>
    public WhiteSpaceCollapseType WhiteSpaceCollapse { get; set; } = WhiteSpaceCollapseType.Collapse;
    public TextWrapModeType TextWrapMode { get; set; } = TextWrapModeType.Wrap;
    /// <summary>'text-wrap-style'. <c>Auto</c> is the initial value; the legacy
    /// 'balance'/'pretty' keywords live on <see cref="TextWrap"/> so the existing
    /// line-balancing code keeps working.</summary>
    public TextWrapStyleType TextWrapStyle { get; set; } = TextWrapStyleType.Auto;
    public WordBreakMode WordBreak { get; set; } = WordBreakMode.Normal;
    public OverflowWrapMode OverflowWrap { get; set; } = OverflowWrapMode.Normal;

    // ---- Scrollbars (standard props + ::-webkit-scrollbar-* side-car) ----
    /// <summary>scrollbar-width: auto | thin | none.</summary>
    public ScrollbarWidthType ScrollbarWidth { get; set; } = ScrollbarWidthType.Auto;
    /// <summary>scrollbar-color first value (thumb). Null = UA default.</summary>
    public SKColor? ScrollbarThumbColor { get; set; }
    /// <summary>scrollbar-color second value (track).</summary>
    public SKColor? ScrollbarTrackColor { get; set; }
    /// <summary>Collected ::-webkit-scrollbar-* part styles; null when none matched.</summary>
    public ScrollbarStyles? ScrollbarCustom { get; set; }

    public OverflowType Overflow { get; set; } = OverflowType.Visible;
    /// <summary>'overflow-clip-margin' (CSS Overflow 3 §4.1): how far the clip region
    /// may extend past the padding box. Only applies when the box actually clips, and
    /// a percentage resolves against the corresponding box's dimensions — the initial
    /// value is 0, so 'clip' hugs the padding box exactly.</summary>
    public Length? OverflowClipMargin { get; set; }
    /// <summary>The keyword form of 'overflow-clip-margin' (CSS Overflow 3 §4.1):
    /// the clip edge is pushed to the named box instead of by a length. The initial
    /// value is 0, which is 'padding-box' with no offset.</summary>
    public OverflowClipMarginBox OverflowClipMarginBox { get; set; } = OverflowClipMarginBox.PaddingBox;
    public OverflowType OverflowX { get; set; } = OverflowType.Visible;
    public OverflowType OverflowY { get; set; } = OverflowType.Visible;
    public VisibilityType Visibility { get; set; } = VisibilityType.Visible;
    public int? ZIndex { get; set; }
    public string? Cursor { get; set; } = "auto";
    public float Opacity { get; set; } = 1.0f;
    public List<BoxShadowValue>? BoxShadow { get; set; }
    public BackgroundSizeType BackgroundSize { get; set; } = BackgroundSizeType.Auto;
    public Length? BackgroundSizeWidth { get; set; }
    public Length? BackgroundSizeHeight { get; set; }

    public FlexDirectionType FlexDirection { get; set; } = FlexDirectionType.Row;
    public FlexWrapType FlexWrap { get; set; } = FlexWrapType.NoWrap;
    public float FlexGrow { get; set; } = 0;
    public float FlexShrink { get; set; } = 1;
    public Length FlexBasis { get; set; } = AutoLength.Instance;
    public JustifyContentType JustifyContent { get; set; } = JustifyContentType.FlexStart;
    public AlignItemsType AlignItems { get; set; } = AlignItemsType.Stretch;
    public AlignSelfType AlignSelf { get; set; } = AlignSelfType.Auto;

    public Length? MinWidth { get; set; }
    public Length? MaxWidth { get; set; }
    public Length? MinHeight { get; set; }
    public Length? MaxHeight { get; set; }

    public BoxSizingType BoxSizing { get; set; } = BoxSizingType.ContentBox;
    /// <summary>True when a 'box-sizing' declaration was actually cascaded. Table
    /// boxes default to border-box (their specified width is the border-box
    /// width), and that default must not override an explicit declaration.
    /// </summary>
    public bool BoxSizingIsAuthored { get; set; }
    public bool BorderCollapse { get; set; }
    public ListStyleType ListStyleType { get; set; } = ListStyleType.Disc;
    /// <summary>Marker text used when list-style-type is a &lt;string&gt;.</summary>
    public string? ListStyleTypeString { get; set; }
    /// <summary>The &lt;custom-ident&gt; of list-style-type when it names a @counter-style
    /// rule (CSS Counter Styles §3.2). Unknown names fall back to decimal.</summary>
    public string? ListStyleTypeName { get; set; }
    public string? ListStyleImage { get; set; }
    public ListStylePosition ListStylePosition { get; set; } = ListStylePosition.Outside;

    public string? Transform { get; set; }
    public string? TransformOrigin { get; set; } = "50% 50% 0";
    // Independent transform properties (CSS Transforms 2 §3.1-3.3). They apply in
    // the order translate → rotate → scale → transform.
    public string? Translate { get; set; }
    public string? Rotate { get; set; }
    public string? Scale { get; set; }
    public string? Transition { get; set; }
    public string? TransitionDelay { get; set; }
    public string? TransitionDuration { get; set; }
    public string? TransitionProperty { get; set; }
    public string? TransitionTimingFunction { get; set; }
    public string? Animation { get; set; }
    public string? AnimationName { get; set; }
    public string? AnimationDuration { get; set; }
    public string? AnimationTimingFunction { get; set; }
    public string? AnimationDelay { get; set; }
    public string? AnimationIterationCount { get; set; }
    public string? AnimationDirection { get; set; }
    public string? AnimationFillMode { get; set; }
    public string? AnimationPlayState { get; set; }
    public string? PointerEvents { get; set; } = "auto";
    public string? UserSelect { get; set; } = "auto";
    public string Direction { get; set; } = "ltr";
    /// <summary>Logical box properties as authored, queued for a second mapping pass
    /// in <c>StyleAdjuster</c>. A logical edge addresses an edge, not a side, so it
    /// resolves against the element's <i>final</i> computed 'direction' (CSS Logical
    /// Properties 1 §2): <c>{ margin-inline-start: 13px; direction: rtl }</c> still
    /// lands on the right, which a single pass over the declarations cannot know.
    /// Null until a logical box property is seen.</summary>
    public System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, string>>? PendingBoxEdgeProperties { get; set; }
    public float LetterSpacing { get; set; }
    public float WordSpacing { get; set; }
    public float TextIndent { get; set; }
    /// <summary>'text-indent: hanging' inverts the indent: the first line stays at
    /// the start edge and the remaining lines are indented (CSS Text 3 §5.2).</summary>
    public bool TextIndentHanging { get; set; }
    /// <summary>'text-indent: each-line' also indents the lines that follow a forced
    /// break, not just the first line of the block (CSS Text 3 §5.2).</summary>
    public bool TextIndentEachLine { get; set; }
    /// <summary>Percentage part of 'text-indent', resolved against the containing
    /// block's inline size at line-break time (CSS Text 3 §5.2).</summary>
    public float TextIndentPercent { get; set; }
    /// <summary>A calc()/min()/max()/clamp() text-indent whose percentage must
    /// resolve against the containing block inline size, deferred to line-break
    /// time. Stores the raw math expression; null when text-indent is not math.</summary>
    public string? TextIndentMath { get; set; }
    public string TextTransform { get; set; } = "none";
    public TextOverflowType TextOverflow { get; set; } = TextOverflowType.Clip;
    /// <summary>The &lt;string&gt; form of 'text-overflow' (CSS UI 4 §4.4). When set it
    /// replaces the ellipsis glyph, and the truncation reserves the string's own
    /// measured width instead.</summary>
    public string? TextOverflowString { get; set; }
    // -webkit-line-clamp / line-clamp: max number of visible lines before the
    // block truncates with an ellipsis. 0 means no clamping.
    public int LineClamp { get; set; }
    // text-wrap keyword: "normal" | "balance" | "nowrap" | "pretty" (lowercased).
    public string TextWrap { get; set; } = "normal";
    public List<TextShadowValue> TextShadow { get; set; } = new();
public TextDecorationLineType TextDecorationLine { get; set; } = TextDecorationLineType.None;
public TextDecorationStyleType TextDecorationStyle { get; set; } = TextDecorationStyleType.Solid;
/// <summary>'text-decoration-color' as authored. Meaningful only when
/// <see cref="TextDecorationColorIsAuto"/> is false; the initial value 'auto'
/// paints with the element's own 'color', which is resolved at use time.</summary>
public SKColor TextDecorationColor { get; set; }
/// <summary>Authored 'auto' keyword (or no declaration at all) for
/// 'text-decoration-color' (CSS Text Decoration 4 §3.2, initial value 'auto').
/// The alpha channel cannot double as this marker: an explicit 'transparent'
/// must stay invisible, while 'auto' must resolve to 'color'.</summary>
public bool TextDecorationColorIsAuto { get; set; } = true;
/// <summary><see cref="TextDecorationColor"/> with 'auto' resolved to the
/// element's own 'color', as a reference engine's computed value reports it.</summary>
public SKColor ResolvedTextDecorationColor => TextDecorationColorIsAuto ? Color : TextDecorationColor;
/// <summary>Resolved 'text-decoration-thickness' in px; NaN means 'auto'.</summary>
public float TextDecorationThickness { get; set; } = float.NaN;
/// <summary>'text-decoration-thickness: from-font' (CSS Text Decoration 4 §3.4).</summary>
public bool TextDecorationThicknessFromFont { get; set; }
/// <summary>Authored 'auto' keyword of 'text-underline-offset'.</summary>
public bool TextUnderlineOffsetIsAuto { get; set; } = true;
public float TextUnderlineOffset { get; set; }
/// <summary>'text-underline-position' (CSS Text Decoration 4 §3.2).</summary>
public TextUnderlinePositionType TextUnderlinePosition { get; set; } = TextUnderlinePositionType.Auto;
/// <summary>'text-decoration-skip-ink: none' asks for an unbroken line.</summary>
public bool TextDecorationSkipInk { get; set; } = true;
/// <summary>'box-decoration-break' (CSS Fragmentation 3 §4.2): whether a box that
/// is split across lines/columns repeats its decoration on each fragment.</summary>
public BoxDecorationBreakType BoxDecorationBreak { get; set; } = BoxDecorationBreakType.Slice;

private System.Collections.Generic.List<AppliedTextDecoration>? _appliedTextDecorations;

/// <summary>
/// The list of text decorations that apply to text in this style, gathering
/// this style's own text-decoration properties. Mirrors
/// ComputedStyle::AppliedTextDecorations() in applied_text_decoration.h. The
/// instance is cached so that reference identity can be compared (the paint
/// pipeline uses identity to detect decoration propagation across a parent
/// chain, see inline_paint_context.cc).
/// </summary>
    public System.Collections.Generic.List<AppliedTextDecoration> AppliedTextDecorations()
    {
        if (_appliedTextDecorations != null)
            return _appliedTextDecorations;

        var list = new System.Collections.Generic.List<AppliedTextDecoration>();
        if (TextDecorationLine != TextDecorationLineType.None)
        {
            // 'text-decoration-color' has the initial value 'auto', which paints
            // with the originating box's own text color. That fallback must key on
            // the 'auto' keyword itself, not on the alpha channel — an authored
            // 'transparent' is an explicit color and draws nothing.
            var decorationColor = ResolvedTextDecorationColor;
            list.Add(new AppliedTextDecoration(
                TextDecorationLine, TextDecorationStyle, decorationColor,
                TextDecorationThickness, TextDecorationThicknessFromFont,
                TextUnderlineOffset, TextUnderlineOffsetIsAuto, TextUnderlinePosition,
                TextDecorationSkipInk, FontSize));
        }
        _appliedTextDecorations = list;
        return list;
    }

    /// <summary>The text decorations before applying ::first-line overrides.</summary>
    public System.Collections.Generic.List<AppliedTextDecoration> BaseAppliedTextDecorations() => AppliedTextDecorations();
    public string TextEmphasis { get; set; } = "none";
    public string TextEmphasisColor { get; set; } = "currentcolor";
    public string TextEmphasisStyle { get; set; } = "none";
    public string TextEmphasisPosition { get; set; } = "over right";

    public Length RowGap { get; set; } = new PixelLength(0);
    public Length ColumnGap { get; set; } = new PixelLength(0);
    public int ColumnCount { get; set; }
    public Length? ColumnWidth { get; set; }
    // 'column-fill: balance | auto'. Balance (the initial value) equalises column
    // heights; auto fills each column to the fragmentainer height before the next.
    public string ColumnFill { get; set; } = "balance";

    // A5: column-rule (multicol separator line). Default 'medium none currentcolor'.
    public float ColumnRuleWidth { get; set; } = 3f;
    public BorderStyle ColumnRuleStyle { get; set; } = BorderStyle.None;
    public SKColor? ColumnRuleColor { get; set; }

    public float OutlineWidth { get; set; }
    public SKColor OutlineColor { get; set; } = SKColors.Black;
    public BorderStyle OutlineStyle { get; set; } = BorderStyle.None;
    public float OutlineOffset { get; set; }
    public string TableLayout { get; set; } = "auto";
    public string CaptionSide { get; set; } = "top";
    public string EmptyCells { get; set; } = "show";
    public string? Content { get; set; }
    public string CounterIncrement { get; set; } = "none";
    public string CounterReset { get; set; } = "none";
    public string CounterSet { get; set; } = "none";
    public string Quotes { get; set; } = "auto";

    public int Order { get; set; }

    /// <summary>Bit set of color properties whose declared value was the keyword
    /// `currentcolor`. The used value can only be known after inheritance, so the
    /// cascade records the slots here and StyleAdjuster resolves them against the
    /// computed `color` (CSS Color 3 §4.4).
    /// The border and outline colours start out as currentcolor because that is their
    /// initial value (CSS Backgrounds 3 §4, CSS UI 4 §5), so an undeclared border paints
    /// the element's text colour; an explicit value clears the bit as it is applied.</summary>
    public uint CurrentColorSlots { get; set; } =
        (uint)(ComputedStyle.CurrentColorSlot.AllBorders | ComputedStyle.CurrentColorSlot.Outline);

    [Flags]
    public enum CurrentColorSlot : uint
    {
        None = 0,
        BorderTop = 1 << 0,
        BorderRight = 1 << 1,
        BorderBottom = 1 << 2,
        BorderLeft = 1 << 3,
        Outline = 1 << 4,
        TextDecoration = 1 << 5,
        ColumnRule = 1 << 6,
        Caret = 1 << 7,
        AllBorders = BorderTop | BorderRight | BorderBottom | BorderLeft,
    }
    public float AspectRatio { get; set; }
    public ObjectFitType ObjectFit { get; set; } = ObjectFitType.Fill;
    public Length? ObjectPositionX { get; set; }
    public Length? ObjectPositionY { get; set; }
    public string FlexFlow { get; set; } = "row nowrap";
    public string AlignContent { get; set; } = "stretch";
    public string JustifyItems { get; set; } = "legacy";
    public string JustifySelf { get; set; } = "auto";
    public string PlaceContent { get; set; } = "normal";
    public string PlaceItems { get; set; } = "normal";
    public string PlaceSelf { get; set; } = "auto";

    public string? GridTemplateColumns { get; set; }
    public string? GridTemplateRows { get; set; }
    public string? GridTemplateAreas { get; set; }
    public string? GridAutoColumns { get; set; } = "auto";
    public string? GridAutoRows { get; set; } = "auto";
    public GridAutoFlowType GridAutoFlow { get; set; } = GridAutoFlowType.Row;
    public string? GridColumnStart { get; set; }
    public string? GridColumnEnd { get; set; }
    public string? GridRowStart { get; set; }
    public string? GridRowEnd { get; set; }
    public string? GridColumn { get; set; }
    public string? GridRow { get; set; }
    public string? GridArea { get; set; }
    public string? Grid { get; set; }

    public string BackgroundClip { get; set; } = "border-box";
    public string BackgroundOrigin { get; set; } = "padding-box";
    public BackgroundBlendModeType BackgroundBlendMode { get; set; } = BackgroundBlendModeType.Normal;

    public WritingModeType WritingMode { get; set; } = WritingModeType.HorizontalTb;
    // CSS Text 3 §4.4: the initial value is 'manual', which still honours the soft
    // hyphens the author wrote — only 'none' removes those break opportunities.
    public HyphensType Hyphens { get; set; } = HyphensType.Manual;
    /// <summary>'hyphenate-character' (CSS Text 4 §5.1, inherited). 'auto' lets the
    /// engine pick the language's hyphen, which in practice is '-'; any other value is
    /// the literal string drawn at a break — including a multi-character one, and the
    /// empty string, which breaks without drawing anything. Measured in Edge: 'auto',
    /// '-' and an invalid 'none' all give a 6.67px mark at 20px, '"="' gives 11.69,
    /// '"→"' gives 20.00 and '">>"' gives 23.36. Note 'none' is NOT a value of this
    /// property, so it must be dropped rather than parsed.</summary>
    public string HyphenateCharacter { get; set; } = "auto";
    /// <summary>The text actually drawn at a soft-hyphen break.</summary>
    public string EffectiveHyphenText => HyphenateCharacter == "auto" ? "-" : HyphenateCharacter;
    /// <summary>CSS Multi-Column 1 §3: 'column-span: all' makes the box span every
    /// column of its multicol ancestor.</summary>
    public bool ColumnSpanAll { get; set; }
    public float TabSize { get; set; } = 8;
    /// <summary>'tab-size' given in absolute/relative length units (CSS Text 3 §3.4).
    /// When set it wins over the unitless <see cref="TabSize"/> space count.</summary>
    public float? TabSizePx { get; set; }
    public ScrollBehaviorType ScrollBehavior { get; set; } = ScrollBehaviorType.Auto;
    public OverscrollBehaviorType OverscrollBehavior { get; set; } = OverscrollBehaviorType.Auto;
    public OverscrollBehaviorType OverscrollBehaviorX { get; set; } = OverscrollBehaviorType.Auto;
    public OverscrollBehaviorType OverscrollBehaviorY { get; set; } = OverscrollBehaviorType.Auto;
    public OverflowAnchorType OverflowAnchor { get; set; } = OverflowAnchorType.Auto;
    public ContainType Contain { get; set; } = ContainType.None;
    public ContentVisibilityType ContentVisibility { get; set; } = ContentVisibilityType.Visible;

    /// <summary>CSS Containment 3 §2.2/§2.3: 'layout' and 'paint' each establish an
    /// independent formatting context, while 'size' and 'style' do not — measured on the
    /// reference engine, a 'contain: style' box still lets its child's block-start margin
    /// collapse through it (20px where 'contain: layout' reports 40px).</summary>
    public bool HasLayoutContainment => (Contain & (ContainType.Layout | ContainType.Paint)) != 0;
    public bool HasPaintContainment => (Contain & ContainType.Paint) != 0;

    /// <summary>Style containment: the keyword, or either non-visible
    /// 'content-visibility' value, which implies it (CSS Containment 3 §3).</summary>
    public bool HasStyleContainment => (Contain & ContainType.Style) != 0
        || ContentVisibility is not ContentVisibilityType.Visible;

    /// <summary>CSS Containment 3 §3: 'content-visibility: hidden' applies size containment
    /// while its contents are not rendered, so the box collapses to its contain-intrinsic
    /// fallback; 'auto' only does so while it is skipped, which this engine never does (it
    /// renders the whole document), so 'auto' keeps its content block size.</summary>
    public bool HasSizeContainment => (Contain & ContainType.Size) != 0
        || ContentVisibility == ContentVisibilityType.Hidden;

    /// <summary>The union of the containment flags that turn the box into a paint boundary:
    /// layout and paint containment, and either non-visible 'content-visibility' value.</summary>
    public bool CreatesContainmentContext =>
        HasLayoutContainment || ContentVisibility is ContentVisibilityType.Auto or ContentVisibilityType.Hidden;

    /// <summary>'contain-intrinsic-width' / '-height': the size that replaces the box's own
    /// intrinsic size while size containment is on. Null means 'none' — no replacement, so
    /// the contained axis measures 0. The auto bit is the remembered-size variant of
    /// 'contain-intrinsic-size: auto &lt;length&gt;'; with no skip rendering it behaves as the
    /// fallback length, but it is kept so the computed value round-trips.</summary>
    public Length? ContainIntrinsicWidth { get; set; }
    public Length? ContainIntrinsicHeight { get; set; }
    public bool ContainIntrinsicWidthIsAuto { get; set; }
    public bool ContainIntrinsicHeightIsAuto { get; set; }

    public string WillChange { get; set; } = "auto";
    public SKColor? AccentColor { get; set; }
    public SKColor? CaretColor { get; set; }
    public string ColorScheme { get; set; } = "normal";
    public string Appearance { get; set; } = "auto";

    /// <summary>CSS 'field-sizing' (shipped by the reference engine as
    /// <c>normal | content</c>): 'content' replaces a text control's rendered size with the
    /// size of its own content — the value text, or the placeholder when there is none. The
    /// initial 'normal' keeps the widget size, which is what the reference engine reports as
    /// the computed value <c>fixed</c> on a form control.</summary>
    public FieldSizingType FieldSizing { get; set; } = FieldSizingType.Normal;

    public ForcedColorAdjustType ForcedColorAdjust { get; set; } = ForcedColorAdjustType.Auto;
    public ImageRenderingType ImageRendering { get; set; } = ImageRenderingType.Auto;
    public IsolationType Isolation { get; set; } = IsolationType.Auto;
    public MixBlendModeType MixBlendMode { get; set; } = MixBlendModeType.Normal;
    public string? Filter { get; set; }
    public string? BackdropFilter { get; set; }
    public string? ClipPath { get; set; }
    public string? Mask { get; set; }
    public string? MaskImage { get; set; }
    public string? MaskClip { get; set; }
    public string? MaskComposite { get; set; }
    public string? MaskMode { get; set; }
    public string? MaskOrigin { get; set; }
    public string? MaskPosition { get; set; }
    public string? MaskRepeat { get; set; }
    public string? MaskSize { get; set; }

    /// <summary>Per-layer values of the mask geometry longhands, the same shape the background
    /// longhands use: the scalar above holds the FIRST layer (so every single-layer mask keeps
    /// its existing path), and the list carries the rest. A mask with three images and two
    /// positions cycles the positions independently — CSS Backgrounds 3 §2, which 'mask'
    /// inherits verbatim (measured: 'mask-size: 50% 50%, 20px' on three layers computes back as
    /// '50% 50%, 20px, 50% 50%').</summary>
    public List<string>? MaskPositionLayers { get; set; }
    public List<string>? MaskSizeLayers { get; set; }
    public List<string>? MaskRepeatLayers { get; set; }
    public List<string>? MaskClipLayers { get; set; }
    public List<string>? MaskOriginLayers { get; set; }
    public List<string>? MaskModeLayers { get; set; }
    public List<string>? MaskCompositeLayers { get; set; }
    public LineBreakType LineBreak { get; set; } = LineBreakType.Auto;
    public TextJustifyType TextJustify { get; set; } = TextJustifyType.Auto;
    public ResizeType Resize { get; set; } = ResizeType.None;
    public string? HangingPunctuation { get; set; } = "none";
    public string? RubyAlign { get; set; } = "space-around";
    public string RubyPosition { get; set; } = "over";
    public float BorderSpacing { get; set; }
    /// <summary>Row (block-axis) border-spacing. 'border-spacing' takes one or two
    /// lengths; the second one only applies between rows (CSS 2.1 §17.5). Unset means
    /// "same as the column spacing", which is what a single value declares.</summary>
    public float? BorderRowSpacing { get; set; }
    public float UsedBorderRowSpacing => BorderRowSpacing ?? BorderSpacing;
    public string? BorderImageSource { get; set; }
    public string BorderImageSlice { get; set; } = "100%";
    public string BorderImageWidth { get; set; } = "1";
    public string BorderImageRepeat { get; set; } = "stretch";
    public string BorderImageOutset { get; set; } = "0";
    public float Zoom { get; set; } = 1;
    public int Orphans { get; set; } = 2;
    public int Widows { get; set; } = 2;
    public string FontVariant { get; set; } = "normal";
    public string FontVariantCaps { get; set; } = "normal";
    public string FontKerning { get; set; } = "auto";
    public string FontStretch { get; set; } = "normal";
    public FontSynthesisType FontSynthesis { get; set; } = FontSynthesisType.Auto;
    public string FontOpticalSizing { get; set; } = "auto";
    public string FontVariationSettings { get; set; } = "normal";
    public string FontFeatureSettings { get; set; } = "normal";
    public float FontSizeAdjust { get; set; }
    public string TextRendering { get; set; } = "auto";
    public string UnicodeBidi { get; set; } = "normal";

    public float GetWidth(float viewportWidth, float rootFontSize)
    {
        if (Width is AutoLength) return float.NaN;
        return Width.ToPixels(viewportWidth, rootFontSize, viewportWidth, 0);
    }

    public float GetHeight(float viewportHeight, float rootFontSize)
    {
        if (Height is AutoLength) return float.NaN;
        return Height.ToPixels(viewportHeight, rootFontSize, 0, viewportHeight);
    }

    /// <summary>
    /// 获取计算后的像素值（用于 getComputedStyle）
    /// </summary>
    public float GetComputedPixel(Length length, float reference = 16f, float rootFontSize = 16f,
        float viewportWidth = 0, float viewportHeight = 0)
    {
        if (length is PixelLength p) return p.Value;
        if (length is AutoLength) return float.NaN;
        return length.ToPixels(reference, rootFontSize, viewportWidth, viewportHeight);
    }

    /// <summary>
    /// 格式化长度为 CSS 像素字符串
    /// </summary>
    public string FormatComputedLength(Length length, float reference = 16f)
    {
        var px = GetComputedPixel(length, reference, FontSize, 0, 0);
        if (float.IsNaN(px)) return "auto";
        return $"{px:F1}px";
    }

    /// <summary>Copy a per-layer background list so a cloned style never shares a
    /// mutable list with its source. Null stays null (= "no list was authored").</summary>
    private static List<T>? CopyLayerList<T>(List<T>? list) =>
        list == null ? null : new List<T>(list);

    public ComputedStyle Clone()
    {
        return new ComputedStyle
        {
            Width = Width, Height = Height,
            Top = Top, Left = Left, Right = Right, Bottom = Bottom,
            MarginTop = MarginTop, MarginRight = MarginRight, MarginBottom = MarginBottom, MarginLeft = MarginLeft,
            PaddingTop = PaddingTop, PaddingRight = PaddingRight, PaddingBottom = PaddingBottom, PaddingLeft = PaddingLeft,
            BorderTopWidth = BorderTopWidth, BorderRightWidth = BorderRightWidth,
            BorderBottomWidth = BorderBottomWidth, BorderLeftWidth = BorderLeftWidth,
            AuthoredWidthSlots = AuthoredWidthSlots,
            BorderTopColor = BorderTopColor, BorderRightColor = BorderRightColor,
            BorderBottomColor = BorderBottomColor, BorderLeftColor = BorderLeftColor,
            BorderTopStyle = BorderTopStyle, BorderRightStyle = BorderRightStyle,
            BorderBottomStyle = BorderBottomStyle, BorderLeftStyle = BorderLeftStyle,
            BorderTopLeftRadius = BorderTopLeftRadius, BorderTopRightRadius = BorderTopRightRadius,
            BorderBottomRightRadius = BorderBottomRightRadius, BorderBottomLeftRadius = BorderBottomLeftRadius,
            BorderTopLeftRadiusY = BorderTopLeftRadiusY, BorderTopRightRadiusY = BorderTopRightRadiusY,
            BorderBottomRightRadiusY = BorderBottomRightRadiusY, BorderBottomLeftRadiusY = BorderBottomLeftRadiusY,
            Display = Display, Position = Position, Float = Float, Clear = Clear,
            FontFamily = FontFamily, FontSize = FontSize, FontSizeIsDefault = FontSizeIsDefault, FontWeight = FontWeight,
            FontStyle = FontStyle, FontStyleObliqueDegrees = FontStyleObliqueDegrees, LineHeight = LineHeight,
            LineHeightIsNormal = LineHeightIsNormal, LineHeightPx = LineHeightPx,
            Color = Color, BackgroundColor = BackgroundColor, BackgroundImage = BackgroundImage,
            BackgroundPositionX = BackgroundPositionX, BackgroundPositionY = BackgroundPositionY,
            BackgroundRepeat = BackgroundRepeat, BackgroundAttachment = BackgroundAttachment,
            // The per-layer lists are copied (the entries themselves are written once by
            // the parser and treated as immutable afterwards).
            MaskPositionLayers = CopyLayerList(MaskPositionLayers),
            MaskSizeLayers = CopyLayerList(MaskSizeLayers),
            MaskRepeatLayers = CopyLayerList(MaskRepeatLayers),
            MaskClipLayers = CopyLayerList(MaskClipLayers),
            MaskOriginLayers = CopyLayerList(MaskOriginLayers),
            MaskModeLayers = CopyLayerList(MaskModeLayers),
            MaskCompositeLayers = CopyLayerList(MaskCompositeLayers),
            BackgroundPositionLayers = CopyLayerList(BackgroundPositionLayers),
            BackgroundSizeLayers = CopyLayerList(BackgroundSizeLayers),
            BackgroundRepeatLayers = CopyLayerList(BackgroundRepeatLayers),
            BackgroundAttachmentLayers = CopyLayerList(BackgroundAttachmentLayers),
            BackgroundOriginLayers = CopyLayerList(BackgroundOriginLayers),
            BackgroundClipLayers = CopyLayerList(BackgroundClipLayers),
            TextAlign = TextAlign, TextAlignLast = TextAlignLast, TextDecoration = TextDecoration, VerticalAlign = VerticalAlign, VerticalAlignOffsetPx = VerticalAlignOffsetPx,
            VerticalAlignIsAuthored = VerticalAlignIsAuthored,
            WhiteSpace = WhiteSpace, WordBreak = WordBreak, OverflowWrap = OverflowWrap,
            ScrollbarWidth = ScrollbarWidth, ScrollbarThumbColor = ScrollbarThumbColor,
            ScrollbarTrackColor = ScrollbarTrackColor, ScrollbarCustom = ScrollbarCustom,
            Overflow = Overflow, OverflowX = OverflowX, OverflowY = OverflowY,
            Visibility = Visibility, ZIndex = ZIndex, Cursor = Cursor, Opacity = Opacity,
            BoxShadow = BoxShadow, BackgroundSize = BackgroundSize,
            BackgroundSizeWidth = BackgroundSizeWidth, BackgroundSizeHeight = BackgroundSizeHeight,
            FlexDirection = FlexDirection, FlexWrap = FlexWrap, FlexGrow = FlexGrow,
            FlexShrink = FlexShrink, FlexBasis = FlexBasis, JustifyContent = JustifyContent,
            AlignItems = AlignItems, AlignSelf = AlignSelf,
            MinWidth = MinWidth, MaxWidth = MaxWidth, MinHeight = MinHeight, MaxHeight = MaxHeight,
            BoxSizing = BoxSizing, BoxSizingIsAuthored = BoxSizingIsAuthored,
            BorderCollapse = BorderCollapse,
            ListStyleType = ListStyleType, ListStyleTypeString = ListStyleTypeString,
            ListStyleTypeName = ListStyleTypeName,
            ListStyleImage = ListStyleImage, ListStylePosition = ListStylePosition,
            Transform = Transform, TransformOrigin = TransformOrigin,
            Translate = Translate, Rotate = Rotate, Scale = Scale,
            Transition = Transition, TransitionDelay = TransitionDelay, TransitionDuration = TransitionDuration,
            TransitionProperty = TransitionProperty, TransitionTimingFunction = TransitionTimingFunction,
            Animation = Animation, AnimationName = AnimationName, AnimationDuration = AnimationDuration,
            AnimationTimingFunction = AnimationTimingFunction, AnimationDelay = AnimationDelay,
            AnimationIterationCount = AnimationIterationCount, AnimationDirection = AnimationDirection,
            AnimationFillMode = AnimationFillMode, AnimationPlayState = AnimationPlayState,
            PointerEvents = PointerEvents, UserSelect = UserSelect,
            Direction = Direction, LetterSpacing = LetterSpacing, WordSpacing = WordSpacing,
            // Carried so a style cloned before adjustment still gets its logical
            // properties re-mapped; replaying an already-mapped pair is idempotent.
            PendingBoxEdgeProperties = PendingBoxEdgeProperties == null
                ? null : new(PendingBoxEdgeProperties),
            TextIndent = TextIndent, TextIndentHanging = TextIndentHanging,
            TextIndentEachLine = TextIndentEachLine,
            TextIndentPercent = TextIndentPercent, TextIndentMath = TextIndentMath, TextTransform = TextTransform,
            TextOverflow = TextOverflow, TextOverflowString = TextOverflowString,
            TextShadow = TextShadow, LineClamp = LineClamp, TextWrap = TextWrap,
            TextDecorationLine = TextDecorationLine, TextDecorationStyle = TextDecorationStyle,
            TextDecorationColor = TextDecorationColor, TextDecorationColorIsAuto = TextDecorationColorIsAuto,
            TextDecorationThickness = TextDecorationThickness,
            TextDecorationThicknessFromFont = TextDecorationThicknessFromFont,
            TextUnderlineOffset = TextUnderlineOffset, TextUnderlineOffsetIsAuto = TextUnderlineOffsetIsAuto,
            TextUnderlinePosition = TextUnderlinePosition, TextDecorationSkipInk = TextDecorationSkipInk,
            BoxDecorationBreak = BoxDecorationBreak,
            TextEmphasis = TextEmphasis, TextEmphasisColor = TextEmphasisColor, TextEmphasisStyle = TextEmphasisStyle,
            TextEmphasisPosition = TextEmphasisPosition,
            RowGap = RowGap, ColumnGap = ColumnGap,
            ColumnCount = ColumnCount, ColumnWidth = ColumnWidth, ColumnFill = ColumnFill,
            ColumnRuleWidth = ColumnRuleWidth, ColumnRuleStyle = ColumnRuleStyle,
            ColumnRuleColor = ColumnRuleColor,
            OutlineWidth = OutlineWidth, OutlineColor = OutlineColor, OutlineStyle = OutlineStyle, OutlineOffset = OutlineOffset,
            TableLayout = TableLayout, CaptionSide = CaptionSide, EmptyCells = EmptyCells, Content = Content,
            CounterIncrement = CounterIncrement, CounterReset = CounterReset, CounterSet = CounterSet, Quotes = Quotes,
            Order = Order, CurrentColorSlots = CurrentColorSlots, AspectRatio = AspectRatio, ObjectFit = ObjectFit,
            ObjectPositionX = ObjectPositionX, ObjectPositionY = ObjectPositionY,
            FlexFlow = FlexFlow, AlignContent = AlignContent, JustifyItems = JustifyItems, JustifySelf = JustifySelf,
            PlaceContent = PlaceContent, PlaceItems = PlaceItems, PlaceSelf = PlaceSelf,
            GridTemplateColumns = GridTemplateColumns, GridTemplateRows = GridTemplateRows, GridTemplateAreas = GridTemplateAreas,
            GridAutoColumns = GridAutoColumns, GridAutoRows = GridAutoRows, GridAutoFlow = GridAutoFlow,
            GridColumnStart = GridColumnStart, GridColumnEnd = GridColumnEnd,
            GridRowStart = GridRowStart, GridRowEnd = GridRowEnd,
            GridColumn = GridColumn, GridRow = GridRow, GridArea = GridArea, Grid = Grid,
            BackgroundClip = BackgroundClip, BackgroundOrigin = BackgroundOrigin, BackgroundBlendMode = BackgroundBlendMode,
            WritingMode = WritingMode, Hyphens = Hyphens, HyphenateCharacter = HyphenateCharacter, TabSize = TabSize, TabSizePx = TabSizePx, ColumnSpanAll = ColumnSpanAll,
            ScrollBehavior = ScrollBehavior, OverscrollBehavior = OverscrollBehavior,
            OverscrollBehaviorX = OverscrollBehaviorX, OverscrollBehaviorY = OverscrollBehaviorY,
            OverflowAnchor = OverflowAnchor, Contain = Contain, ContentVisibility = ContentVisibility,
            ContainIntrinsicWidth = ContainIntrinsicWidth, ContainIntrinsicHeight = ContainIntrinsicHeight,
            ContainIntrinsicWidthIsAuto = ContainIntrinsicWidthIsAuto,
            ContainIntrinsicHeightIsAuto = ContainIntrinsicHeightIsAuto,
            WillChange = WillChange, Appearance = Appearance, FieldSizing = FieldSizing, AccentColor = AccentColor, CaretColor = CaretColor,
            ColorScheme = ColorScheme, ForcedColorAdjust = ForcedColorAdjust,
            ImageRendering = ImageRendering, Isolation = Isolation, MixBlendMode = MixBlendMode,
            Filter = Filter, BackdropFilter = BackdropFilter, ClipPath = ClipPath,
            Mask = Mask, MaskImage = MaskImage, MaskClip = MaskClip, MaskComposite = MaskComposite,
            MaskMode = MaskMode, MaskOrigin = MaskOrigin, MaskPosition = MaskPosition,
            MaskRepeat = MaskRepeat, MaskSize = MaskSize,
            LineBreak = LineBreak, TextJustify = TextJustify, Resize = Resize,
            HangingPunctuation = HangingPunctuation, RubyAlign = RubyAlign, RubyPosition = RubyPosition,
            BorderSpacing = BorderSpacing, BorderRowSpacing = BorderRowSpacing,
            Zoom = Zoom, Orphans = Orphans, Widows = Widows,
            BorderImageSource = BorderImageSource, BorderImageSlice = BorderImageSlice,
            BorderImageWidth = BorderImageWidth, BorderImageRepeat = BorderImageRepeat, BorderImageOutset = BorderImageOutset,
            FontVariant = FontVariant, FontVariantCaps = FontVariantCaps,
            FontKerning = FontKerning, FontStretch = FontStretch,
            FontSynthesis = FontSynthesis, FontOpticalSizing = FontOpticalSizing,
            FontVariationSettings = FontVariationSettings, FontFeatureSettings = FontFeatureSettings,
            FontSizeAdjust = FontSizeAdjust, TextRendering = TextRendering, UnicodeBidi = UnicodeBidi
        };
    }

    public bool HasAnyTransform =>
        IsTransformPropertySet(Translate) || IsTransformPropertySet(Rotate) ||
        IsTransformPropertySet(Scale) || IsTransformPropertySet(Transform);

    /// <summary>
    /// Compose the independent transform properties into one transform-function
    /// list. The applied order is translate, rotate, scale, transform, matching
    /// the used-matrix order of CSS Transforms 2 §3. Percentages in 'translate'
    /// resolve against the element's border box.
    /// </summary>
    public string? EffectiveTransform(float borderBoxWidth = 0, float borderBoxHeight = 0)
    {
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        var parts = new List<string>();

        if (IsTransformPropertySet(Translate))
        {
            var tokens = Translate!.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            float x = ResolveTranslateComponent(tokens.Length > 0 ? tokens[0] : null, borderBoxWidth);
            float y = ResolveTranslateComponent(tokens.Length > 1 ? tokens[1] : null, borderBoxHeight);
            float z = ResolveTranslateComponent(tokens.Length > 2 ? tokens[2] : null, borderBoxHeight);
            string xs = x.ToString(ci), ys = y.ToString(ci), zs = z.ToString(ci);
            parts.Add(z != 0 ? $"translate3d({xs}px,{ys}px,{zs}px)" : $"translate({xs}px,{ys}px)");
        }

        if (IsTransformPropertySet(Rotate))
        {
            // rotate: <angle> | [ x | y | z | <number>{3} ] <angle>
            var tokens = Rotate!.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            string angle = tokens[^1];
            if (tokens.Length == 2)
            {
                parts.Add(tokens[0].ToLowerInvariant() switch
                {
                    "x" => $"rotateX({angle})",
                    "y" => $"rotateY({angle})",
                    _ => $"rotate({angle})",
                });
            }
            else if (tokens.Length == 4)
            {
                parts.Add($"rotate3d({tokens[0]},{tokens[1]},{tokens[2]},{angle})");
            }
            else
            {
                parts.Add($"rotate({angle})");
            }
        }

        if (IsTransformPropertySet(Scale))
        {
            var tokens = Scale!.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            float sx = ResolveScaleComponent(tokens.Length > 0 ? tokens[0] : null);
            float sy = tokens.Length > 1 ? ResolveScaleComponent(tokens[1]) : sx;
            parts.Add($"scale({sx.ToString(ci)},{sy.ToString(ci)})");
        }

        if (IsTransformPropertySet(Transform))
            parts.Add(Acrux.Core.Css.TransformParser.ResolveTranslatePercentages(Transform!.Trim(), borderBoxWidth, borderBoxHeight));

        return parts.Count > 0 ? string.Join(" ", parts) : null;
    }

    private static bool IsTransformPropertySet(string? value) =>
        !string.IsNullOrWhiteSpace(value) && !value.Trim().Equals("none", StringComparison.OrdinalIgnoreCase);

    private static float ResolveTranslateComponent(string? token, float reference)
    {
        if (string.IsNullOrEmpty(token))
            return 0;
        if (token.EndsWith('%') &&
            float.TryParse(token[..^1], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var pct))
            return pct / 100f * reference;
        var px = Length.Parse(token).ToPixels(16f, 16f, 0f, 0f);
        return float.IsNaN(px) ? 0 : px;
    }

    private static float ResolveScaleComponent(string? token)
    {
        if (string.IsNullOrEmpty(token))
            return 1;
        if (token.EndsWith('%') &&
            float.TryParse(token[..^1], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var pct))
            return pct / 100f;
        return float.TryParse(token, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 1;
    }

    public static ComputedStyle CreateDefault() => new();
}

public enum DisplayType { Block, Inline, InlineBlock, Flex, InlineFlex, Grid, InlineGrid, ListItem, Table, TableRow, TableRowGroup, TableHeaderGroup, TableFooterGroup, TableCell, TableCaption, TableColumnGroup, TableColumn, Ruby, Contents, None }
public enum PositionType { Static, Relative, Absolute, Fixed, Sticky }
public enum FloatType { None, Left, Right }
public enum ClearType { None, Left, Right, Both }
public enum BorderStyle { None, Solid, Dashed, Dotted, Double, Groove, Ridge, Inset, Outset }
public enum FontWeight
{
    Thin = 100, ExtraLight = 200, Light = 300, Normal = 400, Medium = 500,
    SemiBold = 600, Bold = 700, ExtraBold = 800, Black = 900,
}
public enum FontStyleType { Normal, Italic, Oblique }
public enum TextAlignType { Start, End, Left, Right, Center, Justify }

public enum TextAlignLastType { Auto, Start, End, Left, Right, Center, Justify }
public enum TextDecorationType { None, Underline, Overline, LineThrough }
public enum VerticalAlignType { Baseline, Top, Middle, Bottom, Sub, Super, TextTop, TextBottom, Inherit, Percentage, Length }
public enum WhiteSpaceMode { Normal, Nowrap, Pre, PreWrap, PreLine, BreakSpaces }

/// <summary>'white-space-collapse' (CSS Text 4 §3.2). 'Discard' only comes from the
/// longhand — no 'white-space' shorthand keyword produces it. 'BreakSpaces' is not in
/// the CSS Text 4 grammar, but the reference engine exposes it here anyway, because
/// 'pre-wrap' and 'break-spaces' otherwise share one identical triple and the
/// shorthand could not round-trip through the longhands.</summary>
public enum WhiteSpaceCollapseType { Collapse, Preserve, PreserveBreaks, Discard, BreakSpaces }

/// <summary>'text-wrap-mode' (CSS Text 4 §4.1), the wrapping half of 'white-space'.</summary>
public enum TextWrapModeType { Wrap, NoWrap }

/// <summary>'text-wrap-style' (CSS Text 4 §4.3). 'Balanced' is the standard keyword;
/// 'Pretty' is the browser-specific one that 'text-wrap: pretty' maps to.</summary>
public enum TextWrapStyleType { Auto, Stable, Balanced, Pretty }
public enum WordBreakMode { Normal, BreakAll, BreakWord, KeepAll }
public enum OverflowWrapMode { Normal, BreakWord, Anywhere }
/// <summary>CSS Overflow 3 §3.3. 'Clip' is NOT a scroll container and, unlike every
/// other non-visible value, it does not establish a block formatting context
/// (CSS Overflow 3 §4.1) — the two differences from 'Hidden' that a naive alias loses.</summary>
public enum OverflowType { Visible, Clip, Hidden, Scroll, Auto }

/// <summary>'overflow-clip-margin' keyword forms (CSS Overflow 3 §4.1).</summary>
public enum OverflowClipMarginBox { ContentBox, PaddingBox, BorderBox }
public enum VisibilityType { Visible, Hidden, Collapse }
public enum FlexDirectionType { Row, RowReverse, Column, ColumnReverse }
public enum FlexWrapType { NoWrap, Wrap, WrapReverse }
public enum JustifyContentType { FlexStart, FlexEnd, Center, SpaceBetween, SpaceAround, SpaceEvenly }
public enum AlignItemsType { Stretch, FlexStart, FlexEnd, Center, Baseline }
public enum AlignSelfType { Auto, Stretch, FlexStart, FlexEnd, Center, Baseline }
public enum BackgroundRepeat { Repeat, RepeatX, RepeatY, NoRepeat, Round, Space }

/// <summary>One entry of a comma-separated 'background-position' list.</summary>
public sealed class BackgroundPositionLayer
{
    public Length? X;
    public Length? Y;
}

/// <summary>One entry of a comma-separated 'background-size' list.</summary>
public sealed class BackgroundSizeLayer
{
    public BackgroundSizeType Type = BackgroundSizeType.Auto;
    public Length? Width;
    public Length? Height;
}

/// <summary>One entry of a comma-separated 'background-repeat' list. The longhand
/// takes one or two keywords: one applies to both axes, two are x-then-y.</summary>
public sealed class BackgroundRepeatPair
{
    public BackgroundRepeat X = BackgroundRepeat.Repeat;
    public BackgroundRepeat Y = BackgroundRepeat.Repeat;
}
public enum BackgroundAttachment { Scroll, Fixed, Local }
public enum BoxSizingType { ContentBox, BorderBox }
/// <summary>'list-style-type' values (CSS Lists 3 §5 plus the counter styles of
/// §A.2). The symbolic ones paint a fixed glyph; the rest are counters.</summary>
public enum ListStyleType
{
    Disc, Circle, Square, Decimal, DecimalLeadingZero, LowerRoman, UpperRoman,
    LowerAlpha, UpperAlpha,
    LowerLatin, UpperLatin, LowerGreek, UpperGreek, Armenian, UpperArmenian, LowerArmenian,
    Georgian, Hebrew, EthiopicNumeric, DisclosureOpen, DisclosureClosed,
    Hiragana, Katakana, HiraganaIroha, KatakanaIroha, CjkDecimal, CjkIdeographic,
    CjkEarthlyBranch, CjkHeavenlyStem, SimpChineseInformal, SimpChineseFormal,
    TradChineseInformal, TradChineseFormal, JapaneseInformal, JapaneseFormal,
    KoreanHangulFormal, KoreanHanjaInformal, KoreanHanjaFormal,
    Thai, Lao, Khmer, Myanmar, Mongolian, Gujarati, Gurmukhi, Kannada, Malayalam,
    Oriya, Tibetan,
    ArabicIndic, Persian, Devanagari, Bengali, Tamil, Telugu, CanadianAboriginal,
    Symbol,
    /// <summary>A quoted string marker, e.g. list-style-type: "--&gt;".</summary>
    String,
    None,
    /// <summary>A &lt;custom-ident&gt; naming an @counter-style rule; the name is kept in
    /// ListStyleTypeName and resolved against the document's registry.</summary>
    Custom,
}
public enum ListStylePosition { Inside, Outside }

public static class LengthExtensions
{
    public static string ToCssString(this Length? length)
    {
        if (length == null) return "0px";
        try
        {
            return length.ToString();
        }
        catch
        {
            return "0px";
        }
    }
}

public enum BackgroundSizeType { Auto, Cover, Contain, Length }
public enum ObjectFitType { Fill, Contain, Cover, None, ScaleDown }
public enum OverflowAnchorType { Auto, None }

/// <summary>
/// CSS Containment 3 §2. 'contain' takes a keyword *list*, so the computed value is a
/// flag set and not one of six states: 'size layout' and 'layout paint style' are both
/// legal and behave as the union of their keywords. 'content' and 'strict' are the two
/// shorthands that name a whole set. Measured against the reference engine, the sets split
/// exactly on these lines: layout/paint give a formatting context, a stacking context and
/// a clip, while size/style give none of them.
/// </summary>
[Flags]
public enum ContainType
{
    None = 0,
    Size = 1 << 0,
    Layout = 1 << 1,
    Style = 1 << 2,
    Paint = 1 << 3,
    Content = Layout | Style | Paint,
    Strict = Size | Layout | Style | Paint,
}

public enum ContentVisibilityType { Visible, Auto, Hidden }

/// <summary>
/// CSS Fonts 4 §6.1: which faces the engine may invent when the family does not have one.
/// 'auto' is the initial value and means all three; 'none' is the empty set, so the flag
/// type is a plain union with no bit for 'auto' itself. 'bold' and 'italic'/'oblique' are
/// the compatibility spellings of 'weight' and 'style' in the same grammar.
/// </summary>
[Flags]
public enum FontSynthesisType
{
    None = 0,
    Weight = 1 << 0,
    Style = 1 << 1,
    SmallCaps = 1 << 2,
    Auto = Weight | Style | SmallCaps,
}
public enum ScrollBehaviorType { Auto, Smooth }
public enum OverscrollBehaviorType { Auto, Contain, None }
public enum ImageRenderingType { Auto, CrispEdges, Pixelated }
public enum IsolationType { Auto, Isolate }
public enum MixBlendModeType { Normal, Multiply, Screen, Overlay, Darken, Lighten, ColorDodge, ColorBurn, HardLight, SoftLight, Difference, Exclusion, Hue, Saturation, Color, Luminosity }
public enum LineBreakType { Auto, Loose, Normal, Strict, Anywhere }
public enum TextJustifyType { Auto, InterWord, InterCharacter, None }
public enum HyphensType { None, Manual, Auto }
public enum WritingModeType { HorizontalTb, VerticalRl, VerticalLr }
public enum ResizeType { None, Both, Horizontal, Vertical }

/// <summary>'field-sizing' (CSS Form Control sizing): 'normal' keeps the widget's rendered
/// size, 'content' sizes the control from its own content.</summary>
public enum FieldSizingType { Normal, Content }
public enum ForcedColorAdjustType { Auto, None }
public enum TextOverflowType { Clip, Ellipsis }
public enum ColorSchemeType { Normal, Light, Dark, Only }
public enum BackgroundClipType { BorderBox, PaddingBox, ContentBox, Text }
public enum BackgroundOriginType { PaddingBox, BorderBox, ContentBox }
public enum BackgroundBlendModeType { Normal, Multiply, Screen, Overlay, Darken, Lighten, ColorDodge, ColorBurn, HardLight, SoftLight, Difference, Exclusion, Hue, Saturation, Color, Luminosity }
/// <summary>'text-decoration-line' (CSS Text Decoration 4 §2.1). The property accepts a
/// space-separated list, so the values are flags: 'underline overline' carries both bits.</summary>
[Flags]
public enum TextDecorationLineType { None = 0, Underline = 1 << 0, Overline = 1 << 1, LineThrough = 1 << 2 }

public static class TextDecorationLineExtensions
{
    public static bool HasUnderline(this TextDecorationLineType line) => (line & TextDecorationLineType.Underline) != 0;
    public static bool HasOverline(this TextDecorationLineType line) => (line & TextDecorationLineType.Overline) != 0;
    public static bool HasLineThrough(this TextDecorationLineType line) => (line & TextDecorationLineType.LineThrough) != 0;
}

public enum TextDecorationStyleType { Solid, Double, Dotted, Dashed, Wavy }

/// <summary>'text-underline-position' (CSS Text Decoration 4 §3.2). 'auto' resolves to
/// 'alphabetic' in horizontal writing modes; 'under' puts the line below the
/// descender instead of at the font's underline position.</summary>
public enum TextUnderlinePositionType { Auto, Alphabetic, Under, Left, Right, FromFont }

/// <summary>'box-decoration-break' values (CSS Fragmentation 3 §4.2).</summary>
public enum BoxDecorationBreakType { Slice, Clone }

/// <summary>
/// A single applied text decoration, as derived from a style's text-decoration
/// properties. Mirrors AppliedTextDecoration in core/style/applied_text_decoration.h.
/// </summary>
public sealed class AppliedTextDecoration
{
    public TextDecorationLineType Line { get; }
    public TextDecorationStyleType Style { get; set; }
    public SKColor Color { get; set; }
    /// <summary>Resolved thickness in px; NaN means 'auto'.</summary>
    public float Thickness { get; }
    public bool ThicknessFromFont { get; }
    public float UnderlineOffset { get; }
    public bool UnderlineOffsetIsAuto { get; }
    public TextUnderlinePositionType UnderlinePosition { get; }
    public bool SkipInk { get; }
    /// <summary>Font size of the box that originated the decoration: a propagated
    /// line keeps the originating box's metrics (CSS Text Decoration 4 §5.1).</summary>
    public float OriginFontSize { get; }

    public AppliedTextDecoration(TextDecorationLineType line, TextDecorationStyleType style, SKColor color,
        float thickness, bool thicknessFromFont, float underlineOffset, bool underlineOffsetIsAuto,
        TextUnderlinePositionType underlinePosition, bool skipInk, float originFontSize)
    {
        Line = line;
        Style = style;
        Color = color;
        Thickness = thickness;
        ThicknessFromFont = thicknessFromFont;
        UnderlineOffset = underlineOffset;
        UnderlineOffsetIsAuto = underlineOffsetIsAuto;
        UnderlinePosition = underlinePosition;
        SkipInk = skipInk;
        OriginFontSize = originFontSize;
    }

    public bool HasUnderline => Line.HasUnderline();
    public bool HasOverline => Line.HasOverline();
    public bool HasLineThrough => Line.HasLineThrough();

    public override string ToString() => $"{Line} ({Style}) R={Color.Red} G={Color.Green} B={Color.Blue}";
}
public enum GridAutoFlowType { Row, Column, Dense, ColumnDense }
public enum ZoomType { Normal, Reset }

public record BoxShadowValue(SKColor Color, float OffsetX, float OffsetY, float BlurRadius, float Spread, bool Inset = false);
public record TextShadowValue(SKColor Color, float OffsetX, float OffsetY, float BlurRadius);

public enum ReflectionDirectionType
{
    ReflectionAbove,
    ReflectionBelow,
    ReflectionLeft,
    ReflectionRight,
}

/// <summary>
/// The computed -webkit-box-reflect value. Mirrors StyleReflection in
/// core/style/style_reflection.h: a direction, an offset and an optional
/// mask nine-piece image.
/// </summary>
public sealed class StyleReflection
{
    public ReflectionDirectionType Direction { get; set; } = ReflectionDirectionType.ReflectionBelow;
    public Length? Offset { get; set; }
    public bool HasMask { get; set; }
}

public class BoxDimensions
{
    public ComputedStyle? Style { get; set; }
    public Element? Element { get; set; }

    public float MarginTop { get; set; }
    public float MarginRight { get; set; }
    public float MarginBottom { get; set; }
    public float MarginLeft { get; set; }

    public float BorderTopWidth { get; set; }
    public float BorderRightWidth { get; set; }
    public float BorderBottomWidth { get; set; }
    public float BorderLeftWidth { get; set; }

    public float PaddingTop { get; set; }
    public float PaddingRight { get; set; }
    public float PaddingBottom { get; set; }
    public float PaddingLeft { get; set; }

    public static BoxDimensions FromStyle(ComputedStyle style)
    {
        float fontSize = style.FontSize > 0 ? style.FontSize : 16f;
        return new BoxDimensions
        {
            MarginTop = GetPixelFromLength(style.MarginTop, fontSize, 16f, 0, 0),
            MarginRight = GetPixelFromLength(style.MarginRight, fontSize, 16f, 0, 0),
            MarginBottom = GetPixelFromLength(style.MarginBottom, fontSize, 16f, 0, 0),
            MarginLeft = GetPixelFromLength(style.MarginLeft, fontSize, 16f, 0, 0),
            BorderTopWidth = style.BorderTopWidth,
            BorderRightWidth = style.BorderRightWidth,
            BorderBottomWidth = style.BorderBottomWidth,
            BorderLeftWidth = style.BorderLeftWidth,
            PaddingTop = GetPixelFromLength(style.PaddingTop, fontSize, 16f, 0, 0),
            PaddingRight = GetPixelFromLength(style.PaddingRight, fontSize, 16f, 0, 0),
            PaddingBottom = GetPixelFromLength(style.PaddingBottom, fontSize, 16f, 0, 0),
            PaddingLeft = GetPixelFromLength(style.PaddingLeft, fontSize, 16f, 0, 0)
        };
    }

    private static float GetPixelFromLength(Length length, float reference, float rootFontSize, float viewportWidth, float viewportHeight)
    {
        if (length == null) return 0;
        try
        {
            var px = length.ToPixels(reference, rootFontSize, viewportWidth, viewportHeight);
            if (!float.IsNaN(px)) return px;
        }
        catch
        {
            // ignore and fallback
        }
        return Length.ToPixelsOrDefault(length, reference > 0 ? reference : rootFontSize);
    }

    public float TotalWidth => MarginLeft + BorderLeftWidth + PaddingLeft + MarginRight + BorderRightWidth + PaddingRight;
    public float TotalHeight => MarginTop + BorderTopWidth + PaddingTop + MarginBottom + BorderBottomWidth + PaddingBottom;
}

public class LayoutBox
{
    public SKRect MarginBox { get; set; }
    public SKRect BorderBox { get; set; }
    public SKRect PaddingBox { get; set; }
    public SKRect ContentBox { get; set; }

    public float LineHeight { get; set; } = 16;
    public List<LayoutBox> Children { get; } = new();
    public LayoutBox? Parent { get; set; }
    public int? ZIndex { get; set; }
    public BoxDimensions? Dimensions { get; set; }

    public float Width => ContentBox.Width;
    public float Height => ContentBox.Height;

    public LayoutBox? ContainingBlock { get; set; }

    /// <summary>CSS Content Distribution 1 §4: 'content-visibility: hidden' keeps the box's
    /// own background and border but its contents — its text and every descendant — are not
    /// rendered. Layout is unaffected, which is why the flag lives on the box and not on the
    /// style.</summary>
    public bool ContentsNotRendered { get; set; }

    /// <summary>True for the whole subtree below such a box: nothing in it paints, not even
    /// its own decoration.</summary>
    public bool InHiddenSubtree { get; set; }

    public LayoutBox? NextSibling
    {
        get
        {
            if (Parent == null) return null;
            var siblings = Parent.Children;
            int idx = siblings.IndexOf(this);
            return idx >= 0 && idx + 1 < siblings.Count ? siblings[idx + 1] : null;
        }
    }

    public List<LineBox>? Lines { get; set; }
    public List<InlineRun>? LineRuns { get; set; }
    public bool IsFloating { get; set; }
    public FloatType Float { get; set; }

    public bool IsScrollContainer { get; set; }
    public float ScrollContentWidth { get; set; }
    public float ScrollContentHeight { get; set; }
    public float ScrollX { get; set; }
    public float ScrollY { get; set; }
    public float TargetScrollX { get; set; } = float.NaN;
    public float TargetScrollY { get; set; } = float.NaN;
    public bool IsSmoothScrollingX { get; set; }
    public bool IsSmoothScrollingY { get; set; }
    public float ScrollVelX { get; set; }
    public float ScrollVelY { get; set; }
    public float SnapTargetX { get; set; }
    public float SnapTargetY { get; set; }
    public bool IsBouncingX { get; set; }
    public bool IsBouncingY { get; set; }

    public bool IsSticky { get; set; }
    public float StickyTop { get; set; }
    public float StickyLeft { get; set; }
    public float StickyOffsetX { get; set; }
    public float StickyOffsetY { get; set; }

    public bool IsMultiColumn { get; set; }
    public int ColumnCount { get; set; }
    public float ColumnWidth { get; set; }
    public float ColumnGapSize { get; set; }
    public List<LayoutBox>? Columns { get; set; }

    public SKRect AlignToDevice(SKCanvas canvas)
    {
        try
        {
            var m = canvas.TotalMatrix;
            float sx = MathF.Abs(m.ScaleX);
            float sy = MathF.Abs(m.ScaleY);
            if (sx <= 0) sx = 1f;
            if (sy <= 0) sy = 1f;

            float left = MathF.Round(MarginBox.Left * sx) / sx;
            float top = MathF.Round(MarginBox.Top * sy) / sy;
            float right = MathF.Round(MarginBox.Right * sx) / sx;
            float bottom = MathF.Round(MarginBox.Bottom * sy) / sy;
            return new SKRect(left, top, right, bottom);
        }
        catch
        {
            return MarginBox;
        }
    }
}

public class InlineRun
{
    public string Text { get; set; } = string.Empty;
    public float X { get; set; }
    public float Width { get; set; }
    public float Height { get; set; }
    public float Baseline { get; set; }
    public Node? Node { get; set; }
    public bool IsText { get; set; }
    public SKColor? Color { get; set; }
    public float? FontSize { get; set; }
    public string? FontFamily { get; set; }
    public FontWeight FontWeight { get; set; } = FontWeight.Normal;
}

public class LineBox
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Width { get; set; }
    public float Height { get; set; }
    public float Baseline { get; set; }
    public float TextAlignOffsetX { get; set; }
    public List<InlineRun> Runs { get; } = new();
}

public class PaintContext
{
    public float CurrentX { get; set; }
    public float CurrentY { get; set; }
    public float AvailableWidth { get; set; }
    public List<LineBox> Lines { get; } = new();
    public LineBox CurrentLine { get; set; } = new();
    public float MaxLineHeight { get; set; } = 16;
    public bool NeedsNewLine { get; set; }
}

// ===== 新增 CSS 枚举格式化器（修复问题5） =====
public static class CssEnumFormatter
{
    /// <summary>'display' as a CSS keyword. Derived rather than listed: the hand-written
    /// table had every multi-word member wrong ('TableCell' came out as "tablecell"), and the
    /// same drift keeps happening wherever an enum name is lowercased by hand.</summary>
    public static string ToCssString(this DisplayType display) =>
        CssKeywordFromEnum(display.ToString());

    /// <summary>CamelCase enum member → kebab-case CSS keyword: TableCell → "table-cell",
    /// RowReverse → "row-reverse", InlineBlock → "inline-block", Contents → "contents".
    /// 'nowrap' is the one keyword in this set that is not a hyphenation of its member name
    /// (measured: the reference engine reports 'nowrap', never 'no-wrap').</summary>
    public static string CssKeywordFromEnum(string member)
    {
        if (member == "NoWrap") return "nowrap";
        return System.Text.RegularExpressions.Regex.Replace(member, "([a-z0-9])([A-Z])", "$1-$2").ToLowerInvariant();
    }

    public static string ToCssString(this BoxSizingType boxSizing) => boxSizing switch
    {
        BoxSizingType.ContentBox => "content-box",
        BoxSizingType.BorderBox => "border-box",
        _ => boxSizing.ToString().ToLowerInvariant()
    };

    public static string ToCssString(this PositionType position) => position switch
    {
        _ => position.ToString().ToLowerInvariant()
    };

    public static string ToCssString(this BorderStyle style) => style switch
    {
        _ => style.ToString().ToLowerInvariant()
    };
}