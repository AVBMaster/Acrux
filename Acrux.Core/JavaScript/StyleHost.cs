using System.Dynamic;
using Acrux.Core.Dom;

namespace Acrux.Core.JavaScript;

public class StyleHost : DynamicObject
{
    private readonly Element _element;

    public StyleHost(Element element)
    {
        _element = element;
    }

    public string cssText
    {
        get => string.Join("; ", _element.Style.Select(kv => $"{kv.Key}: {kv.Value}"));
        set
        {
            _element.Style.Clear();
            if (string.IsNullOrEmpty(value)) return;
            foreach (var part in value.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var colon = part.IndexOf(':');
                if (colon > 0)
                {
                    var prop = part[..colon].Trim();
                    var val = part[(colon + 1)..].Trim();
                    if (!string.IsNullOrEmpty(prop))
                        _element.Style[prop] = val;
                }
            }
        }
    }

    public int length => _element.Style.Count;

    public object? parentRule => null;

    public string getPropertyValue(string propertyName) =>
        _element.Style.TryGetValue(propertyName, out var val) ? val : "";

    public string getPropertyPriority(string propertyName) => "";

    public void setProperty(string propertyName, string value) =>
        _element.Style[propertyName] = value;

    public string removeProperty(string propertyName)
    {
        _element.Style.Remove(propertyName, out var old);
        return old ?? "";
    }

    public string? item(int index)
    {
        if (index < 0 || index >= _element.Style.Count) return null;
        return _element.Style.ElementAt(index).Key;
    }

    public override bool TryGetMember(GetMemberBinder binder, out object? result)
    {
        var cssProp = ToCssProperty(binder.Name);
        if (_element.Style.TryGetValue(cssProp, out var val))
        {
            result = val;
            return true;
        }
        result = "";
        return true;
    }

    public override bool TrySetMember(SetMemberBinder binder, object? value)
    {
        var cssProp = ToCssProperty(binder.Name);
        _element.Style[cssProp] = value?.ToString() ?? "";
        return true;
    }

    private static string ToCssProperty(string jsName)
    {
        if (string.IsNullOrEmpty(jsName)) return jsName;
        var sb = new System.Text.StringBuilder(jsName.Length + 4);
        foreach (var ch in jsName)
        {
            if (char.IsUpper(ch))
            {
                sb.Append('-');
                sb.Append(char.ToLowerInvariant(ch));
            }
            else
            {
                sb.Append(ch);
            }
        }
        return sb.ToString();
    }
}
