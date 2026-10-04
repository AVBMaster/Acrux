using System.Globalization;
using Acrux.Core.Performance;

namespace Acrux.Core.Dom.Html;

public class HTMLInputElement : HtmlElement
{
    public HTMLInputElement(Document document, string? name = null, string? namespaceUri = null)
        : base(name ?? "input") { }

    public string? Accept { get => GetAttribute("accept"); set => SetAttribute("accept", value); }
    public string? Alt { get => GetAttribute("alt"); set => SetAttribute("alt", value); }
    public string? Autocomplete { get => GetAttribute("autocomplete"); set => SetAttribute("autocomplete", value); }
    public bool Autofocus { get => HasAttribute("autofocus"); set => SetBoolAttr("autofocus", value); }
    public bool DefaultChecked { get => HasAttribute("checked"); set => SetBoolAttr("checked", value); }
    private bool _checked;
    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked != value)
            {
                _checked = value;
                DirtyState.AddSelf(this, DirtyFlags.Paint);
                DomMutationTracker.Notify();
            }
        }
    }
    public string? DirName { get => GetAttribute("dirname"); set => SetAttribute("dirname", value); }
    public bool Disabled { get => HasAttribute("disabled"); set => SetBoolAttr("disabled", value); }
    public HTMLFormElement? Form { get; set; }
    public string? FormAction { get => GetAttribute("formaction"); set => SetAttribute("formaction", value); }
    public string? FormEnctype { get => GetAttribute("formenctype"); set => SetAttribute("formenctype", value); }
    public string? FormMethod { get => GetAttribute("formmethod"); set => SetAttribute("formmethod", value); }
    public bool FormNoValidate { get => HasAttribute("formnovalidate"); set => SetBoolAttr("formnovalidate", value); }
    public string? FormTarget { get => GetAttribute("formtarget"); set => SetAttribute("formtarget", value); }
    public ulong Height { get => ulong.TryParse(GetAttribute("height"), out var v) ? v : 0; set => SetAttribute("height", value.ToString()); }
    public bool Indeterminate { get; set; }
    public HTMLDataListElement? List { get; set; }
    public string? Max { get => GetAttribute("max"); set => SetAttribute("max", value); }
    public int? MaxLength { get => int.TryParse(GetAttribute("maxlength"), out var v) ? v : null; set => SetAttribute("maxlength", value?.ToString()); }
    public string? Min { get => GetAttribute("min"); set => SetAttribute("min", value); }
    public int? MinLength { get => int.TryParse(GetAttribute("minlength"), out var v) ? v : null; set => SetAttribute("minlength", value?.ToString()); }
    public bool Multiple { get => HasAttribute("multiple"); set => SetBoolAttr("multiple", value); }
    public string? Name { get => GetAttribute("name"); set => SetAttribute("name", value); }
    public string? Pattern { get => GetAttribute("pattern"); set => SetAttribute("pattern", value); }
    public string? Placeholder { get => GetAttribute("placeholder"); set => SetAttribute("placeholder", value); }
    public bool ReadOnly { get => HasAttribute("readonly"); set => SetBoolAttr("readonly", value); }
    public bool Required { get => HasAttribute("required"); set => SetBoolAttr("required", value); }
    public ulong Size { get => ulong.TryParse(GetAttribute("size"), out var v) ? v : 20; set => SetAttribute("size", value.ToString()); }
    public string? Src { get => GetAttribute("src"); set => SetAttribute("src", value); }
    public string? Step { get => GetAttribute("step"); set => SetAttribute("step", value); }
    public string? Type { get => GetAttribute("type") ?? "text"; set => SetAttribute("type", value); }
    public string? DefaultValue { get => GetAttribute("value"); set => SetAttribute("value", value); }
    public string Value { get => GetAttribute("value") ?? ""; set => SetAttribute("value", value); }
    public ulong Width { get => ulong.TryParse(GetAttribute("width"), out var v) ? v : 0; set => SetAttribute("width", value.ToString()); }
    public DateTime? ValueAsDate { get; set; }
    public double ValueAsNumber { get => double.TryParse(Value, out var v) ? v : double.NaN; set => Value = value.ToString(CultureInfo.InvariantCulture); }

    public void StepUp(int n = 1) { }
    public void StepDown(int n = 1) { }

    public void Select()
    {
        var evt = new Event("select", new EventInit { Bubbles = true, Cancelable = false });
        DispatchEvent(evt);
    }

    public void ShowPicker() { }

    private void SetBoolAttr(string name, bool value)
    {
        if (value) SetAttribute(name, "");
        else RemoveAttribute(name);
    }
}


