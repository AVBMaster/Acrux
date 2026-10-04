namespace Acrux.Core.Dom.Html;

public class HTMLSlotElement : HtmlElement
{
    public HTMLSlotElement(Document document, string? name = null, string? namespaceUri = null)
        : base(name ?? "slot") { }

    public string? Name { get => GetAttribute("name") ?? ""; set => SetAttribute("name", value); }

    public Node[] AssignedNodes(AssignedNodesOptions? options = null) => Array.Empty<Node>();
    public Element[] AssignedElements(AssignedNodesOptions? options = null) => Array.Empty<Element>();

    public void Assign(params Node[] nodes) { }
}

public class AssignedNodesOptions
{
    public bool Flatten { get; set; }
}

public class HTMLDataListElement : HtmlElement
{
    public HTMLDataListElement(Document document, string? name = null, string? namespaceUri = null)
        : base(name ?? "datalist") { }

    public HtmlCollection Options => new(ChildNodes.OfType<HtmlElement>().Cast<Element>().ToList());
}

public class HTMLDetailsElement : HtmlElement
{
    public HTMLDetailsElement(Document document, string? name = null, string? namespaceUri = null)
        : base(name ?? "details") { }

    public bool Open { get => HasAttribute("open"); set => SetBoolAttr("open", value); }

    private void SetBoolAttr(string name, bool value)
    {
        if (value) SetAttribute(name, "");
        else RemoveAttribute(name);
    }
}

public class HTMLDialogElement : HtmlElement
{
    public HTMLDialogElement(Document document, string? name = null, string? namespaceUri = null)
        : base(name ?? "dialog") { }

    public bool Open { get => HasAttribute("open"); set => SetBoolAttr("open", value); }
    public string? ReturnValue { get; set; }

    public void Show()
    {
        Open = true;
        DispatchEvent(new Event("toggle", new EventInit { Bubbles = false, Cancelable = false }));
    }

    public void ShowModal()
    {
        if (Open) throw new DOMException("Already open", "InvalidStateError");
        Open = true;
        DispatchEvent(new Event("toggle", new EventInit { Bubbles = false, Cancelable = false }));
    }

    public void Close(string? returnValue = null)
    {
        if (!Open) return;
        Open = false;
        ReturnValue = returnValue;
        DispatchEvent(new Event("close", new EventInit { Bubbles = false, Cancelable = false }));
    }

    private void SetBoolAttr(string name, bool value)
    {
        if (value) SetAttribute(name, "");
        else RemoveAttribute(name);
    }
}


