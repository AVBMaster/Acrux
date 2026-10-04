using System.Collections;

namespace Acrux.Core.Dom;

public class NodeList : IReadOnlyList<Node>, IEnumerable<Node>
{
    public static readonly NodeList Empty = new();

    private readonly IReadOnlyList<Node> _items;

    public NodeList() : this(Array.Empty<Node>())
    {
    }

    public NodeList(IReadOnlyList<Node> items)
    {
        _items = items;
    }

    public virtual int Length => _items.Count;
    public virtual int Count => _items.Count;

    public virtual Node this[int index] => _items[index];

    public virtual IEnumerator<Node> GetEnumerator() => _items.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

public class LiveNodeList : NodeList
{
    private readonly Func<IReadOnlyList<Node>> _getItems;
    private readonly IReadOnlyList<Node> _fallback = Array.Empty<Node>();

    public LiveNodeList(Func<IReadOnlyList<Node>> getItems) : base(Array.Empty<Node>())
    {
        _getItems = getItems;
    }

    private IReadOnlyList<Node> GetItems() => _getItems() ?? _fallback;

    public override int Count => GetItems().Count;
    public override int Length => GetItems().Count;

    public override Node this[int index] => GetItems()[index];

    public override IEnumerator<Node> GetEnumerator() => GetItems().GetEnumerator();
}

public class StaticNodeList : NodeList
{
    public StaticNodeList(IReadOnlyList<Node> items) : base(items)
    {
    }
}
