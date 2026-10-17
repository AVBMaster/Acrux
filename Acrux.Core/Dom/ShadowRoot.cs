namespace Acrux.Core.Dom;

public class ShadowRoot : DocumentFragment
{
    public ShadowMode Mode { get; }
    public Element Host { get; }
    public bool DelegatesFocus { get; }
    public SlotAssignmentMode SlotAssignment { get; }
    public bool Clonable { get; set; }
    public bool Serializable { get; set; }
    public string? ReferenceTarget { get; set; }

    internal ShadowRoot(Element host, ShadowMode mode, bool delegatesFocus = false,
        SlotAssignmentMode slotAssignment = SlotAssignmentMode.Named)
    {
        Host = host;
        Mode = mode;
        DelegatesFocus = delegatesFocus;
        SlotAssignment = slotAssignment;
        // DOM §4.2: a shadow root is a document fragment, and that is the name it answers — the tree
        // under the host is a fragment of its own, not a node of a kind the interface names otherwise.
        NodeName = "#document-fragment";
    }

    public override NodeType NodeType => NodeType.DocumentFragment;

    /// <summary>HTML §13.2.5.145: writing the root's markup parses it in the context of the host and
    /// puts the result into the shadow tree, so the elements the page wrote are queryable afterwards;
    /// reading it serialises the tree it holds.</summary>
    public string InnerHTML
    {
        get => BuildInnerHtml();
        set
        {
            Children.Clear();
            if (string.IsNullOrEmpty(value)) return;
            foreach (var child in Acrux.Core.JavaScript.HtmlParser.ParseFragment(value, Host.TagName))
                AppendChild(child);
        }
    }

    private string BuildInnerHtml()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var child in Children)
            SerializeNode(child, sb);
        return sb.ToString();
    }

    private static void SerializeNode(Node node, System.Text.StringBuilder sb)
    {
        if (node is TextNode tn)
            sb.Append(System.Net.WebUtility.HtmlEncode(tn.Data));
        else if (node is Element el)
        {
            sb.Append('<').Append(el.TagName.ToLowerInvariant());
            foreach (var attr in el.Attributes)
            {
                // HTML §13.4: every attribute is written with its value in quotes, an empty one included.
                sb.Append(' ').Append(attr.Key)
                    .Append("=\"").Append(System.Net.WebUtility.HtmlEncode(attr.Value ?? "")).Append('"');
            }
            sb.Append('>');
            foreach (var child in el.Children)
                SerializeNode(child, sb);
            sb.Append("</").Append(el.TagName.ToLowerInvariant()).Append('>');
        }
    }
}

public enum ShadowMode
{
    Open,
    Closed
}

public enum SlotAssignmentMode
{
    Named,
    Manual
}

public class ShadowRootInit
{
    public ShadowMode Mode { get; set; }
    public bool DelegatesFocus { get; set; }
    public SlotAssignmentMode SlotAssignment { get; set; } = SlotAssignmentMode.Named;
    public bool Clonable { get; set; }
    public bool Serializable { get; set; }
    public string? ReferenceTarget { get; set; }
}
