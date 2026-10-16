using System.Collections;
using System.Globalization;
using Acrux.Core.Dom;
using DOMException = Acrux.Core.Dom.DOMException;
using DomElement = Acrux.Core.Dom.Element;

namespace Acrux.Core.JavaScript;

/// <summary>A live, element-only list reached through one of the table interfaces (HTML §4.9.11):
/// 'table.rows', 'section.rows', 'row.cells' and 'table.tBodies'. The list is a view — it recomputes
/// from the tree on every read, so a row inserted between two reads of 'rows' appears in the second one
/// — and it answers the indexed read, 'item()' and the named read by id or 'name' the way an
/// HTMLCollection does (DOM §4.5.2). An element reached this way is the same JavaScript object the rest
/// of the page holds for it (DOM §2.8), so the collection wraps through the engine's identity map.</summary>
public class JsLiveElementList
{
    private readonly Func<List<DomElement>> _provider;

    public JsLiveElementList(Func<List<DomElement>> provider) => _provider = provider;

    private List<object> Items()
    {
        var list = new List<object>();
        foreach (var el in _provider())
            list.Add(ElementHost.WrapForCollection(el));
        return list;
    }

    public int length => _provider().Count;

    public object? this[int index]
    {
        get
        {
            var elements = _provider();
            return index >= 0 && index < elements.Count ? ElementHost.WrapForCollection(elements[index]) : null;
        }
    }

    public object? this[string name] => namedItem(name);

    public object? item(int index) => this[index];

    /// <summary>DOM §4.5.2.3: the named read answers an id first and only then a 'name' attribute.</summary>
    public object? namedItem(string? name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        var elements = _provider();
        foreach (var el in elements)
            if (string.Equals(el.GetAttribute("id"), name, StringComparison.Ordinal))
                return ElementHost.WrapForCollection(el);
        foreach (var el in elements)
            if (string.Equals(el.GetAttribute("name"), name, StringComparison.Ordinal))
                return ElementHost.WrapForCollection(el);
        return null;
    }
}

/// <summary>The table interfaces (HTML §4.9.11) on the one element host the engine shares between
/// tags. Each member is named the way its interface names it and answers for the tags that own it; a
/// tag that has no such list answers the value its own interface gives, which is why 'rows' is a
/// collection on a table or a section and a number on a text area.</summary>
public partial class ElementHost
{
    private static bool TagIs(DomElement el, string name)
        => string.Equals(el.TagName, name, StringComparison.OrdinalIgnoreCase);

    private static bool IsTableCell(DomElement el) => TagIs(el, "TD") || TagIs(el, "TH");

    /// <summary>An element reached through one of these live views is the object the page already
    /// holds for it, so the collection reads through the engine rather than wrapping anew.</summary>
    internal static object WrapForCollection(DomElement el)
    {
        var engine = JavaScriptEngine.Current;
        return engine?.IntegrationService != null
            ? engine.IntegrationService.WrapDomNode(el) ?? new ElementHost(el)
            : new ElementHost(el);
    }

    // ── the sections and their rows ──────────────────────────────────────────
    /// <summary>The table's sections in the order the rows collection walks them: the head, the bodies,
    /// the foot (HTML §4.9.11: 'rows' is every row of the table's own sections, a nested table's rows
    /// are the nested table's own — measured: 'outer.rows.length' is 1 when its cell holds a table).</summary>
    private static List<DomElement> TableSections(DomElement table)
    {
        var sections = new List<DomElement>();
        foreach (var child in table.Children.OfType<DomElement>())
            if (TagIs(child, "THEAD") || TagIs(child, "TBODY") || TagIs(child, "TFOOT"))
                sections.Add(child);
        return sections;
    }

    private static List<DomElement> SectionRows(DomElement section)
        => section.Children.OfType<DomElement>().Where(TagIsTr).ToList();

    private static bool TagIsTr(DomElement el) => TagIs(el, "TR");

