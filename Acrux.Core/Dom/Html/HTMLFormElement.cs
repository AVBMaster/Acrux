using System.Text;

namespace Acrux.Core.Dom.Html;

public class HTMLFormElement : HtmlElement
{
    public HTMLFormElement(Document document, string? name = null, string? namespaceUri = null)
        : base(name ?? "form") { }

    public string? AcceptCharset { get => GetAttribute("accept-charset"); set => SetAttribute("accept-charset", value); }
    public string? Action { get => GetAttribute("action"); set => SetAttribute("action", value); }
    public string? Autocomplete { get => GetAttribute("autocomplete"); set => SetAttribute("autocomplete", value); }
    public string? Enctype
    {
        get => GetAttribute("enctype") ?? "application/x-www-form-urlencoded";
        set => SetAttribute("enctype", value);
    }
    public string? Encoding => Enctype;
    public string? Method
    {
        get => (GetAttribute("method") ?? "get").ToLowerInvariant();
        set => SetAttribute("method", value);
    }
    public string? Name { get => GetAttribute("name"); set => SetAttribute("name", value); }
    public bool NoValidate { get => HasAttribute("novalidate"); set => SetBoolAttr("novalidate", value); }
    public string? Target { get => GetAttribute("target"); set => SetAttribute("target", value); }
    public string? Rel { get => GetAttribute("rel"); set => SetAttribute("rel", value); }

    public HtmlCollection Elements
    {
        get
        {
            var elements = new List<HtmlElement>();
            CollectFormElements(this, elements);
            return new HtmlCollection(elements.Cast<Element>().ToList());
        }
    }

    public int Length => Elements.Length;

    public HtmlElement? this[int index] => Elements[index] as HtmlElement;
    public HtmlElement? this[string name] => Elements.Cast<HtmlElement>().FirstOrDefault(
        e => e.GetAttribute("name") == name || e.GetAttribute("id") == name);

    public void Submit()
    {
        var evt = new SubmitEvent("submit", new SubmitEventInit { Bubbles = true, Cancelable = true });
        DispatchEvent(evt);
        if (!evt.DefaultPrevented)
        {
        }
    }

    public void RequestSubmit(HtmlElement? submitter = null)
    {
        var evt = new SubmitEvent("submit", new SubmitEventInit { Bubbles = true, Cancelable = true });
        DispatchEvent(evt);
    }

    public void Reset()
    {
        var evt = new Event("reset", new EventInit { Bubbles = true, Cancelable = true });
        DispatchEvent(evt);
        if (!evt.DefaultPrevented)
        {
            foreach (var el in Elements)
            {
                if (el is Element e)
                {
                    string? tag = e.TagName?.ToLowerInvariant();
                    if (tag == "input")
                    {
                        string? type = e.GetAttribute("type")?.ToLowerInvariant();
                        if (type == "checkbox" || type == "radio")
                        {
                            bool defChecked = e.GetAttribute("checked") != null;
                            if (!defChecked && e.HasAttribute("checked"))
                                e.RemoveAttribute("checked");
                            else if (defChecked && !e.HasAttribute("checked"))
                                e.SetAttribute("checked", "");
                        }
                        else
                        {
                            // text, password, email, etc. — restore default value
                            string? defValue = e.GetAttribute("value");
                            e.Value = defValue ?? "";
                        }
                    }
                    else if (tag == "textarea")
                    {
                        e.Value = e.GetAttribute("value") ?? e.TextContent ?? "";
                    }
                    else if (tag == "select")
                    {
                        // Reset to first option or selected option
                        foreach (var child in e.ChildNodes)
                        {
                            if (child is Element opt && opt.TagName == "OPTION")
                            {
                                bool isDef = opt.HasAttribute("selected");
                                if (isDef)
                                    opt.SetAttribute("selected", "");
                                else
                                    opt.RemoveAttribute("selected");
                            }
                        }
                    }
                }
            }
        }
    }

    public bool CheckValidity() => true;
    public bool ReportValidity() => true;

    private static void CollectFormElements(HtmlElement root, List<HtmlElement> elements)
    {
        foreach (var child in root.ChildNodes)
        {
            if (child is HtmlElement el)
            {
                var tag = el.NodeName?.ToLowerInvariant();
                if (tag is "input" or "select" or "textarea" or "button" or "output" or "datalist" or "progress" or "meter")
                    elements.Add(el);
                CollectFormElements(el, elements);
            }
        }
    }

    private void SetBoolAttr(string name, bool value)
    {
        if (value) SetAttribute(name, "");
        else RemoveAttribute(name);
    }
}


