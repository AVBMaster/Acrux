using Acrux.Core.Dom;
using System.Linq;

namespace Acrux.Core.Layout;

/// <summary>
/// A utility class for flex layout which iterates through children in
/// order-sorted sequence. Mirrors FlexChildIterator in flex_child_iterator.h.
/// </summary>
public class FlexChildIterator
{
    private readonly List<ChildWithOrder> _children = new();
    private int _position;

    public FlexChildIterator(Element container)
    {
        int initialOrder = ComputedStyleConstants.InitialOrder;
        bool needsSort = false;

        foreach (var child in container.Children)
        {
            if (child is not Element childEl) continue;
            int order = childEl.ComputedStyle?.Order ?? initialOrder;
            needsSort |= order != initialOrder;
            _children.Add(new ChildWithOrder(childEl, order));
        }

        if (needsSort)
        {
            _children.Sort((a, b) => a.Order.CompareTo(b.Order));
        }
    }

    public Element? NextChild()
    {
        if (_position >= _children.Count)
            return null;
        return _children[_position++].Element;
    }

    public readonly struct ChildWithOrder
    {
        public Element Element { get; }
        public int Order { get; }

        public ChildWithOrder(Element element, int order)
        {
            Element = element;
            Order = order;
        }
    }
}

/// <summary>Constants for initial values of flex/order properties.</summary>
public static class ComputedStyleConstants
{
    public const int InitialOrder = 0;
    public const int InitialBoxOrdinalGroup = 1;
}