using System.Collections;
using DomElement = Acrux.Core.Dom.Element;
using Node = Acrux.Core.Dom.Node;

namespace Acrux.Core.JavaScript;

public class JsHtmlCollection : IList, IDomPrototypeHost
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

    internal JsHtmlCollection(DomElement root, JavaScriptEngine? engine, string? tagName = null, string? className = null, string? name = null,
        string interfaceName = "HTMLCollection")
    {
        _root = root;
        _engine = engine;
        _tagName = tagName;
        _className = className;
        _name = name;
        InterfaceName = interfaceName;
    }

    /// <summary>The interface the collection reads as: the families that answer an HTMLCollection name
    /// themselves, while 'getElementsByName' answers a NodeList (measured).</summary>
    internal string InterfaceName = "HTMLCollection";

    public override string ToString() => DomHostString.Tagged(InterfaceName);

    public string? __domType => InterfaceName;
    public string[] __domTypeChain => new[] { InterfaceName };

    public int length => GetFilteredElements().Count;
    public int Length => GetFilteredElements().Count;

    public object? this[int index] { get => GetItem(index); set => throw new NotSupportedException(); }

    public object? GetItem(int index)
    {
        var elements = GetFilteredElements();
        return index >= 0 && index < elements.Count ? elements[index] : null;
    }

    public object? item(int index) => GetItem(index);

    /// <summary>HTMLCollection's named read: an id matches first, and only then a 'name' attribute
    /// of the same spelling (DOM 4 §4.5.2.3).</summary>
    public object? namedItem(string name)
    {
        var elements = GetFilteredElements();
        foreach (var e in elements)
            if (e is ElementHost el && el.id == name)
                return el;
        foreach (var e in elements)
            if (e is ElementHost el && el.getAttribute("name") == name)
                return el;
        return null;
    }

    public object?[] namedItems(string name)
    {
        var outList = new List<object>();
        foreach (var e in GetFilteredElements())
        {
            if (e is ElementHost el && (el.id == name || el.getAttribute("name") == name))
                outList.Add(el);
        }
        return outList.ToArray();
    }

    /// <summary>The named read spelled the way a live collection is indexed with a string —
    /// 'document.images["logo"]' — which is the same lookup namedItem does.</summary>
    public object? this[string name] => namedItem(name);

    public object? NamedItem(string name) => namedItem(name);

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

    /// <summary>An element reached through a collection is the same JavaScript object the rest of
    /// the page holds for it (DOM §2.8), so the collection goes through the engine's identity map
    /// rather than wrapping a fresh host of its own.</summary>
    private object WrapDom(DomElement el)
    {
        var engine = JavaScriptEngine.Current ?? _engine;
        if (engine?.IntegrationService != null)
            return engine.IntegrationService.WrapDomNode(el)!;
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
        // DOM §4.2.2: the argument is a list of names separated by space, and an element matches when it
        // carries every one of them — 'getElementsByClassName("a b")' is the intersection, not a class
        // whose name happens to hold a space (measured: two elements with both classes read as 2).
        var wanted = (className ?? "").Split(new[] { ' ', '\t', '\n', '\f', '\r' },
            StringSplitOptions.RemoveEmptyEntries);
        foreach (var child in root.Children.OfType<DomElement>())
        {
            var holds = wanted.Length > 0;
            foreach (var name in wanted)
                if (!child.HasClass(name)) { holds = false; break; }
            if (holds) result.Add(WrapDom(child));
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

public class JsNodeList : IList, IDomPrototypeHost
{
    // The root of a live list is any node: an element, a document, a fragment — the children of a
    // document (its doctype and its element) are a child list like any other (DOM §4.1).
    private readonly Node? _root;
    private readonly Func<DomElement, List<DomElement>>? _filter;
    private readonly bool _childNodes;
    private readonly JavaScriptEngine? _engine;
    // A static list holds the nodes a selector query matched at the moment it ran; it never
    // re-runs the filter, so adding or removing elements after the query does not change it
    // (DOM 4 §4.2.4: querySelectorAll returns a static NodeList).
    private readonly List<Node>? _snapshot;

    public JsNodeList(DomElement root, Func<DomElement, List<DomElement>>? filter = null)
    {
        _root = root;
        _filter = filter;
        _engine = JavaScriptEngine.Current;
    }

    /// <summary>A static NodeList holds the nodes a query matched when it ran. Unlike the live
    /// child and selector lists it never re-walks the tree, so a node added after the query does
    /// not appear in it (DOM 4 §4.2.4, measured).</summary>
    internal JsNodeList(IReadOnlyList<Node> snapshot, JavaScriptEngine? engine,
        string interfaceName = "NodeList")
    {
        _snapshot = snapshot.ToList();
        _engine = engine;
        InterfaceName = interfaceName;
    }

    /// <summary>A node that holds no children still answers 'childNodes' with a list of its own —
    /// an empty NodeList, with the same reads the family carries (measured: 'text.childNodes.length'
    /// is 0, 'text.childNodes.item(0)' is null and the list is iterable).</summary>
    internal JsNodeList(JavaScriptEngine? engine)
    {
        _root = null;
        _engine = engine;
        _childNodes = true;
    }

    internal JsNodeList(DomElement root, JavaScriptEngine? engine, Func<DomElement, List<DomElement>>? filter = null,
        string interfaceName = "NodeList")
    {
        _root = root;
        _engine = engine;
        _filter = filter;
        InterfaceName = interfaceName;
    }

    /// <summary>'children' answers an HTMLCollection and the rest of the family — 'childNodes',
    /// 'querySelectorAll' — answers a NodeList (measured).</summary>
    internal string InterfaceName = "NodeList";

    public override string ToString() => DomHostString.Tagged(InterfaceName);

    public string? __domType => InterfaceName;
    public string[] __domTypeChain => new[] { InterfaceName };

    /// <summary>A childNodes list carries every kind of node child (text and comment included),
    /// while the default and a selector query stay element-only — the two faces of the same
    /// array-like differ only by which children they collect (DOM 4 §4.3 vs §5).</summary>
    internal JsNodeList(DomElement root, JavaScriptEngine? engine, bool childNodes)
    {
        _root = root;
        _engine = engine;
        _childNodes = childNodes;
    }

    /// <summary>The child list of a node that is not an element — the document's, above all, whose
    /// children are the doctype and the document element (measured: 'document.childNodes' reads a
    /// NodeList of two, not the bare array the wrapper used to hand back).</summary>
    internal JsNodeList(Node root, JavaScriptEngine? engine)
    {
        _root = root;
        _engine = engine;
        _childNodes = true;
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
        if (_snapshot != null) return _snapshot;
        if (_root == null) return new List<Node>();
        if (_childNodes)
            return _root.Children.ToList();
        if (_root is DomElement element && _filter != null)
            return _filter(element).Cast<Node>().ToList();
        return _root.Children.OfType<DomElement>().Cast<Node>().ToList();
    }

    private object? WrapNode(Node node)
    {
        var engine = JavaScriptEngine.Current ?? _engine;
        if (engine?.IntegrationService != null)
            return engine.IntegrationService.WrapNode(node);
        return node switch
        {
            DomElement el => new ElementHost(el),
            Acrux.Core.Dom.CommentNode c => new CommentHost(c),
            Acrux.Core.Dom.TextNode t => new TextNodeWrapper(t),
            _ => null,
        };
    }
}

/// <summary>HTMLFormControlsCollection (HTML §4.10.22): the form's listed controls in tree order,
/// read live — controls come and go between two reads of 'form.elements'. Its named read answers a
/// RadioNodeList when several controls share the name, because a group of radio buttons is one
/// control the form reads and writes as a value.</summary>
public class JsFormControlsCollection : IList, IDomPrototypeHost
{
    private static readonly HashSet<string> Listed = new(StringComparer.OrdinalIgnoreCase)
    { "button", "input", "meter", "output", "progress", "select", "textarea" };

    private readonly DomElement _form;
    private readonly JavaScriptEngine? _engine;

    public JsFormControlsCollection(DomElement form, JavaScriptEngine? engine = null)
    {
        _form = form;
        _engine = engine ?? JavaScriptEngine.Current;
    }

    /// <summary>HTML §4.10.3: the form's controls answer an HTMLFormControlsCollection — the interface
    /// that adds the named read on top of an HTMLCollection (measured: the tag, the constructor name and
    /// 'instanceof HTMLCollection' all read that, while a page that builds one itself is refused).</summary>
    public string? __domType => "HTMLFormControlsCollection";
    public string[] __domTypeChain => new[] { "HTMLFormControlsCollection" };
    public override string ToString() => DomHostString.Tagged(this);

    private List<DomElement> Controls()
    {
        var outList = new List<DomElement>();
        Collect(_form, outList);
        return outList;
    }

    private static void Collect(DomElement root, List<DomElement> into)
    {
        foreach (var child in root.Children.OfType<DomElement>())
        {
            if (Listed.Contains(child.TagName)) into.Add(child);
            Collect(child, into);
        }
    }

    private object Wrap(DomElement el)
    {
        var engine = JavaScriptEngine.Current ?? _engine;
        if (engine?.IntegrationService != null)
            return engine.IntegrationService.WrapDomNode(el)!;
        var host = new ElementHost(el);
        if (host.Engine == null && engine != null) host.Engine = engine;
        return host;
    }

    public int length => Controls().Count;
    public int Length => length;

    public object? this[int index]
    {
        get
        {
            var controls = Controls();
            return index >= 0 && index < controls.Count ? Wrap(controls[index]) : null;
        }
        set => throw new NotSupportedException();
    }

    public object? item(int index) => this[index];

    /// <summary>'form.elements.namedItem(name)': the first control whose 'id' answers, then the
    /// first whose 'name' does; a group that shares one name comes back as a RadioNodeList.</summary>
    public object? namedItem(string name)
    {
        var matches = new List<DomElement>();
        foreach (var el in Controls())
            if (el.Id == name || el.GetAttribute("name") == name)
                matches.Add(el);
        if (matches.Count == 0) return null;
        if (matches.Count == 1) return Wrap(matches[0]);
        return new JsRadioNodeList(matches, _engine);
    }

    public object? NamedItem(string name) => namedItem(name);

    public object? this[string name] => namedItem(name);

    public IEnumerator<object> GetEnumerator()
    {
        foreach (var el in Controls())
            yield return Wrap(el);
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public int Count => length;
    public bool IsReadOnly => true;
    public bool IsFixedSize => true;
    public bool IsSynchronized => false;
    public object SyncRoot => this;

    public bool Contains(object? value)
    {
        foreach (var el in Controls())
            if (Equals(Wrap(el), value)) return true;
        return false;
    }

    public int IndexOf(object? value)
    {
        var controls = Controls();
        for (int i = 0; i < controls.Count; i++)
            if (Equals(Wrap(controls[i]), value)) return i;
        return -1;
    }

    public int Add(object? value) => throw new NotSupportedException();
    public void Clear() => throw new NotSupportedException();
    public void Insert(int index, object? value) => throw new NotSupportedException();
    public void Remove(object? value) => throw new NotSupportedException();
    public void RemoveAt(int index) => throw new NotSupportedException();
    public void CopyTo(Array array, int index) => ToArray().CopyTo(array, index);

    public object?[] ToArray() => Controls().Select(Wrap).ToArray();
}

/// <summary>RadioNodeList (HTML §4.10.22.4): the named group a form answers with when several
/// controls share one name. Its 'value' reads the checked control of the group and writes the
/// group, which is what makes 'form.color = "red"' reach the radio button the user picked.</summary>
public class JsRadioNodeList : IList
{
    private readonly List<DomElement> _elements;
    private readonly JavaScriptEngine? _engine;

    public JsRadioNodeList(List<DomElement> elements, JavaScriptEngine? engine = null)
    {
        _elements = elements;
        _engine = engine ?? JavaScriptEngine.Current;
    }

    private object Wrap(DomElement el)
    {
        var engine = JavaScriptEngine.Current ?? _engine;
        if (engine?.IntegrationService != null)
            return engine.IntegrationService.WrapDomNode(el)!;
        var host = new ElementHost(el);
        if (host.Engine == null && engine != null) host.Engine = engine;
        return host;
    }

    public int length => _elements.Count;
    public int Length => length;

    public object? this[int index]
    {
        get => index >= 0 && index < _elements.Count ? Wrap(_elements[index]) : null;
        set => throw new NotSupportedException();
    }

    public object? item(int index) => this[index];

    /// <summary>The group's value: the checked control answers with its value ('on' for a checkbox
    /// or radio that declares none) and nothing checked is the empty string — the form does not
    /// guess from the first control. Writing puts the value on every control that owns a 'value'
    /// attribute and moves the check to the control the value names.</summary>
    public string? value
    {
        get
        {
            foreach (var el in _elements)
            {
                if (!el.HasAttribute("checked")) continue;
                var v = el.GetAttribute("value");
                return v ?? (IsToggle(el) ? "on" : "");
            }
            return "";
        }
        set
        {
            var write = value ?? "";
            foreach (var el in _elements)
            {
                if (el.HasAttribute("value"))
                {
                    el.SetAttribute("value", write);
                    if (IsToggle(el))
                    {
                        if ((el.GetAttribute("value") ?? "") == write) el.SetAttribute("checked", "");
                        else el.RemoveAttribute("checked");
                    }
                }
                else if (IsToggle(el))
                {
                    if (write == "on") el.SetAttribute("checked", "");
                    else el.RemoveAttribute("checked");
                }
            }
        }
    }

    private static bool IsToggle(DomElement el)
        => el.TagName.Equals("INPUT", StringComparison.OrdinalIgnoreCase)
            && (el.GetAttribute("type") is "radio" or "checkbox");

    public IEnumerator<object> GetEnumerator()
    {
        foreach (var el in _elements)
            yield return Wrap(el);
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public int Count => length;
    public bool IsReadOnly => true;
    public bool IsFixedSize => true;
    public bool IsSynchronized => false;
    public object SyncRoot => this;

    public bool Contains(object? value) => IndexOf(value) >= 0;
    public int IndexOf(object? value)
    {
        for (int i = 0; i < _elements.Count; i++)
            if (Equals(Wrap(_elements[i]), value)) return i;
        return -1;
    }

    public int Add(object? value) => throw new NotSupportedException();
    public void Clear() => throw new NotSupportedException();
    public void Insert(int index, object? value) => throw new NotSupportedException();
    public void Remove(object? value) => throw new NotSupportedException();
    public void RemoveAt(int index) => throw new NotSupportedException();
    public void CopyTo(Array array, int index) => ToArray().CopyTo(array, index);

    public object?[] ToArray() => _elements.Select(Wrap).ToArray();
}

/// <summary>A live list (CSSOM §5.2.7, §6.1, Geometry Interfaces §5.2, DOM §5.1): the rules of a sheet,
/// the sheets of a document, the rectangles of a range or the nodes of a collection, read by
/// <c>length</c>, by <c>item</c> and by the index a page writes. The array behind
/// it is asked for on every read, which is what makes the list live — a rule the page inserts through
/// the sheet shows up in the list it already holds, and the wrappers inside it are the same objects
/// every other path hands out.</summary>
public class LiveListHost : IList, IDomPrototypeHost
{
    private readonly string _interfaceName;
    private readonly Func<object[]> _provider;

    public LiveListHost(string interfaceName, Func<object[]> provider)
    {
        _interfaceName = interfaceName;
        _provider = provider;
    }

    string[] IDomPrototypeHost.__domTypeChain => new[] { _interfaceName };

    public string? __domType => _interfaceName;

    public override string ToString() => DomHostString.Tagged(_interfaceName);

    public int length => Count;

    public object? item(int index) => index >= 0 && index < Count ? _provider()[index] : null;

    public object? this[int index]
    {
        get => item(index);
        set => throw new NotSupportedException();
    }

    public int Count => _provider().Length;
    public bool IsReadOnly => true;
    public bool IsFixedSize => true;
    public bool IsSynchronized => false;
    public object SyncRoot => this;
    public IEnumerator GetEnumerator() => _provider().GetEnumerator();
    public void CopyTo(Array array, int index)
    {
        var items = _provider();
        for (int i = 0; i < items.Length; i++) array.SetValue(items[i], index + i);
    }
    public int Add(object? value) => throw new NotSupportedException();
    public void Clear() => throw new NotSupportedException();
    public bool Contains(object? value) => IndexOf(value) >= 0;

    public int IndexOf(object? value)
    {
        var items = _provider();
        for (int i = 0; i < items.Length; i++)
            if (ReferenceEquals(items[i], value)) return i;
        return -1;
    }

    public void Insert(int index, object? value) => throw new NotSupportedException();
    public void Remove(object? value) => throw new NotSupportedException();
    public void RemoveAt(int index) => throw new NotSupportedException();
}

/// <summary>HTML §4.10.9: a select's 'options' answers an HTMLOptionsCollection — the option list
/// with the named read ('namedItem') and the indexed read ('item') a collection has, over the live
/// option elements (the ones inside an option group included, in tree order).</summary>
public class JsOptionsCollection : IList, IDomPrototypeHost
{
    private readonly DomElement _select;
    private readonly JavaScriptEngine? _engine;

    public JsOptionsCollection(DomElement select, JavaScriptEngine? engine = null)
    {
        _select = select;
        _engine = engine ?? JavaScriptEngine.Current;
    }

    public string? __domType => "HTMLOptionsCollection";
    public string[] __domTypeChain => new[] { "HTMLCollection", "HTMLOptionsCollection" };
    public override string ToString() => DomHostString.Tagged(this);

    private List<DomElement> Options()
    {
        var outList = new List<DomElement>();
        Collect(_select, outList);
        return outList;
    }

    private static void Collect(DomElement root, List<DomElement> into)
    {
        foreach (var child in root.Children.OfType<DomElement>())
        {
            var tag = child.TagName;
            if (tag.Equals("OPTION", StringComparison.OrdinalIgnoreCase)) into.Add(child);
            else if (tag.Equals("OPTGROUP", StringComparison.OrdinalIgnoreCase)) Collect(child, into);
        }
    }

    private object Wrap(DomElement el)
    {
        var engine = JavaScriptEngine.Current ?? _engine;
        if (engine?.IntegrationService != null)
            return engine.IntegrationService.WrapDomNode(el)!;
        var host = new ElementHost(el);
        if (host.Engine == null && engine != null) host.Engine = engine;
        return host;
    }

    public int length => Options().Count;
    public int Length => length;

    public object? this[int index]
    {
        get
        {
            var list = Options();
            return index >= 0 && index < list.Count ? Wrap(list[index]) : null;
        }
        set => throw new NotSupportedException();
    }

    public object? item(int index) => this[index];

    /// <summary>'select.options.namedItem(name)': the first option whose 'id' answers, then the
    /// first whose 'name' does; a missing or empty name answers null.</summary>
    public object? namedItem(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        foreach (var el in Options())
        {
            if (el.Id == name || el.GetAttribute("name") == name)
                return Wrap(el);
        }
        return null;
    }

    public object? NamedItem(string name) => namedItem(name);

    public object? this[string name] => namedItem(name);

    public IEnumerator<object> GetEnumerator()
    {
        foreach (var el in Options())
            yield return Wrap(el);
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public int Count => length;
    public bool IsReadOnly => true;
    public bool IsFixedSize => true;
    public bool IsSynchronized => false;
    public object SyncRoot => this;

    public bool Contains(object? value) => IndexOf(value) >= 0;

    public int IndexOf(object? value)
    {
        var list = Options();
        for (int i = 0; i < list.Count; i++)
            if (ReferenceEquals(Wrap(list[i]), value)) return i;
        return -1;
    }

    public void Insert(int index, object? value) => throw new NotSupportedException();
    public void Remove(object? value) => throw new NotSupportedException();
    public void RemoveAt(int index) => throw new NotSupportedException();
    public int Add(object? value) => throw new NotSupportedException();
    public void Clear() => throw new NotSupportedException();
    public void CopyTo(Array array, int index) => Options().Select(Wrap).ToArray().CopyTo(array, index);
}
