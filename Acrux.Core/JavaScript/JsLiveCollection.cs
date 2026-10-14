using System.Collections;
using DomElement = Acrux.Core.Dom.Element;
using Node = Acrux.Core.Dom.Node;

namespace Acrux.Core.JavaScript;

public class JsHtmlCollection : IList
{
    private readonly DomElement _root;
    private readonly string? _tagName;
    private readonly string? _className;
    private readonly string? _name;
    private readonly JavaScriptEngine? _engine;

    public JsHtmlCollection(DomElement root, string? tagName = null, string? className = null, string? name = null)
    {
        _root = root;
        _tagName = tagName;
        _className = className;
        _name = name;
        _engine = JavaScriptEngine.Current;
    }

    internal JsHtmlCollection(DomElement root, JavaScriptEngine? engine, string? tagName = null, string? className = null, string? name = null)
    {
        _root = root;
        _engine = engine;
        _tagName = tagName;
        _className = className;
        _name = name;
    }

    public int length => GetFilteredElements().Count;
    public int Length => GetFilteredElements().Count;

    public object? this[int index] { get => GetItem(index); set => throw new NotSupportedException(); }

    public object? GetItem(int index)
    {
        var elements = GetFilteredElements();
        return index >= 0 && index < elements.Count ? elements[index] : null;
    }

    public object? item(int index) => GetItem(index);

    public object? NamedItem(string name)
    {
        var elements = GetFilteredElements();
        return elements.FirstOrDefault(e =>
        {
            if (e is ElementHost el)
            {
                return el.id == name || el.getAttribute("name") == name;
            }
            return false;
        });
    }

    public IEnumerator<object> GetEnumerator() => GetFilteredElements().GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public int Count => GetFilteredElements().Count;
    public bool IsReadOnly => true;
    public bool IsFixedSize => true;
    public bool IsSynchronized => false;
    public object SyncRoot => this;

    public int Add(object? value) => throw new NotSupportedException();
    public void Clear() => throw new NotSupportedException();
    public bool Contains(object? value) => GetFilteredElements().Contains(value);
    public int IndexOf(object? value) => GetFilteredElements().IndexOf(value);
    public void Insert(int index, object? value) => throw new NotSupportedException();
    public void Remove(object? value) => throw new NotSupportedException();
    public void RemoveAt(int index) => throw new NotSupportedException();
    public void CopyTo(Array array, int index) => GetFilteredElements().ToArray().CopyTo(array, index);

    public object?[] ToArray() => GetFilteredElements().ToArray();

    private List<object> GetFilteredElements()
    {
        var elements = new List<object>();
        if (_className != null)
            CollectByClassName(_root, _className, elements);
        else if (_tagName != null)
            CollectByTagName(_root, _tagName, elements);
        else if (_name != null)
            CollectByName(_root, _name, elements);
        else
            CollectAllChildren(_root, elements);
        return elements;
    }

    private object WrapDom(DomElement el)
    {
        var host = new ElementHost(el);
        if (host.Engine == null && _engine != null)
            host.Engine = _engine;
        return host;
    }

    private void CollectByTagName(DomElement root, string tagName, List<object> result)
    {
        foreach (var child in root.Children.OfType<DomElement>())
        {
            if (child.TagName.Equals(tagName, StringComparison.OrdinalIgnoreCase) ||
                tagName == "*")
                result.Add(WrapDom(child));
            CollectByTagName(child, tagName, result);
        }
    }

    private void CollectByClassName(DomElement root, string className, List<object> result)
    {
        foreach (var child in root.Children.OfType<DomElement>())
        {
            if (child.HasClass(className))
                result.Add(WrapDom(child));
            CollectByClassName(child, className, result);
        }
    }

    private void CollectByName(DomElement root, string name, List<object> result)
    {
        foreach (var child in root.Children.OfType<DomElement>())
        {
            if (child.GetAttribute("name") == name)
                result.Add(WrapDom(child));
            CollectByName(child, name, result);
        }
    }

