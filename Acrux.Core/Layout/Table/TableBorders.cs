using Acrux.Core.Dom;
using Acrux.Core.Layout.Geometry;

namespace Acrux.Core.Layout.Table;

public class TableBorders
{
    public enum EdgeSource { kNone, kCell, kRow, kSection, kColumn, kTable }

    public enum EdgeSide { kTop, kBottom, kLeft, kRight, kDoNotFill }

    public struct Edge
    {
        public ComputedStyle? Style;
        public EdgeSide Side;
        public int BoxOrder;
    }

    public struct Section
    {
        public int StartRow;
        public int RowCount;
    }

    public static float BorderWidth(ComputedStyle? style, EdgeSide edgeSide)
    {
        if (style == null) return 0;
        return edgeSide switch
        {
            EdgeSide.kLeft => style.BorderLeftWidth,
            EdgeSide.kRight => style.BorderRightWidth,
            EdgeSide.kTop => style.BorderTopWidth,
            EdgeSide.kBottom => style.BorderBottomWidth,
            EdgeSide.kDoNotFill => 0,
            _ => 0
        };
    }

    public static BorderStyle BorderStyleValue(ComputedStyle? style, EdgeSide edgeSide)
    {
        if (style == null) return BorderStyle.None;
        var borderStyle = edgeSide switch
        {
            EdgeSide.kLeft => style.BorderLeftStyle,
            EdgeSide.kRight => style.BorderRightStyle,
            EdgeSide.kTop => style.BorderTopStyle,
            EdgeSide.kBottom => style.BorderBottomStyle,
            EdgeSide.kDoNotFill => BorderStyle.None,
            _ => BorderStyle.None
        };
        return CollapsedBorderStyle(borderStyle);
    }

    public static BorderStyle CollapsedBorderStyle(BorderStyle style)
    {
        return style switch
        {
            BorderStyle.Outset => BorderStyle.Groove,
            BorderStyle.Inset => BorderStyle.Ridge,
            _ => style
        };
    }

    public static bool HasBorder(ComputedStyle? style)
    {
        if (style == null) return false;
        return style.BorderLeftStyle != BorderStyle.None ||
               style.BorderRightStyle != BorderStyle.None ||
               style.BorderTopStyle != BorderStyle.None ||
               style.BorderBottomStyle != BorderStyle.None;
    }

    public static bool IsFixedTableLayout(ComputedStyle style) =>
        style.TableLayout == "fixed";

    private readonly List<Edge> _edges = new();
    private readonly List<Section> _sections = new();
    private int _edgesPerRow;
    private BoxStrut _tableBorder;
    private int _lastColumnIndex = int.MaxValue;
    private readonly bool _isCollapsed;

    public bool IsEmpty => _edges.Count == 0;
    public bool IsCollapsed => _isCollapsed;
    public int EdgesPerRow => _edgesPerRow;
    public BoxStrut TableBorderValue => _tableBorder;
    public int EdgeCount => _edges.Count;

    public IReadOnlyList<Edge> Edges => _edges;

    public TableBorders(BoxStrut tableBorder, bool isCollapsed)
    {
        _tableBorder = tableBorder;
        _isCollapsed = isCollapsed;
    }

    public float BorderWidth(int edgeIndex) =>
        BorderWidth(_edges[edgeIndex].Style, _edges[edgeIndex].Side);

    public BorderStyle EdgeBorderStyle(int edgeIndex) =>
        BorderStyleValue(_edges[edgeIndex].Style, _edges[edgeIndex].Side);

    public int BoxOrder(int edgeIndex) =>
        _edges[edgeIndex].BoxOrder;

    public bool CanPaint(int edgeIndex)
    {
        if (!HasEdgeAtIndex(edgeIndex)) return false;
        var borderStyle = EdgeBorderStyle(edgeIndex);
        if (borderStyle == BorderStyle.None) return false;
        if (BorderWidth(edgeIndex) == 0) return false;
        return true;
    }

    public bool HasEdgeAtIndex(int edgeIndex) =>
        edgeIndex < _edges.Count && _edges[edgeIndex].Style != null;

