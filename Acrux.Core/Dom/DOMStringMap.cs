namespace Acrux.Core.Dom;

public class DOMStringMap
{
    private readonly Func<string, string?> _getter;
    private readonly Action<string, string?> _setter;

    public DOMStringMap(Func<string, string?> getter, Action<string, string?> setter)
    {
        _getter = getter;
        _setter = setter;
    }

    public string? Get(string name) => _getter(ToDataDash(name));
    public void Set(string name, string value) => _setter(ToDataDash(name), value);
    public void Delete(string name) => _setter(ToDataDash(name), null);
    public bool Has(string name) => _getter(ToDataDash(name)) != null;

    private static string ToDataDash(string camelCase)
    {
        if (string.IsNullOrEmpty(camelCase)) return "data-";
        var sb = new System.Text.StringBuilder("data-");
        foreach (var ch in camelCase)
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
