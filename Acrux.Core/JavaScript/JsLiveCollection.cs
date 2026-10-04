using System.Collections;
using DomElement = Acrux.Core.Dom.Element;

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
    private List<DomElement>? _cached;
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

    public int length => GetNodes().Count;
    public int Length => GetNodes().Count;

    public object? this[int index] { get => GetItem(index); set => throw new NotSupportedException(); }

    public object? GetItem(int index)
    {
        var nodes = GetNodes();
        return index >= 0 && index < nodes.Count ? WrapNode(nodes[index]) : null;
    }

    public IEnumerator<object> GetEnumerator() => GetNodes().Select(WrapNode).GetEnumerator();

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

    private List<DomElement> GetNodes()
    {
        if (_filter != null)
            return _filter(_root);
        return _root.Children.OfType<DomElement>().ToList();
    }

    private object WrapNode(DomElement el)
    {
        var host = new ElementHost(el);
        if (host.Engine == null && _engine != null)
            host.Engine = _engine;
        return host;
    }
}
