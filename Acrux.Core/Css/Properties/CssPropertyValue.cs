using Acrux.Core.Css.Values;

namespace Acrux.Core.Css.Properties;

public readonly struct CssPropertyValueMetadata
{
    public CssPropertyName Name { get; }
    public bool IsImportant { get; }
    public bool IsImplicit { get; }
    public CssPropertyId ShorthandId { get; }

    public CssPropertyValueMetadata(CssPropertyName name, bool isImportant = false, bool isImplicit = false, CssPropertyId shorthandId = CssPropertyId.Invalid)
    {
        Name = name;
        IsImportant = isImportant;
        IsImplicit = isImplicit;
        ShorthandId = shorthandId;
    }
}

/// <summary>A single CSS property-value pair, mirroring Blink's CSSPropertyValue.</summary>
public readonly struct CssPropertyValue
{
    public CssPropertyValueMetadata Metadata { get; }
    public CssValue Value { get; }

    public CssPropertyName Name => Metadata.Name;
    public bool IsImportant => Metadata.IsImportant;
    public bool IsImplicit => Metadata.IsImplicit;
    public CssPropertyId ShorthandId => Metadata.ShorthandId;

    public CssPropertyValue(CssPropertyName name, CssValue value, bool isImportant = false, bool isImplicit = false, CssPropertyId shorthandId = CssPropertyId.Invalid)
    {
        Metadata = new CssPropertyValueMetadata(name, isImportant, isImplicit, shorthandId);
        Value = value;
    }

    public CssPropertyValue(CssPropertyValueMetadata metadata, CssValue value)
    {
        Metadata = metadata;
        Value = value;
    }
}