    public bool CanPaint(int edgeIndex, int indexOffset)
    {
        if (indexOffset < 0 && edgeIndex < (uint)Math.Abs(indexOffset))
            return false;
        int targetIndex = edgeIndex + indexOffset;
        return targetIndex < _edges.Count && _edges[targetIndex].Style != null;
    }

    public void AddSection(int startRow, int rowCount) =>
        _sections.Add(new Section { StartRow = startRow, RowCount = rowCount });

    public Section GetSection(int sectionIndex) =>
        _sections[sectionIndex];

    public void SetLastColumnIndex(int lastColumnIndex) =>
        _lastColumnIndex = lastColumnIndex;

    private int ClampColspan(int column, int colspan)
    {
        return Math.Min(colspan, _lastColumnIndex - column);
    }

    private int ClampRowspan(int sectionIndex, int tableRowIndex, int rowspan)
    {
        if (rowspan <= 1) return rowspan;
        var section = _sections[sectionIndex];
        return Math.Min(rowspan, section.RowCount - (tableRowIndex - section.StartRow));
    }

    /// <summary>Index of the section a table-level row index falls into.</summary>
    public int SectionIndexOf(int tableRow)
    {
        for (int i = 0; i < _sections.Count; i++)
        {
            if (tableRow >= _sections[i].StartRow && tableRow < _sections[i].StartRow + _sections[i].RowCount)
                return i;
        }
        return 0;
    }

    public BoxStrut CellBorder(int row, int column, int rowspan, int colspan, int sectionIndex)
    {
        if (_isCollapsed)
            return GetCellBorders(row, column, ClampRowspan(sectionIndex, row, rowspan), ClampColspan(column, colspan));
        return _tableBorder;
    }

    public BoxStrut GetCellBorders(int row, int column, int rowspan, int colspan)
    {
        var borderStrut = BoxStrut.Zero;
        if (_edgesPerRow == 0) return borderStrut;
        if (column * 2 >= _edgesPerRow || row >= _edges.Count / _edgesPerRow)
            return borderStrut;

        int firstInlineStartEdge = row * _edgesPerRow + column * 2;
        int firstInlineEndEdge = firstInlineStartEdge + colspan * 2;
        for (int i = 0; i < rowspan; i++)
        {
            int startEdgeIndex = firstInlineStartEdge + i * _edgesPerRow;
            borderStrut = new BoxStrut(
                borderStrut.Top,
                borderStrut.Right,
                borderStrut.Bottom,
                Math.Max(borderStrut.Left, CanPaint(startEdgeIndex) ? BorderWidth(startEdgeIndex) : 0));
            if (startEdgeIndex >= _edges.Count) break;
            int endEdgeIndex = firstInlineEndEdge + i * _edgesPerRow;
            borderStrut = new BoxStrut(
                borderStrut.Top,
                Math.Max(borderStrut.Right, CanPaint(endEdgeIndex) ? BorderWidth(endEdgeIndex) : 0),
                borderStrut.Bottom,
                borderStrut.Left);
        }

        int startEdgeColumnIndex = column * 2 + 1;
        for (int i = 0; i < colspan; i++)
        {
            int currentColumnIndex = startEdgeColumnIndex + i * 2;
            if (currentColumnIndex >= _edgesPerRow) break;
            int startEdgeIndex = row * _edgesPerRow + currentColumnIndex;
            borderStrut = new BoxStrut(
                Math.Max(borderStrut.Top, CanPaint(startEdgeIndex) ? BorderWidth(startEdgeIndex) : 0),
                borderStrut.Right,
                borderStrut.Bottom,
                borderStrut.Left);
            int endEdgeIndex = startEdgeIndex + rowspan * _edgesPerRow;
            borderStrut = new BoxStrut(
                borderStrut.Top,
                borderStrut.Right,
                Math.Max(borderStrut.Bottom, CanPaint(endEdgeIndex) ? BorderWidth(endEdgeIndex) : 0),
                borderStrut.Left);
        }

        borderStrut = new BoxStrut(
            borderStrut.Top / 2,
            borderStrut.Right / 2,
            borderStrut.Bottom / 2,
            borderStrut.Left / 2);
        return borderStrut;
    }

