using System.Globalization;

namespace UpBrowser.Core.Dom.Animations;

/// <summary>
/// Where a <c>steps()</c> easing changes value (CSS Easing Functions 1 §2.3).
/// </summary>
public enum StepPosition
{
    /// <summary>jump-start: the output rises to 1/n at the very start of each step.</summary>
    JumpStart,
    /// <summary>jump-end (the default): the output rises at the end of each step.</summary>
    JumpEnd,
    /// <summary>jump-none: the rise lands in the middle of each step. Requires n >= 2.</summary>
    JumpNone,
    /// <summary>jump-both: extra output values at both ends of the interval.</summary>
    JumpBoth,
}

/// <summary>
/// An easing function mapping an iteration progress in [0,1] to an eased
/// progress. CSS Easing Functions 1 defines linear, the cubic-bezier() family
/// and steps(); CSS Easing 2 adds linear().
///
/// Every implementation maps 0 to 0 and 1 to 1 (Easing 1 §2: "the output is 0
/// for an input of 0 and 1 for an input of 1") so an animation always starts
/// and ends exactly on its keyframe values.
/// </summary>
public abstract class AnimationTimingFunction
{
    /// <summary>Human-readable serialization, matching the authored syntax.</summary>
    public abstract string CssText { get; }

    public abstract double Apply(double progress);

    /// <summary>
    /// Value equality. Timing identity drives animation replacement, and a
    /// re-parsed <c>ease</c> must compare equal to a previously parsed one or
    /// every frame would replace every animation.
    /// </summary>
    public override bool Equals(object? obj) =>
        obj is AnimationTimingFunction other && GetType() == other.GetType() && CssText == other.CssText;

    public override int GetHashCode() => HashCode.Combine(GetType(), CssText);

    public static readonly AnimationTimingFunction Linear = new LinearTimingFunction();
    public static readonly AnimationTimingFunction Ease = new CubicBezierTimingFunction(0.25, 0.1, 0.25, 1.0);
    public static readonly AnimationTimingFunction EaseIn = new CubicBezierTimingFunction(0.42, 0, 1.0, 1.0);
    public static readonly AnimationTimingFunction EaseOut = new CubicBezierTimingFunction(0, 0, 0.58, 1.0);
    public static readonly AnimationTimingFunction EaseInOut = new CubicBezierTimingFunction(0.42, 0, 0.58, 1.0);
    public static readonly AnimationTimingFunction StepStart = new StepsTimingFunction(1, StepPosition.JumpStart);
    public static readonly AnimationTimingFunction StepEnd = new StepsTimingFunction(1, StepPosition.JumpEnd);

    /// <summary>
    /// Parse a single easing function. Unrecognized input falls back to
    /// <c>ease</c> only when it is empty; genuinely invalid syntax falls back to
    /// <c>linear</c>, which is what a dropped declaration computes to.
    /// </summary>
    public static AnimationTimingFunction Parse(string? easing)
    {
        if (string.IsNullOrWhiteSpace(easing)) return Ease;
        var text = easing.Trim();
        var lower = text.ToLowerInvariant();

        switch (lower)
        {
            case "linear": return Linear;
            case "ease": return Ease;
            case "ease-in": return EaseIn;
            case "ease-out": return EaseOut;
            case "ease-in-out": return EaseInOut;
            case "step-start": return StepStart;
            case "step-end": return StepEnd;
        }

        if (CssValueTokenizer.TryReadFunction(lower, out var name, out var argsText))
        {
            switch (name)
            {
                case "cubic-bezier":
                {
                    var args = CssValueTokenizer.SplitArguments(argsText);
                    if (args.Count == 4 &&
                        TryNumber(args[0], out var x1) && TryNumber(args[1], out var y1) &&
                        TryNumber(args[2], out var x2) && TryNumber(args[3], out var y2))
                    {
                        // Easing 1 §3.2: x1/x2 are clamped to [0,1] so the curve
                        // stays a function of the input; y1/y2 are free.
                        x1 = Math.Clamp(x1, 0, 1);
                        x2 = Math.Clamp(x2, 0, 1);
                        return new CubicBezierTimingFunction(x1, y1, x2, y2);
                    }
                    return Linear;
                }
                case "steps":
                {
                    var args = CssValueTokenizer.SplitArguments(argsText);
                    if (args.Count == 0) return Linear;
                    if (!TryNumber(args[0], out var stepCount) || stepCount < 1)
                        return Linear;
                    int count = (int)Math.Round(stepCount);

                    StepPosition position = StepPosition.JumpEnd;
                    if (args.Count >= 2)
                    {
                        var p = args[1].Trim();
                        position = p switch
                        {
                            "jump-start" or "start" => StepPosition.JumpStart,
                            "jump-end" or "end" => StepPosition.JumpEnd,
                            "jump-none" => StepPosition.JumpNone,
                            "jump-both" => StepPosition.JumpBoth,
                            _ => StepPosition.JumpEnd,
                        };
                    }
                    // jump-none is only defined for two or more steps.
                    if (position == StepPosition.JumpNone && count < 2) position = StepPosition.JumpEnd;
                    return new StepsTimingFunction(count, position);
                }
                case "linear":
                {
                    var args = CssValueTokenizer.SplitArguments(argsText);
                    var stops = new List<double>();
                    foreach (var a in args)
                    {
                        if (TryNumber(a, out var v)) stops.Add(v);
                    }
                    if (stops.Count == 0) return Linear;
                    if (stops.Count == 1) return new LinearEasingFunction(stops.ToArray());
                    return new LinearEasingFunction(stops.ToArray());
                }
            }
        }

        return Linear;
    }

