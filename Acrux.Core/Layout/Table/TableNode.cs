using System.Collections.Generic;
using Acrux.Core.Dom;

namespace Acrux.Core.Layout.Table;

/// <summary>
/// Thin wrapper around Element for table layout. Mirrors TableNode in
/// table_node.h (layout-only surface: style, children, fixed-layout flag).
/// </summary>
public class TableNode
{
    private static readonly ComputedStyle DefaultStyle = new();

    public Element Element { get; }

    /// <summary>The table's computed style (never null; defaults on missing).</summary>
    public ComputedStyle Style => Element.ComputedStyle ?? DefaultStyle;

    /// <summary>The table's child elements.</summary>
    public List<Element> Children { get; }

    /// <summary>table-layout: fixed.</summary>
    public bool IsFixedLayout => Style.TableLayout == "fixed";

    public TableNode(Element element)
    {
        Element = element;
        Children = new List<Element>();
        foreach (var child in element.Children)
        {
            if (child is Element el)
                Children.Add(el);
        }
    }
}