using System.Globalization;
using SkiaSharp;

namespace Acrux.Core.Css.Values;

public enum CssClassType
{
    NumericLiteral,
    MathFunction,
    Identifier,
    Knot,
    Color,
    Counter,
    Quad,
    CustomIdent,
    String,
    URI,
    ValuePair,
    ValueList,
    Function,
    Image,
    Shadow,
    GridTemplateAreas,
    TypedOmWrapper,
    UnparsedDeclaration,
    PendingSubstitution,
    PendingSystemFont,
    InvalidVariable,
    CyclicVariable,
    Inherited,
    Initial,
    Unset,
    Revert,
    RevertLayer,
    FontFaceSrc,
    FontFamily,
    FontStyleRange,
    FontVariation,
    FontFeature,
    BorderImageSlice,
    Crossfade,
    Paint,
    LinearGradient,
    RadialGradient,
    ConicGradient,
    StringScientific,
    NamedColor,
    HexColor,
    RgbColor,
    HslColor,
    HwbColor,
    LabColor,
    LchColor,
    OklabColor,
    OklchColor,
    ColorMix,
    LightDark,
    RelativeColor,
    CubicBezierTimingFunction,
    StepsTimingFunction,
    LinearTimingFunction,
    BasicShapeCircle,
    BasicShapeEllipse,
    BasicShapePolygon,
    BasicShapeInset,
    BasicShapeRect,
    BasicShapeXywh,
    BasicShapePath,
    Path,
    Quad4,
    Ray,
    Alternate,
    CounterStyle,
    PaletteMix,
    Unparsed,
    Value,
    ImageSet,
    GridLineNames,
    GridAutoRepeat,
    GridIntegerRepeat,
    Axis,
    Repeat
}

/// <summary>
/// Base class for CSS values, mirroring Blink's CSSValue. Uses a ClassType tag for
/// fast type dispatch instead of virtual method dispatch when needed.
/// </summary>
public abstract class CssValue
{
    public CssClassType ClassTypeValue { get; }

    protected CssValue(CssClassType classType)
    {
        ClassTypeValue = classType;
    }

    public virtual bool IsBaseValueList => false;
    public bool IsValueList => IsBaseValueList;
    public bool IsPrimitiveValue => ClassTypeValue == CssClassType.NumericLiteral || ClassTypeValue == CssClassType.MathFunction;
    public bool IsNumericLiteralValue => ClassTypeValue == CssClassType.NumericLiteral;
    public bool IsMathFunctionValue => ClassTypeValue == CssClassType.MathFunction;
    public bool IsIdentifierValue => ClassTypeValue == CssClassType.Identifier;
    public bool IsInheritedValue => ClassTypeValue == CssClassType.Inherited;
    public bool IsInitialValue => ClassTypeValue == CssClassType.Initial;
    public bool IsUnsetValue => ClassTypeValue == CssClassType.Unset;
    public bool IsRevertValue => ClassTypeValue == CssClassType.Revert;
    public bool IsRevertLayerValue => ClassTypeValue == CssClassType.RevertLayer;
    public bool IsColorValue => ClassTypeValue is
        CssClassType.Color or CssClassType.NamedColor or CssClassType.HexColor or
        CssClassType.RgbColor or CssClassType.HslColor or CssClassType.HwbColor or
        CssClassType.LabColor or CssClassType.LchColor or CssClassType.OklabColor or
        CssClassType.OklchColor or CssClassType.ColorMix or CssClassType.LightDark or
        CssClassType.RelativeColor;
    public bool IsPendingSubstitutionValue => ClassTypeValue == CssClassType.PendingSubstitution;
    public bool IsUnparsedDeclaration => ClassTypeValue == CssClassType.UnparsedDeclaration;
    public bool IsInvalidVariableValue => ClassTypeValue == CssClassType.InvalidVariable;
    public bool IsCyclicVariableValue => ClassTypeValue == CssClassType.CyclicVariable;
    public bool IsInitialColorValue => false;

    /// <summary>Whether this value is a CSS-wide keyword (inherit/initial/unset/revert/revert-layer).</summary>
    public bool IsCssWideKeyword => ClassTypeValue is
        CssClassType.Inherited or CssClassType.Initial or CssClassType.Unset or
        CssClassType.Revert or CssClassType.RevertLayer;

    public abstract string CssText();

    public virtual bool Equals(CssValue other) => ReferenceEquals(this, other);

    public override bool Equals(object? obj) => obj is CssValue v && Equals(v);

    public override int GetHashCode()
    {
        int hash = (int)ClassTypeValue * 397;
        hash ^= CssText().GetHashCode();
        return hash;
    }

    public static CssValue? Inherited { get; } = new CssInheritedValue();
    public static CssValue? Initial { get; } = new CssInitialValue();
    public static CssValue? Unset { get; } = new CssUnsetValue();
    public static CssValue? Revert { get; } = new CssRevertValue();
    public static CssValue? RevertLayer { get; } = new CssRevertLayerValue();
}

public sealed class CssInheritedValue : CssValue
{
    public CssInheritedValue() : base(CssClassType.Inherited) { }
    public override string CssText() => "inherit";
}

public sealed class CssInitialValue : CssValue
{
    public CssInitialValue() : base(CssClassType.Initial) { }
    public override string CssText() => "initial";
}

public sealed class CssUnsetValue : CssValue
{
    public CssUnsetValue() : base(CssClassType.Unset) { }
    public override string CssText() => "unset";
}

public sealed class CssRevertValue : CssValue
{
    public CssRevertValue() : base(CssClassType.Revert) { }
    public override string CssText() => "revert";
}

public sealed class CssRevertLayerValue : CssValue
{
    public CssRevertLayerValue() : base(CssClassType.RevertLayer) { }
    public override string CssText() => "revert-layer";
}

public static class CssWideKeywordParser
{
    public static CssValue? Parse(string value) => value.ToLowerInvariant() switch
    {
        "inherit" => CssValue.Inherited,
        "initial" => CssValue.Initial,
        "unset" => CssValue.Unset,
        "revert" => CssValue.Revert,
        "revert-layer" => CssValue.RevertLayer,
        _ => null
    };
}