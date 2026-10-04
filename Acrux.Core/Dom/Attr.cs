namespace Acrux.Core.Dom;

public class Attr : Node
{
    private string _value = string.Empty;

    internal Attr(string name, string? ns = null)
    {
        NodeName = name;
        InternalState.NamespaceUri = ns;
    }

    public override NodeType NodeType => NodeType.Attribute;
    public override string NodeName => Name;

    public string Name => NodeName;
    public string? NamespaceUri => InternalState.NamespaceUri;
    public string? Prefix => InternalState.Prefix;
    public string LocalName => Name;

    public string Value
    {
        get => _value;
        set
        {
            var old = _value;
            _value = value;
            OnAttributeValueChanged(old, value);
        }
    }

    public Element? OwnerElement { get; internal set; }
    public bool Specified => true;

    private void OnAttributeValueChanged(string? oldValue, string? newValue) { }

    protected override Node CloneNodeInternal(bool deep)
    {
        var clone = new Attr(Name, NamespaceUri);
        clone._value = _value;
        return clone;
    }
}
