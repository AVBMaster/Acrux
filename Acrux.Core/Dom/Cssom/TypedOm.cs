using System.Globalization;
using System.Numerics;

namespace Acrux.Core.Dom.Cssom;

public abstract class CSSStyleValue
{
    public override string ToString() => "";
}

public class CSSKeywordValue : CSSStyleValue
{
    public string Value { get; set; }

    public CSSKeywordValue(string value) { Value = value; }

    public override string ToString() => Value;
}

public class CSSNumericValue : CSSStyleValue
{
}

public class CSSUnitValue : CSSNumericValue
{
    public double Value { get; set; }
    public string Unit { get; set; }

    public CSSUnitValue(double value, string unit)
    {
        Value = value;
        Unit = unit;
    }

    public override string ToString() => $"{Value.ToString(CultureInfo.InvariantCulture)}{Unit}";
}

public class CSSMathValue : CSSNumericValue
{
    public string Operator { get; }
    public CSSNumericValue[] Values { get; }

    public CSSMathValue(string op, CSSNumericValue[] values)
    {
        Operator = op;
        Values = values;
    }

    public override string ToString() =>
        $"{Operator}({string.Join(", ", Values.AsEnumerable())})";
}

public class CSSTransformComponent
{
    public bool Is2D { get; set; } = true;
    public DomMatrix? ToMatrix() => null;
    public override string ToString() => "";
}

public class CSSTranslate : CSSTransformComponent
{
    public CSSNumericValue X { get; }
    public CSSNumericValue Y { get; }
    public CSSNumericValue? Z { get; }

    public CSSTranslate(CSSNumericValue x, CSSNumericValue y, CSSNumericValue? z = null)
    {
        X = x; Y = y; Z = z;
        Is2D = z == null;
    }

    public override string ToString() =>
        Z != null ? $"translate3d({X}, {Y}, {Z})" : $"translate({X}, {Y})";
}

public class CSSRotate : CSSTransformComponent
{
    public CSSNumericValue? Angle { get; }
    public double? X { get; }
    public double? Y { get; }
    public double? Z { get; }

    public CSSRotate(CSSNumericValue? angle = null, double? x = null, double? y = null, double? z = null)
    {
        Angle = angle; X = x; Y = y; Z = z;
    }

    public override string ToString() =>
        (X.HasValue && Y.HasValue && Z.HasValue)
            ? $"rotate3d({X}, {Y}, {Z}, {Angle})"
            : $"rotate({Angle})";
}

public class CSSScale : CSSTransformComponent
{
    public CSSNumericValue X { get; }
    public CSSNumericValue? Y { get; }
    public CSSNumericValue? Z { get; }

    public CSSScale(CSSNumericValue x, CSSNumericValue? y = null, CSSNumericValue? z = null)
    {
        X = x; Y = y; Z = z;
        Is2D = z == null;
    }

    public override string ToString() =>
        Z != null ? $"scale3d({X}, {Y}, {Z})" : $"scale({X}, {Y ?? X})";
}

public class CSSSkew : CSSTransformComponent
{
    public CSSNumericValue Ax { get; }
    public CSSNumericValue? Ay { get; }

    public CSSSkew(CSSNumericValue ax, CSSNumericValue? ay = null) { Ax = ax; Ay = ay; }

    public override string ToString() => $"skew({Ax}, {Ay})";
}

public class CSSPerspective : CSSTransformComponent
{
    public CSSNumericValue Length { get; }

    public CSSPerspective(CSSNumericValue length) { Length = length; Is2D = false; }

    public override string ToString() => $"perspective({Length})";
}

public class CSSMatrixComponent : CSSTransformComponent
{
    public DomMatrix Matrix { get; }

    public CSSMatrixComponent(DomMatrix matrix) { Matrix = matrix; }

    public override string ToString() => $"matrix({Matrix})";
}

public class CSSTransformValue : CSSStyleValue
{
    public CSSTransformComponent[] TransformComponents { get; }
    public bool Is2D => TransformComponents.All(c => c.Is2D);
    public int Length => TransformComponents.Length;