    private void CollectAllChildren(DomElement root, List<object> result)
    {
        foreach (var child in root.Children.OfType<DomElement>())
        {
            result.Add(WrapDom(child));
            CollectAllChildren(child, result);
        }
    }
}

public class JsNodeList : IList
{
    private readonly DomElement _root;
    private readonly Func<DomElement, List<DomElement>>? _filter;
    private readonly bool _childNodes;
    private readonly JavaScriptEngine? _engine;

    public JsNodeList(DomElement root, Func<DomElement, List<DomElement>>? filter = null)
    {
        _root = root;
        _filter = filter;
        _engine = JavaScriptEngine.Current;
    }

    internal JsNodeList(DomElement root, JavaScriptEngine? engine, Func<DomElement, List<DomElement>>? filter = null)
    {
        _root = root;
        _engine = engine;
        _filter = filter;
    }

    /// <summary>A childNodes list carries every kind of node child (text and comment included),
    /// while the default and a selector query stay element-only — the two faces of the same
    /// array-like differ only by which children they collect (DOM 4 §4.3 vs §5).</summary>
    internal JsNodeList(DomElement root, JavaScriptEngine? engine, bool childNodes)
    {
        _root = root;
        _engine = engine;
        _childNodes = childNodes;
    }

    public int length => GetNodes().Count;
    public int Length => GetNodes().Count;

    public object? this[int index] { get => GetItem(index); set => throw new NotSupportedException(); }

    public object? GetItem(int index)
    {
        var nodes = GetNodes();
        return index >= 0 && index < nodes.Count ? WrapNode(nodes[index]) : null;
    }

    /// <summary>The indexed read spelled as a method: a NodeList is an indexed collection, and
    /// DOM 4 gives it <c>item()</c> as well (a live HTMLCollection keeps only the indexed read).</summary>
    public object? item(int index) => GetItem(index);

    /// <summary>A NodeList is iterable, so <c>[...nodeList]</c> and <c>for (const n of list)</c>
    /// walk the same nodes the indexed read gives.</summary>
    public IEnumerator<object> GetEnumerator()
    {
        foreach (var n in GetNodes())
            yield return WrapNode(n)!;
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public int Count => GetNodes().Count;
    public bool IsReadOnly => true;
    public bool IsFixedSize => true;
    public bool IsSynchronized => false;
    public object SyncRoot => this;

    public int Add(object? value) => throw new NotSupportedException();
    public void Clear() => throw new NotSupportedException();
    public bool Contains(object? value) => GetNodes().Any(n => WrapNode(n) == value);
    public int IndexOf(object? value) => GetNodes().Select(WrapNode).ToList().IndexOf(value);
    public void Insert(int index, object? value) => throw new NotSupportedException();
    public void Remove(object? value) => throw new NotSupportedException();
    public void RemoveAt(int index) => throw new NotSupportedException();
    public void CopyTo(Array array, int index) => GetNodes().Select(WrapNode).ToArray().CopyTo(array, index);

    public object?[] ToArray() => GetNodes().Select(WrapNode).ToArray();

    private List<Node> GetNodes()
    {
        if (_childNodes)
            return _root.Children.ToList();
        if (_filter != null)
            return _filter(_root).Cast<Node>().ToList();
        return _root.Children.OfType<DomElement>().Cast<Node>().ToList();
    }

    private object? WrapNode(Node node)
    {
        if (node is DomElement el)
        {
            var engine = JavaScriptEngine.Current ?? _engine;
            if (engine?.IntegrationService != null)
                return engine.IntegrationService.WrapDomNode(el);
            var host = new ElementHost(el);
            if (host.Engine == null && engine != null)
                host.Engine = engine;
            return host;
        }
        if (node is Acrux.Core.Dom.CommentNode c)
            return new CommentHost(c);
        return new TextNodeWrapper((Acrux.Core.Dom.TextNode)node);
    }
}