    private static List<DomElement> RowCells(DomElement row)
        => row.Children.OfType<DomElement>().Where(IsTableCell).ToList();

    private DomElement? OwningTable => FindAncestor(_element, "TABLE");

    /// <summary>The rows of the table this element belongs to — for a table itself, its own rows; for a
    /// section, its rows; for a row, the rows of the table that holds it.</summary>
    public object rows
    {
        get
        {
            var tag = _element.TagName;
            if (TagIs(_element, "TEXTAREA")) return _element.GetAttribute("rows") is { Length: > 0 } r
                && int.TryParse(r, out var n) ? n : 2;
            if (IsTagSectionOrTable(tag))
                return new JsLiveElementList(() => tag.Equals("TABLE", StringComparison.OrdinalIgnoreCase)
                    ? TableRowsOf(_element)
                    : SectionRows(_element));
            return new JsLiveElementList(() => OwningTable != null ? TableRowsOf(OwningTable) : new List<DomElement>());
        }
        set
        {
            // The text area's 'rows' is a number the page writes; the collection a table answers has no
            // write to make (HTML §4.9.11, §4.10.13).
            if (TagIs(_element, "TEXTAREA"))
                _element.SetAttribute("rows", (value?.ToString() ?? "").Trim());
        }
    }

    private static bool IsTagSectionOrTable(string tag)
        => tag is "TABLE" or "THEAD" or "TBODY" or "TFOOT";

    internal static List<DomElement> TableRowsOf(DomElement table)
    {
        var rows = new List<DomElement>();
        foreach (var section in TableSections(table))
            rows.AddRange(SectionRows(section));
        return rows;
    }

    /// <summary>The table's head section — the 'thead' child, and nothing else: a thead inside a nested
    /// table belongs to that one (HTML §4.9.11). What comes back is the element the page holds for it,
    /// not a second wrapper of the same node (DOM §2.8).</summary>
    public object? tHead => WrapChild(_element, "THEAD");

    public object? tFoot => WrapChild(_element, "TFOOT");

    public object? caption => WrapChild(_element, "CAPTION");

    private static object? WrapChild(DomElement root, string tag)
    {
        var found = (root.Children.OfType<DomElement>().FirstOrDefault(el => TagIs(el, tag)));
        return found == null ? null : WrapForCollection(found);
    }

    /// <summary>The body sections, in the order they sit in the table (§4.9.11).</summary>
    public object tBodies => new JsLiveElementList(
        () => _element.Children.OfType<DomElement>().Where(el => TagIs(el, "TBODY")).ToList());

    /// <summary>HTML §4.9.11's 'rows' of a row: the cells of the row itself, td and th only.</summary>
    public object cells
    {
        get
        {
            if (TagIs(_element, "TR"))
                return new JsLiveElementList(() => RowCells(_element));
            return new JsLiveElementList(() => new List<DomElement>());
        }
    }

    private object? FirstChildOfKind(DomElement root, string tag)
        => root.Children.OfType<DomElement>().FirstOrDefault(el => TagIs(el, tag));

    // ── the indexes ──────────────────────────────────────────────────────────
    /// <summary>The row's place in the rows of the table that holds it (§4.9.11). A cell has no row
    /// index to answer, and the reference says so by not carrying the member at all (measured:
    /// 'td.rowIndex' is undefined).</summary>
    public int? rowIndex
    {
        get
        {
            if (!TagIs(_element, "TR")) return null;
            var table = OwningTable;
            if (table == null) return null;
            var rows = TableRowsOf(table);
            var at = rows.IndexOf(_element);
            return at >= 0 ? at : (int?)null;
        }
    }

    /// <summary>The row's place among the rows of the section it sits in (§4.9.11).</summary>
    public int? sectionRowIndex
    {
        get
        {
            if (!TagIs(_element, "TR")) return null;
            var section = _element.ParentElement;
            if (section == null || !IsTagSectionOrTable(section.TagName) || TagIs(section, "TABLE")) return null;
            var at = SectionRows(section).IndexOf(_element);
            return at >= 0 ? at : (int?)null;
        }
    }