    public void UpdateTableBorder(int tableRowCount, int tableColumnCount)
    {
        if (_edgesPerRow == 0)
        {
            _tableBorder = BoxStrut.Zero;
            return;
        }
        _tableBorder = GetCellBorders(0, 0, tableRowCount, tableColumnCount);
    }

    public void MergeBorders(int cellStartRow, int cellStartColumn, int rowspan, int colspan,
        ComputedStyle sourceStyle, EdgeSource source, int boxOrder, int sectionIndex = -1)
    {
        if (rowspan == 0 || colspan == 0) return;

        int clampedColspan = ClampColspan(cellStartColumn, colspan);
        int clampedRowspan = source == EdgeSource.kCell
            ? ClampRowspan(sectionIndex, cellStartRow, rowspan)
            : rowspan;
        bool markInnerBorders = source == EdgeSource.kCell && (clampedRowspan > 1 || clampedColspan > 1);

        if (markInnerBorders)
        {
            EnsureCellColumnFits(cellStartColumn + clampedColspan - 1);
            EnsureCellRowFits(cellStartRow + clampedRowspan - 1);
        }
        else
        {
            if (sourceStyle.BorderTopStyle == BorderStyle.None &&
                sourceStyle.BorderRightStyle == BorderStyle.None &&
                sourceStyle.BorderBottomStyle == BorderStyle.None &&
                sourceStyle.BorderLeftStyle == BorderStyle.None)
                return;

            if (sourceStyle.BorderRightStyle == BorderStyle.None &&
                sourceStyle.BorderTopStyle == BorderStyle.None &&
                sourceStyle.BorderBottomStyle == BorderStyle.None)
                EnsureCellColumnFits(cellStartColumn);
            else
                EnsureCellColumnFits(cellStartColumn + clampedColspan - 1);

            if (sourceStyle.BorderLeftStyle == BorderStyle.None &&
                sourceStyle.BorderRightStyle == BorderStyle.None &&
                sourceStyle.BorderBottomStyle == BorderStyle.None)
                EnsureCellRowFits(cellStartRow);
            else
                EnsureCellRowFits(cellStartRow + clampedRowspan - 1);
        }

        MergeRowAxisBorder(cellStartRow, cellStartColumn, clampedColspan, sourceStyle, boxOrder, EdgeSide.kTop);
        MergeRowAxisBorder(cellStartRow + clampedRowspan, cellStartColumn, clampedColspan, sourceStyle, boxOrder, EdgeSide.kBottom);
        MergeColumnAxisBorder(cellStartRow, cellStartColumn, clampedRowspan, sourceStyle, boxOrder, EdgeSide.kLeft);
        MergeColumnAxisBorder(cellStartRow, cellStartColumn + clampedColspan, clampedRowspan, sourceStyle, boxOrder, EdgeSide.kRight);

        if (markInnerBorders)
            MarkInnerBordersAsDoNotFill(cellStartRow, cellStartColumn, clampedRowspan, clampedColspan);
    }

    private void MergeRowAxisBorder(int startRow, int startColumn, int colspan,
        ComputedStyle sourceStyle, int boxOrder, EdgeSide physicalSide)
    {
        var sourceBorderStyle = BorderStyleValue(sourceStyle, physicalSide);
        if (sourceBorderStyle == BorderStyle.None) return;
        float sourceBorderWidth = BorderWidth(sourceStyle, physicalSide);

        int startEdge = _edgesPerRow * startRow + startColumn * 2 + 1;
        int endEdge = startEdge + colspan * 2;
        for (int currentEdge = startEdge; currentEdge < endEdge; currentEdge += 2)
        {
            if (IsSourceMoreSpecificThanEdge(sourceBorderStyle, sourceBorderWidth, _edges[currentEdge]))
            {
                _edges[currentEdge] = new Edge
                {
                    Style = sourceStyle,
                    Side = physicalSide,
                    BoxOrder = boxOrder
                };
            }
        }
    }

