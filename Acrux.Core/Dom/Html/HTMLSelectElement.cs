using System.Linq;

namespace Acrux.Core.Dom.Html;

public class HTMLSelectElement : HtmlElement
{
    public HTMLSelectElement(Document document, string? name = null, string? namespaceUri = null)
        : base(name ?? "select") { }

    public bool Autofocus { get => HasAttribute("autofocus"); set => SetBoolAttr("autofocus", value); }
    public bool Disabled { get => HasAttribute("disabled"); set => SetBoolAttr("disabled", value); }
    public HTMLFormElement? Form { get; set; }
    public bool Multiple { get => HasAttribute("multiple"); set => SetBoolAttr("multiple", value); }
    public string? Name { get => GetAttribute("name"); set => SetAttribute("name", value); }
    public bool Required { get => HasAttribute("required"); set => SetBoolAttr("required", value); }
    public int Size { get => int.TryParse(GetAttribute("size"), out var v) ? v : Multiple ? 4 : 1; set => SetAttribute("size", value.ToString()); }
    public string? Type => Multiple ? "select-multiple" : "select-one";

    public HtmlCollection Options => Children;
    public int Length { get; set; }
    public HtmlElement? SelectedOptions { get; set; }
    public int SelectedIndex { get; set; } = -1;
    public HtmlElement? SelectedOption { get; set; }
    public string? Value
    {
        get
        {
            var selected = SelectedOption;
            return selected?.GetAttribute("value") ?? selected?.TextContent ?? "";
        }
        set
        {
            for (int i = 0; i < Options.Length; i++)
            {
                var opt = Options[i];
                if (opt.GetAttribute("value") == value || opt.TextContent == value)
                {
                    SelectedIndex = i;
                    return;
                }
            }
        }
    }

    public new HtmlCollection Children => new(ChildNodes.OfType<HtmlElement>().Cast<Element>().ToList());

    public HtmlElement? Item(int index) => index >= 0 && index < Options.Length ? Options[index] as HtmlElement : null;
    public HtmlElement? NamedItem(string name) => Options.Cast<HtmlElement>().FirstOrDefault(o => o.GetAttribute("name") == name || o.GetAttribute("id") == name);

    public void Add(HtmlElement? element, HtmlElement? before = null) { }
    public void Remove(int index = -1) { }

    private void SetBoolAttr(string name, bool value)
    {
        if (value) SetAttribute(name, "");
        else RemoveAttribute(name);
    }
}


