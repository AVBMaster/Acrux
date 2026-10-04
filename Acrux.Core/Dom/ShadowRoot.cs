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

    private string? _innerHtml;

    internal ShadowRoot(Element host, ShadowMode mode, bool delegatesFocus = false,
        SlotAssignmentMode slotAssignment = SlotAssignmentMode.Named)
    {
        Host = host;
        Mode = mode;
        DelegatesFocus = delegatesFocus;
        SlotAssignment = slotAssignment;
        NodeName = "#shadow-root";
    }

    public override NodeType NodeType => NodeType.DocumentFragment;

    public string InnerHTML
    {
        get => _innerHtml ?? BuildInnerHtml();
        set => _innerHtml = value;
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
                sb.Append(' ').Append(attr.Key);
                if (!string.IsNullOrEmpty(attr.Value))
                    sb.Append("=\"").Append(System.Net.WebUtility.HtmlEncode(attr.Value)).Append('"');
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
