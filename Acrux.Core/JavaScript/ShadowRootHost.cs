using DomShadowRoot = Acrux.Core.Dom.ShadowRoot;

namespace Acrux.Core.JavaScript;

/// <summary>The shadow tree a page opened with 'attachShadow' (DOM §4.3.7, HTML §4.3.7.2). It is a
/// document fragment that happens to have a host, so everything the fragment answers — its children,
/// 'querySelector', 'append', 'innerHTML' — is read through that host, and this adds the members the
/// shadow root names: the element it belongs to, the mode the page asked for, the sheets the root
/// adopted and the flags the init dictionary carried.
/// <para>
/// What the engine does not do yet is keep the root's styles from reaching the rest of the page, which
/// is the shadow host's other half; the tree itself is separate, and that separation is what the page
/// measures through 'host.querySelector' finding nothing and 'root.querySelector' finding its own.</para>
/// </summary>
public class ShadowRootHost : DocumentFragmentHost
{
    private readonly DomShadowRoot _root;

    public ShadowRootHost(DomShadowRoot root) : base(root) => _root = root;

    public override string[] __domTypeChain =>
        new[] { "EventTarget", "Node", "DocumentFragment", "ShadowRoot" };
    public string? __domType => "ShadowRoot";

    /// <summary>DOM §4.3.7: the element whose shadow tree this is. The page holds the very object it has
    /// for that element, so 'root.host === host' holds.</summary>
    public object? host => JsIntegrationService.WrapAny(_root.Host);

    /// <summary>HTML §4.3.7.2: the mode the init dictionary asked for, read back as it was given.</summary>
    public string mode => _root.Mode == Acrux.Core.Dom.ShadowMode.Closed ? "closed" : "open";

    public bool delegatesFocus => _root.DelegatesFocus;
    public bool clonable => _root.Clonable;
    public bool serializable => _root.Serializable;
    public string slotAssignment => _root.SlotAssignment == Acrux.Core.Dom.SlotAssignmentMode.Manual
        ? "manual" : "named";

    /// <summary>HTML §13.2.5.114: the markup written into a shadow root is parsed in the context of the
    /// host's tag — a 'tr' written into a root whose host is a 'div' is dropped the way the parser
    /// drops it for a div, not kept the way it would be inside a table.</summary>
    internal override string InnerHtmlContextTag => _root.Host.TagName;

    /// <summary>DOM §4.3.7: the style sheets the root adopted. The engine keeps its cascade per
    /// document, so this is the list the page writes and reads while those sheets are not yet applied
    /// through it.</summary>
    public object[] adoptedStyleSheets
    {
        get => _adopted.ToArray();
        set
        {
            _adopted.Clear();
            if (value == null) return;
            foreach (var sheet in value) _adopted.Add(sheet);
        }
    }

    private readonly List<object> _adopted = new();

    /// <summary>DOM §4.3.7: the element inside the shadow tree that has the focus. Nothing is focused
    /// until the shell says so, and then it is the document's own answer.</summary>
    public object? activeElement => null;

    public object? styleSheets => Array.Empty<object>();

    /// <summary>DOM §4.2: the root holds the tree below the host, so it answers for the nodes inside it
    /// and for nothing in the light tree.</summary>
    public bool contains(object? other)
    {
        var node = ElementHost.UnwrapNode(other);
        if (node == null) return false;
        for (var current = node; current != null; current = current.Parent)
            if (ReferenceEquals(current, _root)) return true;
        return false;
    }

    /// <summary>DOM §4.2.1: the root is the root of its own tree — a node inside it reports it from
    /// 'getRootNode', and the tree is connected only when the host is.</summary>
    public object? getRootNode() => this;

    /// <summary>DOM §4.3.7: 'getSelection' is a member the root shares with the document, and a shadow
    /// tree with no selection of its own answers null.</summary>
    public object? getSelection() => null;
}
