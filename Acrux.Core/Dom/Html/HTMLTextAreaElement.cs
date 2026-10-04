namespace Acrux.Core.Dom.Html;

public class HTMLTextAreaElement : HtmlElement
{
    public HTMLTextAreaElement(Document document, string? name = null, string? namespaceUri = null)
        : base(name ?? "textarea") { }

    public string? Autocomplete { get => GetAttribute("autocomplete"); set => SetAttribute("autocomplete", value); }
    public bool Autofocus { get => HasAttribute("autofocus"); set => SetBoolAttr("autofocus", value); }
    public int Cols { get => int.TryParse(GetAttribute("cols"), out var v) ? v : 20; set => SetAttribute("cols", value.ToString()); }
    public string? DefaultValue
    {
        get => GetAttribute("value") ?? TextContent ?? "";
        set { SetAttribute("value", value); TextContent = value; }
    }
    public bool Disabled { get => HasAttribute("disabled"); set => SetBoolAttr("disabled", value); }
    public HTMLFormElement? Form { get; set; }
    public int? MaxLength { get => int.TryParse(GetAttribute("maxlength"), out var v) ? v : null; set => SetAttribute("maxlength", value?.ToString()); }
    public int? MinLength { get => int.TryParse(GetAttribute("minlength"), out var v) ? v : null; set => SetAttribute("minlength", value?.ToString()); }
    public string? Name { get => GetAttribute("name"); set => SetAttribute("name", value); }
    public string? Placeholder { get => GetAttribute("placeholder"); set => SetAttribute("placeholder", value); }
    public bool ReadOnly { get => HasAttribute("readonly"); set => SetBoolAttr("readonly", value); }
    public bool Required { get => HasAttribute("required"); set => SetBoolAttr("required", value); }
    public int Rows { get => int.TryParse(GetAttribute("rows"), out var v) ? v : 2; set => SetAttribute("rows", value.ToString()); }
    public string? SelectionDirection { get; set; }
    public int? SelectionEnd { get; set; }
    public int? SelectionStart { get; set; }
    public string? TextLength => Value?.Length.ToString();
    public string? Type => "textarea";
    public string? Value
    {
        get => GetAttribute("value") ?? TextContent ?? "";
        set { SetAttribute("value", value); TextContent = value; }
    }
    public string? Wrap { get => GetAttribute("wrap"); set => SetAttribute("wrap", value); }
    public NodeList? Labels { get; set; }

    public void Select()
    {
        var evt = new Event("select", new EventInit { Bubbles = true, Cancelable = false });
        DispatchEvent(evt);
    }

    public void SetRangeText(string replacement, int? start = null, int? end = null, string? selectionMode = null) { }
    public void SetSelectionRange(int start, int end, string? direction = null)
    {
        SelectionStart = start;
        SelectionEnd = end;
        SelectionDirection = direction;
    }

    private void SetBoolAttr(string name, bool value)
    {
        if (value) SetAttribute(name, "");
        else RemoveAttribute(name);
    }
}


