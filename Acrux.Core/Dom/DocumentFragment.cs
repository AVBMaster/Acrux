namespace Acrux.Core.Dom;

public class DocumentFragment : Node
{
    public DocumentFragment() : base()
    {
        NodeName = "#document-fragment";
    }

    public override NodeType NodeType => NodeType.DocumentFragment;

    protected override Node CloneNodeInternal(bool deep)
    {
        var clone = new DocumentFragment();
        if (deep)
        {
            foreach (var child in ChildNodes)
                clone.AppendChild(child.CloneNode(true));
        }
        return clone;
    }
}