    /// <summary>The cell's place among the cells of its row (§4.9.11).</summary>
    public int cellIndex
    {
        get
        {
            var row = _element.ParentElement;
            if (row == null || !TagIs(row, "TR")) return 0;
            var at = RowCells(row).IndexOf(_element);
            return at >= 0 ? at : 0;
        }
    }

    // ── the insert and delete members ────────────────────────────────────────
    /// <summary>HTML §4.9.11: a row is put in at the index the page gives, or at the end when the index
    /// is left out or is -1; an index outside [-1, rows] is refused with the sentence the reference
    /// writes, which names the interface the member was called on. Called on a table, the row goes into
    /// its last body section — and a table without one grows a body first.</summary>
    public object insertRow(object? index = null)
    {
        var at = ToIndex(index, -1);
        if (TagIs(_element, "TABLE"))
        {
            var bodies = _element.Children.OfType<DomElement>().Where(el => TagIs(el, "TBODY")).ToList();
            DomElement section;
            if (bodies.Count > 0) section = bodies[bodies.Count - 1];
            else
            {
                section = NewElement("tbody");
                _element.AppendChild(section);
            }
            return InsertRowInto(section, at, "HTMLTableElement", TableRowsOf(_element).Count);
        }
        return InsertRowInto(_element, at, "HTMLTableSectionElement", SectionRows(_element).Count);
    }

    private object InsertRowInto(DomElement section, int at, string owner, int count)
    {
        RequireRowColumnIndex(at, count, "insertRow", owner);
        var row = NewElement("tr");
        if (at < 0) section.AppendChild(row);
        else section.InsertBefore(row, SectionRows(section).ElementAtOrDefault(at));
        return WrapForCollection(row);
    }

    /// <summary>HTML §4.9.11: the row at the index leaves the section; an index outside [0, rows) or a
    /// negative one other than nothing at all is refused (§4.9.11's delete step).</summary>
    public void deleteRow(object? index = null)
    {
        var at = ToIndex(index, -1);
        var rows = TagIs(_element, "TABLE") ? TableRowsOf(_element) : SectionRows(_element);
        var owner = TagIs(_element, "TABLE") ? "HTMLTableElement" : "HTMLTableSectionElement";
        if (at < 0 || at >= rows.Count)
            throw new DOMException($"Failed to execute 'deleteRow' on '{owner}': The provided index "
                                   + $"({at} is outside the range [-1, {rows.Count}].", "IndexSizeError");
        rows[at].Remove();
    }

    /// <summary>HTML §4.9.11: a cell is put in at the index the row is given or at its end, and the
    /// index is refused outside [-1, cells] — in the row's own sentence (measured: 'The value provided
    /// (9) is outside the range [-1, 2].').</summary>
    public object insertCell(object? index = null)
    {
        var at = ToIndex(index, -1);
        var cells = RowCells(_element);
        RequireRowColumnIndex(at, cells.Count, "insertCell", "HTMLTableRowElement", "The value provided");
        var cell = NewElement("td");
        if (at < 0) _element.AppendChild(cell);
        else _element.InsertBefore(cell, cells.ElementAtOrDefault(at));
        return WrapForCollection(cell);
    }

    public void deleteCell(object? index = null)
    {
        var at = ToIndex(index, -1);
        var cells = RowCells(_element);
        if (at < 0 || at >= cells.Count)
            throw new DOMException($"Failed to execute 'deleteCell' on 'HTMLTableRowElement': The "
                                   + $"provided index ({at} is outside the range [-1, {cells.Count}].",
                                   "IndexSizeError");
        cells[at].Remove();
    }

    private static void RequireRowColumnIndex(int at, int count, string method, string owner,
        string phrase = "The provided index")
    {
        if (at >= -1 && at <= count) return;
        throw new DOMException($"Failed to execute '{method}' on '{owner}': {phrase} "
                               + $"({at} is outside the range [-1, {count}].", "IndexSizeError");
    }

