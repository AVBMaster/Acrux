using System.Globalization;

namespace Acrux.Core.Css.Values;

public enum CssUnitType
{
    Unknown, Number, Percentage,
    Ems, Exs, Pixels, Centimeters, Millimeters, Inches, Points, Picas, QuarterMillimeters,
    ViewportWidth, ViewportHeight, ViewportInline, ViewportBlock, ViewportMin, ViewportMax,
    SmallViewportWidth, SmallViewportHeight, SmallViewportInline, SmallViewportBlock, SmallViewportMin, SmallViewportMax,
    LargeViewportWidth, LargeViewportHeight, LargeViewportInline, LargeViewportBlock, LargeViewportMin, LargeViewportMax,
    DynamicViewportWidth, DynamicViewportHeight, DynamicViewportInline, DynamicViewportBlock, DynamicViewportMin, DynamicViewportMax,
    ContainerWidth, ContainerHeight, ContainerInline, ContainerBlock, ContainerMin, ContainerMax,
    Rems, Rexs, Rchs, Rics, Chs, Ics, Lhs, Rlhs, Caps, Rcaps, UserUnits,
    Degrees, Radians, Gradians, Turns,
    Milliseconds, Seconds,
    Hertz, Kilohertz,
    DotsPerPixel, X, DotsPerInch, DotsPerCentimeter,
    Flex, Integer, Ident, QuirkyEms
}

public enum ValueRange
{
    All, NonNegative, Integer, NonNegativeInteger, PositiveInteger
}

public enum UnitCategory
{
    Number, Percent, Length, Angle, Time, Frequency, Resolution, Other
}

public abstract class CssPrimitiveValue : CssValue, IEquatable<CssPrimitiveValue>
{
    protected CssPrimitiveValue(CssClassType classType) : base(classType) { }

    public abstract CssUnitType UnitType { get; }
    public abstract double DoubleValueWithoutClamping { get; }
    public abstract bool IsZero { get; }
    public abstract bool IsOne { get; }
    public abstract bool IsHundred { get; }
    public abstract bool IsNegative { get; }

    public double DoubleValue => ClampToCssLengthRange(DoubleValueWithoutClamping);
    public float FloatValue => (float)DoubleValue;
    public int IntValue => (int)DoubleValue;

    public virtual bool IsNumber => UnitType == CssUnitType.Number;
    public virtual bool IsInteger => UnitType == CssUnitType.Integer || UnitType == CssUnitType.Number;
    public virtual bool IsPercentage => UnitType == CssUnitType.Percentage;
    public virtual bool HasPercentage => IsPercentage;
    public virtual bool IsPx => UnitType == CssUnitType.Pixels;
    public virtual bool IsAngle => UnitCategoryTests.Test(UnitType) == UnitCategory.Angle;
    public virtual bool IsTime => UnitCategoryTests.Test(UnitType) == UnitCategory.Time;
    public virtual bool IsLength => UnitCategoryTests.Test(UnitType) == UnitCategory.Length;
    public virtual bool IsResolution => UnitCategoryTests.Test(UnitType) == UnitCategory.Resolution;
    public virtual bool IsFlex => UnitType == CssUnitType.Flex;

    public static double ClampToCssLengthRange(double value)
    {
        const double min = -1e9;
        const double max = 1e9;
        if (double.IsNaN(value) || double.IsInfinity(value)) return 0;
        return Math.Clamp(value, min, max);
    }

    public static UnitCategory CssUnitTypeToUnitCategory(CssUnitType unit) => UnitCategoryTests.Test(unit);

    public static bool IsAngleUnit(CssUnitType unit) => UnitCategoryTests.Test(unit) == UnitCategory.Angle;
    public static bool IsTimeUnit(CssUnitType unit) => UnitCategoryTests.Test(unit) == UnitCategory.Time;
    public static bool IsLengthUnit(CssUnitType unit) => UnitCategoryTests.Test(unit) == UnitCategory.Length;
    public static bool IsResolutionUnit(CssUnitType unit) => UnitCategoryTests.Test(unit) == UnitCategory.Resolution;
    public static bool IsViewportPercentageLength(CssUnitType unit) =>
        unit >= CssUnitType.ViewportWidth && unit <= CssUnitType.DynamicViewportMax;
    public static bool IsContainerPercentageLength(CssUnitType unit) =>
        unit >= CssUnitType.ContainerWidth && unit <= CssUnitType.ContainerMax;
    public static bool IsRelativeUnit(CssUnitType unit) =>
        IsViewportPercentageLength(unit) || IsContainerPercentageLength(unit) ||
        unit is CssUnitType.Ems or CssUnitType.Exs or CssUnitType.Rems or CssUnitType.Rexs or
        CssUnitType.Chs or CssUnitType.Rchs or CssUnitType.Ics or CssUnitType.Rics or
        CssUnitType.Lhs or CssUnitType.Rlhs or CssUnitType.Caps or CssUnitType.Rcaps;

