using Acrux.Core.Dom;

namespace Acrux.Core.Layout;

/// <summary>
/// A table's children grouped by type (captions, columns, header, bodies,
/// footer). Mirrors TableGroupedChildren in table_layout_algorithm_types.h.
/// </summary>
public class TableGroupedChildren
{
    public List<Element> Captions { get; } = new();   // CAPTION
    public List<Element> Columns { get; } = new();    // COLGROUP, COL
    public Element? Header { get; private set; }      // first THEAD
    public List<Element> Bodies { get; } = new();     // TBODY / extra THEAD/TFOOT
    public Element? Footer { get; private set; }      // first TFOOT

    public TableGroupedChildren(Element table)
    {
        foreach (var child in table.Children)
        {
            if (child is not Element el) continue;
            var display = el.ComputedStyle?.Display;

            if (display == DisplayType.TableCaption)
            {
                Captions.Add(el);
            }
            else
            {
                switch (display)
                {
                    case DisplayType.TableColumn:
                    case DisplayType.TableColumnGroup:
                        Columns.Add(el);
                        break;
                    case DisplayType.TableHeaderGroup:
                        if (Header == null) Header = el;
                        else Bodies.Add(el);
                        break;
                    case DisplayType.TableRowGroup:
                        Bodies.Add(el);
                        break;
                    case DisplayType.TableFooterGroup:
                        if (Footer == null) Footer = el;
                        else Bodies.Add(el);
                        break;
                    default:
                        // Unexpected table child; treat as implicit tbody.
                        Bodies.Add(el);
                        break;
                }
            }
        }
    }

    public TableGroupedChildrenIterator Begin() => new(this);
    public TableGroupedChildrenIterator End() => new(this, isEnd: true);
}

/// <summary>
/// Iterates a table's sections in layout order: thead, tbody, tfoot.
/// Mirrors TableGroupedChildrenIterator in table_layout_algorithm_types.h.
/// </summary>
public class TableGroupedChildrenIterator
{
    private enum CurrentSection { None, Head, Body, Foot, End }

    private readonly TableGroupedChildren _groupedChildren;
    private CurrentSection _currentSection;
    private List<Element>? _bodyVector;
    private int _position;

    public TableGroupedChildrenIterator(TableGroupedChildren groupedChildren, bool isEnd = false)
    {
        _groupedChildren = groupedChildren;
        if (isEnd)
        {
            _currentSection = CurrentSection.End;
            return;
        }
        _currentSection = CurrentSection.None;
        AdvanceForwardToNonEmptySection();
    }

    public bool TreatAsTBody => _currentSection == CurrentSection.Body;

    public Element? Current()
    {
        switch (_currentSection)
        {
            case CurrentSection.Head: return _groupedChildren.Header;
            case CurrentSection.Foot: return _groupedChildren.Footer;
            case CurrentSection.Body: return _bodyVector![_position];
            default: return null;
        }
    }

    public bool MoveNext()
    {
        switch (_currentSection)
        {
            case CurrentSection.Head:
            case CurrentSection.Foot:
                AdvanceForwardToNonEmptySection();
                break;
            case CurrentSection.Body:
                _position++;
                if (_position >= _bodyVector!.Count)
                    AdvanceForwardToNonEmptySection();
                break;
            case CurrentSection.End:
                return false;
        }
        return _currentSection != CurrentSection.End;
    }

    public IEnumerable<Element> Sections()
    {
        // Start from the beginning.
        _currentSection = CurrentSection.None;
        _bodyVector = null;
        _position = 0;
        AdvanceForwardToNonEmptySection();
        while (_currentSection != CurrentSection.End)
        {
            var cur = Current();
            if (cur != null) yield return cur;
            MoveNext();
        }
    }

    private void AdvanceForwardToNonEmptySection()
    {
        switch (_currentSection)
        {
            case CurrentSection.None:
                _currentSection = CurrentSection.Head;
                if (_groupedChildren.Header == null)
                    AdvanceForwardToNonEmptySection();
                break;
            case CurrentSection.Head:
                _currentSection = CurrentSection.Body;
                _bodyVector = _groupedChildren.Bodies;
                _position = 0;
                if (_bodyVector.Count == 0)
                    AdvanceForwardToNonEmptySection();
                break;
            case CurrentSection.Body:
                _currentSection = CurrentSection.Foot;
                if (_groupedChildren.Footer == null)
                    AdvanceForwardToNonEmptySection();
                break;
            case CurrentSection.Foot:
                _currentSection = CurrentSection.End;
                break;
        }
    }
}