    private void MergeColumnAxisBorder(int startRow, int startColumn, int rowspan,
        ComputedStyle sourceStyle, int boxOrder, EdgeSide physicalSide)
    {
        var sourceBorderStyle = BorderStyleValue(sourceStyle, physicalSide);
        if (sourceBorderStyle == BorderStyle.None) return;
        float sourceBorderWidth = BorderWidth(sourceStyle, physicalSide);

        int startEdge = _edgesPerRow * startRow + startColumn * 2;
        int endEdge = startEdge + (rowspan * _edgesPerRow);
        for (int currentEdge = startEdge; currentEdge < endEdge; currentEdge += _edgesPerRow)
        {
            if (IsSourceMoreSpecificThanEdge(sourceBorderStyle, sourceBorderWidth, _edges[currentEdge]))
            {
                _edges[currentEdge] = new Edge
                {
                    Style = sourceStyle,
                    Side = physicalSide,
                    BoxOrder = boxOrder
                };
            }
        }
    }

    private void MarkInnerBordersAsDoNotFill(int startRow, int startColumn, int rowspan, int colspan)
    {
        int startEdge = (startColumn * 2) + 2;
        int endEdge = startEdge + (colspan - 1) * 2;
        for (int row = startRow; row < startRow + rowspan && startEdge != endEdge; row++)
        {
            int rowOffset = row * _edgesPerRow;
            for (int edge = rowOffset + startEdge; edge < rowOffset + endEdge; edge += 2)
            {
                if (edge < _edges.Count && _edges[edge].Style == null)
                    _edges[edge] = new Edge { Style = null, Side = EdgeSide.kDoNotFill, BoxOrder = 0 };
            }
        }

        startEdge = startColumn * 2 + 1;
        endEdge = startEdge + colspan * 2;
        for (int row = startRow + 1; row < startRow + rowspan; row++)
        {
            int rowOffset = row * _edgesPerRow;
            for (int edge = rowOffset + startEdge; edge < rowOffset + endEdge; edge += 2)
            {
                if (edge < _edges.Count && _edges[edge].Style == null)
                    _edges[edge] = new Edge { Style = null, Side = EdgeSide.kDoNotFill, BoxOrder = 0 };
            }
        }
    }

    private void EnsureCellColumnFits(int cellColumn)
    {
        int desiredEdgesPerRow = (cellColumn + 2) * 2;
        if (desiredEdgesPerRow <= _edgesPerRow) return;

        int rowCount = _edgesPerRow == 0 ? 1 : _edges.Count / _edgesPerRow;
        int oldSize = _edges.Count;
        _edges.Capacity = rowCount * desiredEdgesPerRow;
        for (int i = oldSize; i < rowCount * desiredEdgesPerRow; i++)
            _edges.Add(new Edge { Style = null, Side = EdgeSide.kTop, BoxOrder = 0 });

        for (int rowIndex = rowCount - 1; rowIndex > 0; rowIndex--)
        {
            int newEdge = desiredEdgesPerRow - 1;
            bool done = false;
            do
            {
                int newEdgeIndex = rowIndex * desiredEdgesPerRow + newEdge;
                if (newEdge < _edgesPerRow)
                {
                    int oldEdgeIndex = rowIndex * _edgesPerRow + newEdge;
                    _edges[newEdgeIndex] = _edges[oldEdgeIndex];
                }
                else
                {
                    _edges[newEdgeIndex] = new Edge { Style = null, Side = EdgeSide.kTop, BoxOrder = 0 };
                }
                done = newEdge-- == 0;
            } while (!done);
        }

        for (int edgeIndex = _edgesPerRow; edgeIndex < desiredEdgesPerRow; edgeIndex++)
        {
            _edges[edgeIndex] = new Edge { Style = null, Side = EdgeSide.kTop, BoxOrder = 0 };
        }
        _edgesPerRow = desiredEdgesPerRow;
    }

    private void EnsureCellRowFits(int cellRow)
    {
        int currentBlockEdges = _edgesPerRow == 0 ? 0 : _edges.Count / _edgesPerRow;
        int desiredBlockEdges = cellRow + 2;
        if (desiredBlockEdges <= currentBlockEdges) return;

        int targetCount = desiredBlockEdges * _edgesPerRow;
        for (int i = _edges.Count; i < targetCount; i++)
            _edges.Add(new Edge { Style = null, Side = EdgeSide.kTop, BoxOrder = 0 });
    }

