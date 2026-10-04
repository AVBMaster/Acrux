namespace Acrux.Core.Dom.Html;

public class ValidityState
{
    public bool ValueMissing { get; set; }
    public bool TypeMismatch { get; set; }
    public bool PatternMismatch { get; set; }
    public bool TooLong { get; set; }
    public bool TooShort { get; set; }
    public bool RangeUnderflow { get; set; }
    public bool RangeOverflow { get; set; }
    public bool StepMismatch { get; set; }
    public bool BadInput { get; set; }
    public bool CustomError { get; set; }
    public bool Valid =>
        !ValueMissing && !TypeMismatch && !PatternMismatch &&
        !TooLong && !TooShort && !RangeUnderflow && !RangeOverflow &&
        !StepMismatch && !BadInput && !CustomError;
}

public class FormData
{
    private readonly List<KeyValuePair<string, string>> _entries = new();

    public FormData(HTMLFormElement? form = null)
    {
        if (form != null)
        {
            foreach (var el in form.Elements)
            {
                var name = el.GetAttribute("name");
                if (string.IsNullOrEmpty(name)) continue;
                if (el is HTMLInputElement input)
                {
                    if (input.Type == "checkbox" || input.Type == "radio")
                    {
                        if (input.Checked)
                            Append(name, input.Value);
                    }
                    else if (input.Type != "submit" && input.Type != "button")
                    {
                        Append(name, input.Value);
                    }
                }
                else
                {
                    var value = el.GetAttribute("value") ?? el.TextContent ?? "";
                    Append(name, value);
                }
            }
        }
    }

    public int Length => _entries.Count;

    public void Append(string name, string value)
    {
        _entries.Add(new KeyValuePair<string, string>(name, value));
    }

    public void Append(string name, string value, string? filename = null)
    {
        Append(name, value);
    }

    public void Delete(string name)
    {
        _entries.RemoveAll(e => e.Key == name);
    }

    public string? Get(string name)
    {
        var entry = _entries.FirstOrDefault(e => e.Key == name);
        return entry.Key != null ? entry.Value : null;
    }

    public string[] GetAll(string name)
    {
        return _entries.Where(e => e.Key == name).Select(e => e.Value).ToArray();
    }

    public bool Has(string name)
    {
        return _entries.Any(e => e.Key == name);
    }

    public void Set(string name, string value)
    {
        Delete(name);
        Append(name, value);
    }

    public IEnumerable<KeyValuePair<string, string>> Entries() => _entries;

    public IEnumerator<KeyValuePair<string, string>> GetEnumerator() => _entries.GetEnumerator();
}

public class HTMLFormControlsCollection
{
    private readonly List<HtmlElement> _elements = new();

    public int Length => _elements.Count;
    public HtmlElement? this[int index] => index >= 0 && index < _elements.Count ? _elements[index] : null;
    public HtmlElement? this[string name] => _elements.FirstOrDefault(
        e => e.GetAttribute("name") == name || e.GetAttribute("id") == name);

    public void Add(HtmlElement element) => _elements.Add(element);
    public void Remove(HtmlElement element) => _elements.Remove(element);
}

public class HTMLOptionsCollection
{
    private readonly List<HtmlElement> _options = new();

    public int Length => _options.Count;
    public int SelectedIndex { get; set; } = -1;

    public HtmlElement? this[int index] => index >= 0 && index < _options.Count ? _options[index] : null;
    public HtmlElement? this[string name] => _options.FirstOrDefault(
        o => o.GetAttribute("id") == name || o.GetAttribute("name") == name);

    public void Add(HtmlElement element, HtmlElement? before = null)
    {
        if (before != null)
        {
            var idx = _options.IndexOf(before);
            if (idx >= 0) _options.Insert(idx, element);
            else _options.Add(element);
        }
        else _options.Add(element);
    }

    public void Remove(int index)
    {
        if (index >= 0 && index < _options.Count) _options.RemoveAt(index);
    }

    public void Remove(HtmlElement element) => _options.Remove(element);
}