    /// <summary>
    /// Parse a comma-separated easing list as used by
    /// <c>animation-timing-function</c> and <c>transition-timing-function</c>.
    /// A list is repeated (cycled) to the requested length; percentages in the
    /// list distribute the cycling points (Easing 1 §2, Easing 2 §2.1).
    /// </summary>
    public static List<AnimationTimingFunction> ParseList(string? value, int count, int index)
    {
        var raw = SplitTopLevel(value ?? "");
        var parsed = new List<AnimationTimingFunction>(raw.Count);
        foreach (var item in raw) parsed.Add(Parse(item));
        if (parsed.Count == 0) return new List<AnimationTimingFunction> { Ease };
        if (parsed.Count == 1) return new List<AnimationTimingFunction> { parsed[0] };

        // Distribution points: `linear(0, 25%, 0.5, 75%, 1)` inside a list.
        var offsets = new double[parsed.Count];
        for (int i = 0; i < parsed.Count; i++) offsets[i] = i / (double)(parsed.Count - 1);
        if (value != null && value.Contains('%') && raw.Count == parsed.Count)
        {
            for (int i = 0; i < parsed.Count; i++)
            {
                if (raw[i].Trim().EndsWith('%') &&
                    TryNumber(raw[i].Trim().TrimEnd('%'), out var pct))
                {
                    offsets[i] = Math.Clamp(pct / 100.0, 0, 1);
                }
            }
        }

        // Select the entry for `index` by walking the (normalized) offsets.
        int position = count > 0 ? index % count : 0;
        double t = count > 1 ? position / (double)count : 0;
        int chosen = 0;
        for (int i = 0; i < offsets.Length; i++)
        {
            if (t >= offsets[i]) chosen = i;
        }
        return new List<AnimationTimingFunction> { parsed[chosen] };
    }

    /// <summary>Split on top-level commas, returning trimmed, non-empty items.</summary>
    public static List<string> SplitTopLevel(string value) => CssValueTokenizer.SplitTopLevel(value);

    internal static bool TryNumber(string text, out double value) =>
        double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
}

public sealed class LinearTimingFunction : AnimationTimingFunction
{
    public override string CssText => "linear";

    public override double Apply(double progress)
    {
        if (progress <= 0) return 0;
        if (progress >= 1) return 1;
        return progress;
    }
}

/// <summary>
/// <c>cubic-bezier(x1, y1, x2, y2)</c> (Easing 1 §3). Inverting the parametric
/// cubic for x needs a numeric solve; Newton-Raphson converges in a handful of
/// steps for well-behaved control points and bisection is the guaranteed fallback.
/// </summary>
public sealed class CubicBezierTimingFunction : AnimationTimingFunction
{
    private const int NewtonIterations = 8;
    private const double NewtonEpsilon = 1e-6;
    private const int Subdivisions = 11;
    private const double SubdivisionEpsilon = 1e-7;

    public double P1x { get; }
    public double P1y { get; }
    public double P2x { get; }
    public double P2y { get; }

    public CubicBezierTimingFunction(double p1x, double p1y, double p2x, double p2y)
    {
        P1x = p1x; P1y = p1y; P2x = p2x; P2y = p2y;
    }

    public override string CssText =>
        $"cubic-bezier({CssNum(P1x)},{CssNum(P1y)},{CssNum(P2x)},{CssNum(P2y)})";

    public override double Apply(double progress)
    {
        if (progress <= 0) return 0;
        if (progress >= 1) return 1;
        // A degenerate curve (both control points on the diagonal) is the
        // identity; the solve below would divide by a zero derivative.
        if (P1x == P1y && P2x == P2y) return progress;
        return SampleCurveY(SolveCurveX(progress));
    }

    private static string CssNum(double v) => v.ToString("0.####", CultureInfo.InvariantCulture);