    private static bool IsSourceMoreSpecificThanEdge(BorderStyle sourceStyle, float sourceWidth, Edge edge)
    {
        if (edge.Side == EdgeSide.kDoNotFill) return false;
        if (edge.Style == null || sourceStyle == BorderStyle.None) return true;

        var edgeBorderStyle = BorderStyleValue(edge.Style, edge.Side);
        if (edgeBorderStyle == BorderStyle.None) return false;

        float edgeWidth = BorderWidth(edge.Style, edge.Side);
        if (sourceWidth < edgeWidth) return false;
        if (sourceWidth > edgeWidth) return true;
        return sourceStyle > edgeBorderStyle;
    }

    public static TableBorders ComputeTableBorders(Element table)
    {
        var tableStyle = table.ComputedStyle;
        if (tableStyle == null)
            return new TableBorders(BoxStrut.Zero, false);

        bool isCollapsed = tableStyle.BorderCollapse;
        var tableBorders = new TableBorders(LengthUtils.ComputeBorders(tableStyle), isCollapsed);

        if (!isCollapsed)
            return tableBorders;

        var groupedChildren = new TableGroupedChildren(table);
        int boxOrder = 0;
        int tableColumnCount = ComputeMaximumNonMergeableColumnCount(groupedChildren.Columns, IsFixedTableLayout(tableStyle));
        int tableRowIndex = 0;
        bool foundMultispanCells = false;

        foreach (var section in TableBordersHelpers.GetSections(groupedChildren))
        {
            int sectionStartRow = tableRowIndex;
            var tabulator = new ColspanCellTabulator();
            foreach (var row in section.Children)
            {
                if (row is not Element rowEl) continue;
                tabulator.StartRow();
                foreach (var cell in rowEl.Children)
                {
                    if (cell is not Element cellEl) continue;
                    tabulator.FindNextFreeColumn();
                    int cellColspan = int.TryParse(cellEl.GetAttribute("colspan"), out var cs) ? cs : 1;
                    int cellRowspan = int.TryParse(cellEl.GetAttribute("rowspan"), out var rs) ? rs : 1;
                    foundMultispanCells |= cellRowspan > 1 || cellColspan > 1;
                    tableColumnCount = Math.Max(tableColumnCount,
                        ComputeMaxColumn(tabulator.CurrentColumn, cellColspan, IsFixedTableLayout(tableStyle)));
                    if (!foundMultispanCells)
                    {
                        tableBorders.MergeBorders(tableRowIndex, tabulator.CurrentColumn,
                            cellRowspan, cellColspan, cellEl.ComputedStyle!,
                            EdgeSource.kCell, ++boxOrder);
                    }
                    tabulator.ProcessCell(cellEl);
                }
                tabulator.EndRow();
                tableRowIndex++;
            }
            tableBorders.AddSection(sectionStartRow, tableRowIndex - sectionStartRow);
        }

        tableBorders.SetLastColumnIndex(tableColumnCount);
        int tableRowCount = tableRowIndex;
        tableRowIndex = 0;

        if (foundMultispanCells)
        {
            int sectionIndex = 0;
foreach (var section in TableBordersHelpers.GetSections(groupedChildren))
            {
                var tabulator = new ColspanCellTabulator();
                foreach (var row in section.Children)
                {
                    if (row is not Element rowEl) continue;
                    tabulator.StartRow();
                    foreach (var cell in rowEl.Children)
                    {
                        if (cell is not Element cellEl) continue;
                        tabulator.FindNextFreeColumn();
                        tableBorders.MergeBorders(tableRowIndex, tabulator.CurrentColumn,
                            cellEl.GetAttribute("rowspan") is string rs && int.TryParse(rs, out var rsv) ? rsv : 1,
                            cellEl.GetAttribute("colspan") is string cs && int.TryParse(cs, out var csv) ? csv : 1, cellEl.ComputedStyle!,
                            EdgeSource.kCell, ++boxOrder, sectionIndex);
                        tabulator.ProcessCell(cellEl);
                    }
                    tabulator.EndRow();
                    tableRowIndex++;
                }
                sectionIndex++;
            }
        }

        tableRowIndex = 0;
        foreach (var section in TableBordersHelpers.GetSections(groupedChildren))
            foreach (var row in section.Children)
            {
                if (row is not Element rowEl) continue;
                tableBorders.MergeBorders(tableRowIndex, 0, 1, tableColumnCount,
                    rowEl.ComputedStyle!, EdgeSource.kRow, ++boxOrder);
                tableRowIndex++;
            }

        int secIndex = 0;
        foreach (var section in TableBordersHelpers.GetSections(groupedChildren))
        {
            var sectionInfo = tableBorders.GetSection(secIndex);
            tableBorders.MergeBorders(sectionInfo.StartRow, 0, sectionInfo.RowCount, tableColumnCount,
                section.ComputedStyle!, EdgeSource.kSection, ++boxOrder);
            secIndex++;
        }

        tableBorders.MergeBorders(0, 0, tableRowCount, tableColumnCount,
            tableStyle, EdgeSource.kTable, ++boxOrder);

        tableBorders.UpdateTableBorder(tableRowCount, tableColumnCount);
        return tableBorders;
    }