    /// <summary>HTML §4.9.11: the head, foot and caption the table does not have yet are made and put
    /// where the interface says — the head before the first body or foot, the foot after the last body,
    /// the caption first among the table's children (measured: creating all three leaves
    /// 'caption, thead, tfoot'). Creating one the table already has answers that element.</summary>
    public object createTHead()
    {
        if (FirstChildOfKind(_element, "THEAD") is DomElement existing) return WrapForCollection(existing);
        var head = NewElement("thead");
        var before = _element.Children.OfType<DomElement>()
            .FirstOrDefault(el => TagIs(el, "TBODY") || TagIs(el, "TFOOT"));
        if (before != null) _element.InsertBefore(head, before);
        else _element.AppendChild(head);
        return WrapForCollection(head);
    }

    public object createTFoot()
    {
        if (FirstChildOfKind(_element, "TFOOT") is DomElement existing) return WrapForCollection(existing);
        var foot = NewElement("tfoot");
        _element.AppendChild(foot);
        return WrapForCollection(foot);
    }

    public object createCaption()
    {
        if (FirstChildOfKind(_element, "CAPTION") is DomElement existing) return WrapForCollection(existing);
        var caption = NewElement("caption");
        _element.InsertBefore(caption, _element.Children.FirstOrDefault());
        return WrapForCollection(caption);
    }
    public void deleteTHead() { (FirstChildOfKind(_element, "THEAD") as DomElement)?.Remove(); }
    public void deleteTFoot() { (FirstChildOfKind(_element, "TFOOT") as DomElement)?.Remove(); }
    public void deleteCaption() { (FirstChildOfKind(_element, "CAPTION") as DomElement)?.Remove(); }

    // ── the cell and column reflections ──────────────────────────────────────
    /// <summary>HTML §4.9.11: 'colSpan' and 'rowSpan' are the attributes read as integers, and both
    /// answer at least 1 when the attribute is missing or not a number.</summary>
    public int colSpan
    {
        get => ReflectSpan("colspan");
        set => _element.SetAttribute("colspan", (value < 1 ? 1 : value).ToString(
            System.Globalization.CultureInfo.InvariantCulture));
    }