    public static string UnitTypeToString(CssUnitType unit) => unit switch
    {
        CssUnitType.Number => "",
        CssUnitType.Percentage => "%",
        CssUnitType.Ems => "em",
        CssUnitType.Exs => "ex",
        CssUnitType.Pixels => "px",
        CssUnitType.Centimeters => "cm",
        CssUnitType.Millimeters => "mm",
        CssUnitType.Inches => "in",
        CssUnitType.Points => "pt",
        CssUnitType.Picas => "pc",
        CssUnitType.QuarterMillimeters => "q",
        CssUnitType.ViewportWidth => "vw",
        CssUnitType.ViewportHeight => "vh",
        CssUnitType.ViewportInline => "vi",
        CssUnitType.ViewportBlock => "vb",
        CssUnitType.ViewportMin => "vmin",
        CssUnitType.ViewportMax => "vmax",
        CssUnitType.SmallViewportWidth => "svw",
        CssUnitType.SmallViewportHeight => "svh",
        CssUnitType.SmallViewportInline => "svi",
        CssUnitType.SmallViewportBlock => "svb",
        CssUnitType.SmallViewportMin => "svmin",
        CssUnitType.SmallViewportMax => "svmax",
        CssUnitType.LargeViewportWidth => "lvw",
        CssUnitType.LargeViewportHeight => "lvh",
        CssUnitType.LargeViewportInline => "lvi",
        CssUnitType.LargeViewportBlock => "lvb",
        CssUnitType.LargeViewportMin => "lvmin",
        CssUnitType.LargeViewportMax => "lvmax",
        CssUnitType.DynamicViewportWidth => "dvw",
        CssUnitType.DynamicViewportHeight => "dvh",
        CssUnitType.DynamicViewportInline => "dvi",
        CssUnitType.DynamicViewportBlock => "dvb",
        CssUnitType.DynamicViewportMin => "dvmin",
        CssUnitType.DynamicViewportMax => "dvmax",
        CssUnitType.ContainerWidth => "cqw",
        CssUnitType.ContainerHeight => "cqh",
        CssUnitType.ContainerInline => "cqi",
        CssUnitType.ContainerBlock => "cqb",
        CssUnitType.ContainerMin => "cqmin",
        CssUnitType.ContainerMax => "cqmax",
        CssUnitType.Rems => "rem",
        CssUnitType.Rexs => "rex",
        CssUnitType.Rchs => "rch",
        CssUnitType.Rics => "ric",
        CssUnitType.Chs => "ch",
        CssUnitType.Ics => "ic",
        CssUnitType.Lhs => "lh",
        CssUnitType.Rlhs => "rlh",
        CssUnitType.Caps => "cap",
        CssUnitType.Rcaps => "rcap",
        CssUnitType.Degrees => "deg",
        CssUnitType.Radians => "rad",
        CssUnitType.Gradians => "grad",
        CssUnitType.Turns => "turn",
        CssUnitType.Milliseconds => "ms",
        CssUnitType.Seconds => "s",
        CssUnitType.Hertz => "hz",
        CssUnitType.Kilohertz => "khz",
        CssUnitType.DotsPerPixel => "dppx",
        CssUnitType.X => "x",
        CssUnitType.DotsPerInch => "dpi",
        CssUnitType.DotsPerCentimeter => "dpcm",
        CssUnitType.Flex => "fr",
        _ => ""
    };

    public static CssUnitType StringToUnitType(string value) => value.ToLowerInvariant() switch
    {
        "" => CssUnitType.Number,
        "%" => CssUnitType.Percentage,
        "em" => CssUnitType.Ems,
        "ex" => CssUnitType.Exs,
        "px" => CssUnitType.Pixels,
        "cm" => CssUnitType.Centimeters,
        "mm" => CssUnitType.Millimeters,
        "in" => CssUnitType.Inches,
        "pt" => CssUnitType.Points,
        "pc" => CssUnitType.Picas,
        "q" => CssUnitType.QuarterMillimeters,
        "vw" => CssUnitType.ViewportWidth,
        "vh" => CssUnitType.ViewportHeight,
        "vi" => CssUnitType.ViewportInline,
        "vb" => CssUnitType.ViewportBlock,
        "vmin" => CssUnitType.ViewportMin,
        "vmax" => CssUnitType.ViewportMax,
        "svw" => CssUnitType.SmallViewportWidth,
        "svh" => CssUnitType.SmallViewportHeight,
        "svi" => CssUnitType.SmallViewportInline,
        "svb" => CssUnitType.SmallViewportBlock,
        "svmin" => CssUnitType.SmallViewportMin,
        "svmax" => CssUnitType.SmallViewportMax,
        "lvw" => CssUnitType.LargeViewportWidth,
        "lvh" => CssUnitType.LargeViewportHeight,
        "lvi" => CssUnitType.LargeViewportInline,
        "lvb" => CssUnitType.LargeViewportBlock,
        "lvmin" => CssUnitType.LargeViewportMin,
        "lvmax" => CssUnitType.LargeViewportMax,
        "dvw" => CssUnitType.DynamicViewportWidth,
        "dvh" => CssUnitType.DynamicViewportHeight,
        "dvi" => CssUnitType.DynamicViewportInline,
        "dvb" => CssUnitType.DynamicViewportBlock,
        "dvmin" => CssUnitType.DynamicViewportMin,
        "dvmax" => CssUnitType.DynamicViewportMax,
        "cqw" => CssUnitType.ContainerWidth,
        "cqh" => CssUnitType.ContainerHeight,
        "cqi" => CssUnitType.ContainerInline,
        "cqb" => CssUnitType.ContainerBlock,
        "cqmin" => CssUnitType.ContainerMin,
        "cqmax" => CssUnitType.ContainerMax,
        "rem" => CssUnitType.Rems,
        "rex" => CssUnitType.Rexs,
        "rch" => CssUnitType.Rchs,
        "ric" => CssUnitType.Rics,
        "ch" => CssUnitType.Chs,
        "ic" => CssUnitType.Ics,
        "lh" => CssUnitType.Lhs,
        "rlh" => CssUnitType.Rlhs,
        "cap" => CssUnitType.Caps,
        "rcap" => CssUnitType.Rcaps,
        "deg" => CssUnitType.Degrees,
        "rad" => CssUnitType.Radians,
        "grad" => CssUnitType.Gradians,
        "turn" => CssUnitType.Turns,
        "ms" => CssUnitType.Milliseconds,
        "s" => CssUnitType.Seconds,
        "hz" => CssUnitType.Hertz,
        "khz" => CssUnitType.Kilohertz,
        "dppx" => CssUnitType.DotsPerPixel,
        "x" => CssUnitType.X,
        "dpi" => CssUnitType.DotsPerInch,
        "dpcm" => CssUnitType.DotsPerCentimeter,
        "fr" => CssUnitType.Flex,
        _ => CssUnitType.Unknown
    };