    public static int ComputeMaximumNonMergeableColumnCount(List<Element> columns, bool isFixedLayout)
    {
        var columnConstraints = new List<TableTypes.Column>();
        foreach (var col in columns)
        {
            var style = col.ComputedStyle;
            if (style == null) continue;
            var constraint = new TableTypes.CellInlineConstraint();
            int span = int.TryParse(col.GetAttribute("span"), out var spanVal) ? spanVal : 1;
            for (int i = 0; i < span; i++)
            {
                columnConstraints.Add(new TableTypes.Column(constraint, 0, 1)
                {
                    is_table_fixed = isFixedLayout,
                    is_mergeable = !isFixedLayout
                });
            }
        }

        if (columnConstraints.Count == 0) return 0;
        int columnIndex = columnConstraints.Count - 1;
        while (columnIndex > 0 && columnConstraints[columnIndex].is_mergeable)
            columnIndex--;
        if (columnIndex == 0 && columnConstraints[0].is_mergeable) return 0;
        return columnIndex + 1;
    }

    public static int ComputeMaxColumn(int currentColumn, int colspan, bool isFixedTableLayout)
    {
        if (isFixedTableLayout) return currentColumn + colspan;
        return currentColumn + 1;
    }

    public class ColspanCellTabulator
    {
        public struct Cell
        {
            public int ColumnStart;
            public int Span;
            public int RemainingRows;
        }

        private int _currentColumn;
        private readonly List<Cell> _colspannedCells = new();

        public int CurrentColumn => _currentColumn;

        public void StartRow()
        {
            _currentColumn = 0;
        }

        public void FindNextFreeColumn()
        {
            foreach (var cell in _colspannedCells)
            {
                if (cell.ColumnStart <= _currentColumn &&
                    cell.ColumnStart + cell.Span > _currentColumn)
                {
                    _currentColumn = cell.ColumnStart + cell.Span;
                }
            }
        }

        public void ProcessCell(Element cell)
        {
            int colspan = int.TryParse(cell.GetAttribute("colspan"), out var csv) ? csv : 1;
            int rowspan = int.TryParse(cell.GetAttribute("rowspan"), out var rsv) ? rsv : 1;
            if (rowspan > 1)
                _colspannedCells.Add(new Cell { ColumnStart = _currentColumn, Span = colspan, RemainingRows = rowspan });
            _currentColumn += colspan;
        }

        public void EndRow()
        {
            for (int i = 0; i < _colspannedCells.Count;)
            {
                var cell = _colspannedCells[i];
                cell.RemainingRows--;
                if (cell.RemainingRows == 0)
                    _colspannedCells.RemoveAt(i);
                else
                {
                    _colspannedCells[i] = cell;
                    i++;
                }
            }
            _colspannedCells.Sort((a, b) => a.ColumnStart.CompareTo(b.ColumnStart));
        }
    }
}

internal static class TableBordersHelpers
{
    internal static IEnumerable<Element> GetSections(TableGroupedChildren groupedChildren)
    {
        if (groupedChildren.Header != null) yield return groupedChildren.Header;
        foreach (var body in groupedChildren.Bodies) yield return body;
        if (groupedChildren.Footer != null) yield return groupedChildren.Footer;
    }
}