    private double SampleCurveX(double t) =>
        ((1 - t) * (1 - t) * (1 - t) * 0) +
        3 * (1 - t) * (1 - t) * t * P1x +
        3 * (1 - t) * t * t * P2x +
        t * t * t;

    private double SampleCurveY(double t) =>
        3 * (1 - t) * (1 - t) * t * P1y +
        3 * (1 - t) * t * t * P2y +
        t * t * t;

    private double SampleDerivativeX(double t) =>
        3 * (1 - t) * (1 - t) * P1x +
        6 * (1 - t) * t * (P2x - P1x) +
        3 * t * t * (1 - P2x);

    private double SolveCurveX(double x)
    {
        // Newton-Raphson first: quadratic convergence when the derivative is
        // well behaved.
        double t = x;
        for (int i = 0; i < NewtonIterations; i++)
        {
            double error = SampleCurveX(t) - x;
            if (Math.Abs(error) < NewtonEpsilon) return t;
            double d = SampleDerivativeX(t);
            if (Math.Abs(d) < 1e-6) break;
            t -= error / d;
        }

        // Bisection fallback over a fixed subdivision grid.
        double start = 0;
        double end = 1;
        t = x;
        for (int i = 0; i < Subdivisions * 2; i++)
        {
            double value = SampleCurveX(t);
            if (Math.Abs(value - x) < SubdivisionEpsilon) return t;
            if (x > value) start = t; else end = t;
            t = (end + start) / 2;
        }
        return t;
    }
}

/// <summary>
/// <c>steps(n, position)</c> (Easing 1 §2.3). The output is a step function with
/// <c>n</c> jumps; the position selects where inside the interval the jump lands.
/// </summary>
public sealed class StepsTimingFunction : AnimationTimingFunction
{
    public int Count { get; }
    public StepPosition Position { get; }

    public StepsTimingFunction(int count, StepPosition position)
    {
        Count = Math.Max(1, count);
        Position = position;
    }

    public override string CssText
    {
        get
        {
            string p = Position switch
            {
                StepPosition.JumpStart => "jump-start",
                StepPosition.JumpNone => "jump-none",
                StepPosition.JumpBoth => "jump-both",
                _ => "jump-end",
            };
            // step-start / step-end are the canonical spellings of the
            // one-step forms, so emit them rather than steps(1, ...).
            if (Count == 1 && Position == StepPosition.JumpStart) return "step-start";
            if (Count == 1 && Position == StepPosition.JumpEnd) return "step-end";
            return $"steps({Count},{p})";
        }
    }

    public override double Apply(double progress)
    {
        // Endpoints first: each position keyword defines its own value at 0, and
        // jump-both / jump-start differ from the interior formula there.
        if (progress <= 0)
        {
            return Position switch
            {
                StepPosition.JumpStart => 1.0 / Count,
                _ => 0,
            };
        }
        if (progress >= 1) return 1;

        int jumps = (int)Math.Floor(progress * Count);
        return Position switch
        {
            StepPosition.JumpStart => Math.Min(1.0, (jumps + 1) / (double)Count),
            StepPosition.JumpEnd => jumps / (double)Count,
            // jump-none keeps 0 and 1 as its endpoints and puts the remaining
            // Count-1 jumps in the middle of the intervals, so the output is
            // jumps / (Count - 1) (Easing 1 §2.3).
            StepPosition.JumpNone => jumps / (double)Math.Max(1, Count - 1),
            // jump-both adds a value at each end of the interval, giving Count+1
            // output values including 0 and 1.
            StepPosition.JumpBoth => (jumps + 1) / (double)(Count + 1),
            _ => jumps / (double)Count,
        };
    }
}

/// <summary>
/// <c>linear(stop, stop, ...)</c> (Easing 2 §3): piecewise-linear interpolation
/// through explicit output stops, evenly spaced unless a stop carries a
/// percentage hint that the caller already folded into its value.
/// </summary>
public sealed class LinearEasingFunction : AnimationTimingFunction
{
    private readonly double[] _stops;

    public LinearEasingFunction(double[] stops) => _stops = stops;

    public override string CssText =>
        "linear(" + string.Join(",", _stops.Select(s => s.ToString("0.####", CultureInfo.InvariantCulture))) + ")";

    public override double Apply(double progress)
    {
        if (progress <= 0) return _stops.Length > 0 ? _stops[0] : 0;
        if (progress >= 1) return _stops.Length > 0 ? _stops[^1] : 1;

        int segments = _stops.Length - 1;
        if (segments <= 0) return _stops[0];

        double scaled = progress * segments;
        int index = (int)Math.Floor(scaled);
        if (index >= segments) index = segments - 1;
        double local = scaled - index;
        double a = _stops[index];
        double b = _stops[index + 1];
        return a + (b - a) * local;
    }
}