    public bool Equals(CssPrimitiveValue? other) =>
        other is not null && UnitType == other.UnitType &&
        Math.Abs(DoubleValueWithoutClamping - other.DoubleValueWithoutClamping) < 1e-9;

    public override bool Equals(CssValue other) => other is CssPrimitiveValue v && Equals(v);
    public override int GetHashCode() => HashCode.Combine((int)UnitType, DoubleValueWithoutClamping);
}

public static class UnitCategoryTests
{
    public static UnitCategory Test(CssUnitType unit) => unit switch
    {
        CssUnitType.Number or CssUnitType.Integer => UnitCategory.Number,
        CssUnitType.Percentage => UnitCategory.Percent,
        CssUnitType.Ems or CssUnitType.Exs or CssUnitType.Pixels or CssUnitType.Centimeters or
        CssUnitType.Millimeters or CssUnitType.Inches or CssUnitType.Points or CssUnitType.Picas or
        CssUnitType.QuarterMillimeters or CssUnitType.Rems or CssUnitType.QuirkyEms or CssUnitType.Caps or
        CssUnitType.Rcaps or CssUnitType.Chs or CssUnitType.Ics or CssUnitType.Lhs or CssUnitType.Rlhs or
        CssUnitType.Rexs or CssUnitType.Rchs or CssUnitType.Rics or CssUnitType.UserUnits or
        (>= CssUnitType.ViewportWidth and <= CssUnitType.DynamicViewportMax) or
        (>= CssUnitType.ContainerWidth and <= CssUnitType.ContainerMax) => UnitCategory.Length,
        CssUnitType.Degrees or CssUnitType.Radians or CssUnitType.Gradians or CssUnitType.Turns => UnitCategory.Angle,
        CssUnitType.Milliseconds or CssUnitType.Seconds => UnitCategory.Time,
        CssUnitType.Hertz or CssUnitType.Kilohertz => UnitCategory.Frequency,
        CssUnitType.DotsPerPixel or CssUnitType.X or CssUnitType.DotsPerInch or CssUnitType.DotsPerCentimeter => UnitCategory.Resolution,
        CssUnitType.Flex => UnitCategory.Other,
        _ => UnitCategory.Other
    };
}

/// <summary>A simple numeric literal value (number, length, percentage, angle, etc.).</summary>
public sealed class CssNumericLiteralValue : CssPrimitiveValue
{
    public double Value { get; }
    public override CssUnitType UnitType { get; }

    public CssNumericLiteralValue(double value, CssUnitType unitType) : base(CssClassType.NumericLiteral)
    {
        Value = value;
        UnitType = unitType;
    }

    public override double DoubleValueWithoutClamping => Value;
    public override bool IsZero => Math.Abs(Value) < 1e-9;
    public override bool IsOne => Math.Abs(Value - 1) < 1e-9;
    public override bool IsHundred => Math.Abs(Value - 100) < 1e-9;
    public override bool IsNegative => Value < 0;

    public override string CssText()
    {
        if (UnitType == CssUnitType.Number)
            return Value.ToString(CultureInfo.InvariantCulture);
        string num = Value.ToString(CultureInfo.InvariantCulture);
        return num + UnitTypeToString(UnitType);
    }

    public static CssNumericLiteralValue Create(double value, CssUnitType unitType) => new(value, unitType);
}