    public CSSTransformValue(CSSTransformComponent[] components)
    {
        TransformComponents = components;
    }

    public DomMatrix? ToMatrix()
    {
        if (TransformComponents.Length == 0) return null;
        DomMatrix? matrix = null;
        foreach (var component in TransformComponents)
        {
            var m = component.ToMatrix();
            if (m == null) continue;
            matrix = matrix != null ? matrix.Multiply(m) : m;
        }
        return matrix;
    }

    public override string ToString() =>
        string.Join(" ", TransformComponents.AsEnumerable());
}

public class StylePropertyMap
{
    private readonly Dictionary<string, CSSStyleValue[]> _properties = new();

    public int Size => _properties.Count;

    public CSSStyleValue[]? Get(string property) =>
        _properties.TryGetValue(property, out var val) ? val : null;

    public void Set(string property, params CSSStyleValue[] values)
    {
        _properties[property] = values;
    }

    public void Append(string property, params CSSStyleValue[] values)
    {
        if (_properties.TryGetValue(property, out var existing))
            _properties[property] = existing.Concat(values).ToArray();
        else
            _properties[property] = values;
    }

    public void Delete(string property) => _properties.Remove(property);

    public void Clear() => _properties.Clear();

    public IEnumerable<KeyValuePair<string, CSSStyleValue[]>> Entries() => _properties;

    public IEnumerable<string> Keys() => _properties.Keys;

    public IEnumerable<CSSStyleValue[]> Values() => _properties.Values;

    public bool Has(string property) => _properties.ContainsKey(property);
}

public static class CSS
{
    public static bool Supports(string property, string? value = null) => true;

    public static CSSUnitValue Number(double value) => new(value, "number");
    public static CSSUnitValue Percent(double value) => new(value, "percent");
    public static CSSUnitValue Em(double value) => new(value, "em");
    public static CSSUnitValue Rem(double value) => new(value, "rem");
    public static CSSUnitValue Px(double value) => new(value, "px");
    public static CSSUnitValue Cm(double value) => new(value, "cm");
    public static CSSUnitValue Mm(double value) => new(value, "mm");
    public static CSSUnitValue In(double value) => new(value, "in");
    public static CSSUnitValue Pt(double value) => new(value, "pt");
    public static CSSUnitValue Pc(double value) => new(value, "pc");
    public static CSSUnitValue Ex(double value) => new(value, "ex");
    public static CSSUnitValue Ch(double value) => new(value, "ch");
    public static CSSUnitValue Vw(double value) => new(value, "vw");
    public static CSSUnitValue Vh(double value) => new(value, "vh");
    public static CSSUnitValue Vmin(double value) => new(value, "vmin");
    public static CSSUnitValue Vmax(double value) => new(value, "vmax");
    public static CSSUnitValue Deg(double value) => new(value, "deg");
    public static CSSUnitValue Rad(double value) => new(value, "rad");
    public static CSSUnitValue Grad(double value) => new(value, "grad");
    public static CSSUnitValue Turn(double value) => new(value, "turn");
    public static CSSUnitValue S(double value) => new(value, "s");
    public static CSSUnitValue Ms(double value) => new(value, "ms");
    public static CSSUnitValue Hz(double value) => new(value, "hz");
    public static CSSUnitValue KHz(double value) => new(value, "khz");
    public static CSSUnitValue Dpi(double value) => new(value, "dpi");
    public static CSSUnitValue Dpcm(double value) => new(value, "dpcm");
    public static CSSUnitValue Dppx(double value) => new(value, "dppx");
    public static CSSUnitValue Fr(double value) => new(value, "fr");

    public static CSSMathValue Min(params CSSNumericValue[] values) =>
        new("min", values);
    public static CSSMathValue Max(params CSSNumericValue[] values) =>
        new("max", values);
    public static CSSMathValue Clamp(CSSNumericValue min, CSSNumericValue val, CSSNumericValue max) =>
        new("clamp", new[] { min, val, max });
}