    public int rowSpan
    {
        get => ReflectSpan("rowspan");
        set => _element.SetAttribute("rowspan", value.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private int ReflectSpan(string attribute)
    {
        // HTML §4.9.11: both spans answer 1 when the attribute is missing or is not a number, because
        // a cell always covers at least one row and one column.
        var text = _element.GetAttribute(attribute);
        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed >= 1)
            return parsed;
        return 1;
    }

    /// <summary>HTML §4.9.11: 'headers' is the list of the header cells that label this one.</summary>
    public object headers => new DomTokenListHostByAttribute(this, "headers");

    public string scope
    {
        get => _element.GetAttribute("scope") ?? "";
        set => _element.SetAttribute("scope", value ?? "");
    }

    public string abbr
    {
        get => _element.GetAttribute("abbr") ?? "";
        set => _element.SetAttribute("abbr", value ?? "");
    }

    /// <summary>HTML §4.9.8: the column's 'span', which defaults to 1.</summary>
    public int span
    {
        get
        {
            var text = _element.GetAttribute("span");
            return int.TryParse(text, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var parsed) && parsed >= 1 ? parsed : 1;
        }
        set => _element.SetAttribute("span", (value < 1 ? 1 : value)
            .ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private DomElement NewElement(string tag)
    {
        var owner = _element.OwnerDocument;
        DomElement made = owner != null ? owner.CreateElement(tag)
            : Acrux.Core.Dom.Html.HtmlElementFactory.Create(tag) ?? new HtmlElement(tag);
        return made;
    }

    private static DomElement? FindAncestor(DomElement? from, string tag)
    {
        for (var n = from?.Parent; n is DomElement el; n = n.Parent)
            if (TagIs(el, tag)) return el;
        return null;
    }

    private static int ToIndex(object? value, int fallback)
    {
        if (value == null) return fallback;
        var text = value.ToString();
        if (string.IsNullOrEmpty(text)) return fallback;
        return int.TryParse(text, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var parsed) ? parsed : fallback;
    }

    // ── the namespace axis (DOM §5.4) ────────────────────────────────────────
    internal const string XmlNamespace = "http://www.w3.org/XML/1998/namespace";
    internal const string XmlnsNamespace = "http://www.w3.org/2000/xmlns/";
    internal const string HtmlNamespace = "http://www.w3.org/1999/xhtml";

    /// <summary>The namespace a prefix names for this node, read from the declarations the node and its
    /// ancestors carry: 'xmlns:p="…"' gives a prefix and a bare 'xmlns="…"' the default namespace, and
    /// the closest one wins because the walk stops at the first hit (DOM §5.4). The two prefixes the
    /// XML specification itself reserves answer without any declaration, and a name nothing declared
    /// answers null.</summary>
    public string? lookupNamespaceURI(string? prefix)
        => NamespaceAxis.LookupNamespace(_element, prefix);

    /// <summary>The prefix this node would use for a namespace: its own when the node is in it, and
    /// otherwise the one a declaration in its ancestry gave that namespace. The HTML namespace is never
    /// returned — a document written in HTML has no prefix for it (DOM §5.4).</summary>
    public string? lookupPrefix(string? ns) => NamespaceAxis.LookupPrefix(_element, ns);

    public bool isDefaultNamespace(string? ns) => NamespaceAxis.IsDefaultNamespace(_element, ns);
}

/// <summary>DOM §5.4's three namespace helpers, written once so the element, the document and an
/// attribute all answer them the same way. The declarations an XML tree carries are attributes named
/// 'xmlns' and 'xmlns:prefix', and the walk up from the node is what gives an inner element the
/// namespace an ancestor declared for it (measured: an element created in a prefix's namespace reads
/// 'urn:x' back from 'lookupNamespaceURI("x")' and gives that namespace its 'x' prefix).</summary>
internal static class NamespaceAxis
{
    internal static string? LookupNamespace(DomElement? from, string? prefix)
    {
        var wanted = prefix ?? "";
        if (wanted == "xml") return ElementHost.XmlNamespace;
        if (wanted == "xmlns") return ElementHost.XmlnsNamespace;
        if (wanted.Length == 0) return DefaultNamespace(from);
        for (var node = from; node != null; node = node.ParentElement)
        {
            var declared = DeclarationFor(node, wanted);
            if (declared != null) return declared;
        }
        return null;
    }

    /// <summary>The default namespace in force at a node: the last bare 'xmlns' the ancestry declares,
    /// and an element the HTML parser made carries none.</summary>
    private static string? DefaultNamespace(DomElement? from)
    {
        for (var node = from; node != null; node = node.ParentElement)
        {
            foreach (var attr in node.Attributes)
            {
                if (!string.Equals(attr.Key, "xmlns", StringComparison.Ordinal)) continue;
                var value = attr.Value;
                return string.IsNullOrEmpty(value) ? null : value;
            }
        }
        return null;
    }

    private static string? DeclarationFor(DomElement node, string prefix)
    {
        foreach (var attr in node.Attributes)
        {
            var name = attr.Key;
            if (name.Length > 6 && name.StartsWith("xmlns:", StringComparison.Ordinal)
                && string.Equals(name.Substring(6), prefix, StringComparison.Ordinal))
                return attr.Value;
        }
        return null;
    }

    internal static string? LookupPrefix(DomElement? from, string? ns)
    {
        if (string.IsNullOrEmpty(ns)) return null;
        if (ns == ElementHost.XmlNamespace) return "xml";
        if (ns == ElementHost.XmlnsNamespace) return "xmlns";
        // The HTML namespace is the one a document written in HTML carries without ever naming a prefix
        // for it, so the interface has nothing to answer (DOM §5.4).
        if (ns == ElementHost.HtmlNamespace) return null;
        for (var node = from; node != null; node = node.ParentElement)
        {
            if (string.Equals(node.NamespaceUri, ns, StringComparison.Ordinal))
                return string.IsNullOrEmpty(node.Prefix) ? "" : node.Prefix;
            foreach (var attr in node.Attributes)
            {
                var name = attr.Key;
                if (string.Equals(attr.Value, ns, StringComparison.Ordinal))
                {
                    if (name.Length > 6 && name.StartsWith("xmlns:", StringComparison.Ordinal))
                        return name.Substring(6);
                    if (string.Equals(name, "xmlns", StringComparison.Ordinal)) return "";
                }
            }
        }
        return null;
    }

    internal static bool IsDefaultNamespace(DomElement? from, string? ns)
    {
        var nullNamespace = DefaultNamespace(from);
        if (string.Equals(nullNamespace, ns, StringComparison.Ordinal)) return true;
        if (ns == null && nullNamespace == ElementHost.HtmlNamespace) return true;
        if (ns == ElementHost.HtmlNamespace && nullNamespace == null) return true;
        return false;
    }
}

/// <summary>A DOMTokenList over an attribute other than 'class' (DOM §3.3.6): the 'headers' list of a
/// cell and the 'htmlFor' list of a label or output read the tokens in the attribute the same way,
/// serialise back into it, and share the token checks the class list already makes.</summary>
public class DomTokenListHostByAttribute : IDomPrototypeHost
{
    private readonly ElementHost _element;
    private readonly string _attribute;

    public DomTokenListHostByAttribute(ElementHost element, string attribute)
    {
        _element = element;
        _attribute = attribute;
    }

    string[] Acrux.Core.JavaScript.IDomPrototypeHost.__domTypeChain => new[] { "DOMTokenList" };

    private List<string> Tokens() => (_element.NativeElement.GetAttribute(_attribute) ?? "")
        .Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();

    public int length => Tokens().Count;
    public string? this[int index]
    {
        get
        {
            var tokens = Tokens();
            return index >= 0 && index < tokens.Count ? tokens[index] : null;
        }
    }
    public string? item(int index) => this[index];
    public bool contains(string token) => Tokens().Contains(token);
    public void add(params string[] tokens)
    {
        foreach (var token in tokens) DomTokens.Require(token, "add");
        var held = Tokens();
        foreach (var token in tokens) if (!held.Contains(token)) held.Add(token);
        _element.NativeElement.SetAttribute(_attribute, string.Join(" ", held));
    }
    public void remove(params string[] tokens)
    {
        foreach (var token in tokens) DomTokens.Require(token, "remove");
        var held = Tokens();
        foreach (var token in tokens) held.Remove(token);
        _element.NativeElement.SetAttribute(_attribute, string.Join(" ", held));
    }
    public bool toggle(string token)
    {
        DomTokens.Require(token, "toggle");
        var held = Tokens();
        if (held.Contains(token)) { held.Remove(token); _element.NativeElement.SetAttribute(_attribute, string.Join(" ", held)); return false; }
        held.Add(token); _element.NativeElement.SetAttribute(_attribute, string.Join(" ", held)); return true;
    }
    public bool replace(string token, string newToken)
    {
        DomTokens.Require(token, "replace");
        DomTokens.Require(newToken, "replace");
        var held = Tokens();
        if (!held.Contains(token)) return false;
        held.Remove(token);
        if (!held.Contains(newToken)) held.Add(newToken);
        _element.NativeElement.SetAttribute(_attribute, string.Join(" ", held));
        return true;
    }
    public string value
    {
        get => _element.NativeElement.GetAttribute(_attribute) ?? "";
        set => _element.NativeElement.SetAttribute(_attribute, value ?? "");
    }
    public override string ToString() => value;
}
