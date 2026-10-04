using Acrux.Core.Css.Values;

namespace Acrux.Core.Css.Properties;

public enum CssParserMode
{
    UACSS,
    HTMLStandard,
    SVGAttribute,
    MathML
}

public enum SetResult
{
    ParseError = 0,
    Unchanged = 1,
    ModifiedExisting = 2,
    ChangedPropertySet = 3
}

/// <summary>
/// A set of CSS property-value pairs, mirroring Blink's CSSPropertyValueSet.
/// This is the mutable storage used during parsing and for inline styles.
/// </summary>
public class CssPropertyValueSet
{
    private readonly List<CssPropertyValue> _properties = new();
    public CssParserMode ParserMode { get; }

    public CssPropertyValueSet(CssParserMode parserMode = CssParserMode.HTMLStandard)
    {
        ParserMode = parserMode;
    }

    public int PropertyCount => _properties.Count;
    public bool IsEmpty => _properties.Count == 0;

    public IReadOnlyList<CssPropertyValue> Properties => _properties;

    public CssPropertyValue PropertyAt(int index) => _properties[index];

    public int FindPropertyIndex(CssPropertyId id)
    {
        for (int i = _properties.Count - 1; i >= 0; i--)
        {
            // Logical properties: match by resolving. For now compare directly.
            if (_properties[i].Name.Id == id && !_properties[i].Name.IsCustom)
                return i;
        }
        return -1;
    }

    public int FindPropertyIndex(string customName)
    {
        for (int i = _properties.Count - 1; i >= 0; i--)
        {
            if (_properties[i].Name.IsCustom && _properties[i].Name.CustomName == customName)
                return i;
        }
        return -1;
    }

    public bool HasProperty(CssPropertyId id) => FindPropertyIndex(id) >= 0;

    public CssValue? GetPropertyCssValue(CssPropertyId id)
    {
        int idx = FindPropertyIndex(id);
        return idx >= 0 ? _properties[idx].Value : null;
    }

    public CssValue? GetPropertyCssValue(string customName)
    {
        int idx = FindPropertyIndex(customName);
        return idx >= 0 ? _properties[idx].Value : null;
    }

    public string GetPropertyValue(CssPropertyId id)
    {
        var val = GetPropertyCssValue(id);
        return val?.CssText() ?? "";
    }

    public bool PropertyIsImportant(CssPropertyId id)
    {
        int idx = FindPropertyIndex(id);
        return idx >= 0 && _properties[idx].IsImportant;
    }

    public SetResult SetProperty(CssPropertyId id, CssValue value, bool important = false)
    {
        return SetLonghandProperty(new CssPropertyValue(new CssPropertyName(id), value, important));
    }

    public SetResult SetProperty(string customName, CssValue value, bool important = false)
    {
        return SetLonghandProperty(new CssPropertyValue(new CssPropertyName(customName), value, important));
    }

    /// <summary>The central setter, mirroring MutableCSSPropertyValueSet::SetLonghandProperty.</summary>
    public SetResult SetLonghandProperty(CssPropertyValue property)
    {
        int index = property.Name.IsCustom
            ? FindPropertyIndex(property.Name.CustomName!)
            : FindPropertyIndex(property.Name.Id);

        if (index >= 0)
        {
            var existing = _properties[index];
            if (existing.Value.Equals(property.Value) && existing.IsImportant == property.IsImportant)
                return SetResult.Unchanged;
            _properties[index] = property;
            return SetResult.ModifiedExisting;
        }

        _properties.Add(property);
        return SetResult.ChangedPropertySet;
    }

    public bool RemoveProperty(CssPropertyId id)
    {
        int idx = FindPropertyIndex(id);
        if (idx < 0) return false;
        _properties.RemoveAt(idx);
        return true;
    }

    public void Clear() => _properties.Clear();

    public string AsText()
    {
        return string.Join("; ", _properties.Select(p =>
            $"{p.Name.ToCssString()}: {p.Value.CssText()}{(p.IsImportant ? " !important" : "")}"));
    }

    public CssPropertyValueSet MutableCopy()
    {
        var copy = new CssPropertyValueSet(ParserMode);
        copy._properties.AddRange(_properties);
        return copy;
    }

    public override bool Equals(object? obj)
    {
        if (obj is not CssPropertyValueSet other || other._properties.Count != _properties.Count)
            return false;
        for (int i = 0; i < _properties.Count; i++)
        {
            var a = _properties[i];
            var b = other._properties[i];
            if (!a.Name.Equals(b.Name) || a.IsImportant != b.IsImportant || !a.Value.Equals(b.Value))
                return false;
        }
        return true;
    }

    public override int GetHashCode()
    {
        int hash = 0;
        foreach (var p in _properties)
            hash = hash * 31 + p.Name.GetHashCode() + p.Value.GetHashCode() + (p.IsImportant ? 7 : 0);
        return hash;
    }
}