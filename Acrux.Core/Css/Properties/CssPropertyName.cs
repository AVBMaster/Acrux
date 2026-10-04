namespace Acrux.Core.Css.Properties;

/// <summary>Identifies a CSS property by ID or custom name, mirroring Blink's CSSPropertyName.</summary>
public readonly struct CssPropertyName : IEquatable<CssPropertyName>
{
    public CssPropertyId Id { get; }
    public string? CustomName { get; }

    public bool IsCustom => Id == CssPropertyId.Variable;

    public CssPropertyName(CssPropertyId id)
    {
        Id = id;
        CustomName = null;
    }

    public CssPropertyName(string customName)
    {
        Id = CssPropertyId.Variable;
        CustomName = customName;
    }

    public static CssPropertyName FromString(string name)
    {
        if (name.StartsWith("--"))
            return new CssPropertyName(name);
        var id = CssPropertyIdExtensions.FromString(name);
        return new CssPropertyName(id);
    }

    public string ToCssString() => IsCustom ? CustomName! : CssPropertyIdExtensions.ToString(Id);

    public bool Equals(CssPropertyName other) =>
        Id == other.Id && CustomName == other.CustomName;

    public override bool Equals(object? obj) => obj is CssPropertyName n && Equals(n);
    public override int GetHashCode() => HashCode.Combine((int)Id, CustomName);
    public override string ToString() => ToCssString();

    public static bool operator ==(CssPropertyName a, CssPropertyName b) => a.Equals(b);
    public static bool operator !=(CssPropertyName a, CssPropertyName b) => !a.Equals(b);
}