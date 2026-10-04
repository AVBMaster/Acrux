using System;
using System.Collections.Generic;
using System.Linq;
using Acrux.Core.Dom;
using Acrux.Core.Layout.Geometry;

namespace Acrux.Core.Layout.Table;

/// <summary>
/// Distribution and measurement helpers for the table layout algorithm.
/// Mirrors table_layout_utils.cc (column constraints, inline-size
/// distribution, row/block-size distribution, cell constraint spaces).
/// </summary>
public static class TableLayoutUtils
{
    /// <summary>A resolved grid column location (offset/size within the grid).</summary>
    public struct TableColumnLocation
    {
        public float Offset;
        public float Size;
        public bool IsCollapsed;
    }

    // ==========================================================================
    // Small helpers.
    // ==========================================================================

    private static float K(float v) => TableTypes.IsIndefinite(v) ? 0 : v;
    private static float ClampNeg(float v) => Math.Max(0, v);
    private static float MulDiv(float a, float b, float c) => c != 0 ? a * b / c : 0;

    public static int CellColspan(Element cell)
    {
        if (int.TryParse(cell.GetAttribute("colspan"), out var span) && span > 0)
            return span;
        return 1;
    }

    public static int CellRowspan(Element cell)
    {
        if (int.TryParse(cell.GetAttribute("rowspan"), out var span) && span > 0)
            return span;
        return 1;
    }

    public static int ComputeMaxColumn(int currentColumn, int colspan, bool isFixedTableLayout)
    {
        if (isFixedTableLayout) return currentColumn + colspan;
        return currentColumn + 1;
    }

    public static bool IsEmptyTableSection(Element section) => GetSectionRows(section).Count == 0;

    /// <summary>Rows of a section. A row that appears directly under the table (no
    /// surrounding tbody) is treated as a section of one row. Membership is decided
    /// by the COMPUTED display, not the tag: CSS-created tables put
    /// 'display: table-row' on arbitrary elements, and a 'tr' restyled to
    /// 'display: block' is no longer a row. Rows with display:none are excluded so
    /// hidden rows take no space at all.</summary>
    public static List<Element> GetSectionRows(Element section)
    {
        var rows = new List<Element>();
        foreach (var child in section.Children)
        {
            if (child is Element el && el.ComputedStyle?.Display == DisplayType.TableRow)
            {
                rows.Add(el);
            }
        }
        if (rows.Count == 0 && section.ComputedStyle?.Display == DisplayType.TableRow)
            rows.Add(section);
        return rows;
    }

    // ==========================================================================
    // Recursive min/max content-size computation (cell/caption sizing).
    // ==========================================================================

    public static (float min, float max) ComputeContentMinMax(Element node)
    {
        float min = 0, max = 0;
        bool hasUnmeasurableChild = false;
        var style = node.ComputedStyle;
        foreach (var child in node.Children)
        {
            switch (child)
            {
                case TextNode tn:
                    string text = tn.Data ?? string.Empty;
                    // Collapsible whitespace claims no width: '<td> </td>' is an empty
                    // cell, and the spaces around a word are not part of its content
                    // size either (CSS Text 3 §4.1.1). A non-breaking space is not
                    // collapsible, so it still counts.
                    if (TextNode.IsCollapsibleWhitespace(text)) continue;
                    text = text.Trim(' ', '\t', '\n', '\r', '\f');
                    min = Math.Max(min, LongestWordWidth(text, style));
                    max += MeasureTextWidth(text, style);
                    break;
                case Element el:
                {
                    var childStyle = el.ComputedStyle;
                    if (childStyle == null || childStyle.Display == DisplayType.None)
                        continue;
                    // Only content this walk cannot size at all (an image) may claim a
                    // placeholder width: a line break or an empty span must not stop an
                    // otherwise empty cell from collapsing to zero.
                    hasUnmeasurableChild |= BlockLayoutAlgorithm.IsReplacedElement(el);
                    // A replaced child is sized by its own box, not by its (absent)
                    // children: the specified width wins, then the 'width' content
                    // attribute, which is what the layout box of an <img width=40> gets.
                    if (BlockLayoutAlgorithm.IsReplacedElement(el))
                    {
                        float replaced = ReplacedInlineSize(el, childStyle);
                        if (replaced > 0)
                        {
                            min = Math.Max(min, replaced);
                            max += replaced;
                            continue;
                        }
                    }
                    var (cmin, cmax) = ComputeContentMinMax(el);
                    min = Math.Max(min, cmin);
                    switch (childStyle.Display)
                    {
                        case DisplayType.Inline:
                        case DisplayType.InlineBlock:
                        case DisplayType.InlineFlex:
                        case DisplayType.InlineGrid:
                        case DisplayType.TableCaption:
                            max += cmax;
                            break;
                        default:
                            max = Math.Max(max, cmax);
                            break;
                    }
                    break;
                }
            }
        }
        if (min == 0 && max == 0 && style != null && hasUnmeasurableChild)
        {
            // A replaced child still has to claim some width or the cell collapses
            // right onto it; an empty cell has nothing to claim and stays 0, which is what
            // Chrome gives it.
            float fallback = MeasureTextWidth(" ", style);
            min = Math.Max(min, fallback);
            max = Math.Max(max, fallback);
        }
        return (min, max);
    }

    /// <summary>The inline size a replaced child claims: CSS 'width' when it is a fixed
    /// length, otherwise the 'width' content attribute. Zero means "unknown here", which
    /// leaves the caller's placeholder fallback in charge.</summary>
    private static float ReplacedInlineSize(Element el, ComputedStyle childStyle)
    {
        var width = childStyle.Width;
        if (width != null && width.IsFixed())
        {
            float fixedWidth = width.FixedValue();
            if (fixedWidth > 0) return fixedWidth;
        }
        string? attribute = el.GetAttribute("width");
        if (!string.IsNullOrEmpty(attribute)
            && float.TryParse(attribute.Trim(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float fromAttribute)
            && fromAttribute > 0)
            return fromAttribute;
        return 0;
    }

    private static float MeasureTextWidth(string text, ComputedStyle? style)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        float fontSize = style?.FontSize ?? 16;
        string fontFamily = style?.FontFamily ?? "Arial, sans-serif";
        var measurer = TextMeasurer.Instance;
        if (measurer != null)
            return measurer.MeasureText(text, fontFamily, fontSize);
        return text.Length * fontSize * 0.5f;
    }

    private static float LongestWordWidth(string text, ComputedStyle? style)
    {
        float max = 0;
        int start = 0;
        for (int i = 0; i <= text.Length; i++)
        {
            if (i == text.Length || text[i] == ' ')
            {
                if (i > start)
                    max = Math.Max(max, MeasureTextWidth(text.Substring(start, i - start), style));
                start = i + 1;
            }
        }
        if (max == 0)
            max = MeasureTextWidth(text, style);
        return max;
    }

    // ==========================================================================
    // Column constraint building (COL / COLGROUP / cells).
    // ==========================================================================

    /// <summary>Builds column constraints from COL/COLGROUP elements. Mirrors
    /// the ColumnConstraintsBuilder in table_layout_utils.cc.</summary>
    public class ColumnConstraintsBuilder
    {
        private readonly List<TableTypes.Column> _columnConstraints;
        private readonly bool _isFixedLayout;
        private TableTypes.Column? _colgroupConstraint;

        public ColumnConstraintsBuilder(List<TableTypes.Column> columnConstraints, bool isFixedLayout)
        {
            _columnConstraints = columnConstraints;
            _isFixedLayout = isFixedLayout;
        }

        public void VisitCol(Element column, int startColumnIndex, int span)
        {
            // COL creates |span| constraints. Its width is the col css width,
            // or the enclosing colgroup css width.
            float? defaultInlineSize = null;
            if (!_isFixedLayout && _colgroupConstraint != null)
                defaultInlineSize = K(_colgroupConstraint.constraint.max_inline_size);
            var colConstraint = CreateColumnFromStyle(column.ComputedStyle, defaultInlineSize, _isFixedLayout);
            for (int i = 0; i < span; i++)
                _columnConstraints.Add(colConstraint);
        }

        public void EnterColgroup(Element colgroup, int startColumnIndex)
        {
            _colgroupConstraint = CreateColumnFromStyle(colgroup.ComputedStyle, null, _isFixedLayout);
        }

        public void LeaveColgroup(Element colgroup, int startColumnIndex, int span, bool hasChildren)
        {
            if (!hasChildren && _colgroupConstraint != null)
            {
                for (int i = 0; i < span; i++)
                    _columnConstraints.Add(_colgroupConstraint);
            }
            _colgroupConstraint = null;
        }
    }

    private static TableTypes.Column CreateColumnFromStyle(ComputedStyle? style, float? defaultInlineSize, bool isTableFixed)
    {
        style ??= new ComputedStyle();
        var constraint = new TableTypes.CellInlineConstraint { is_table_fixed = isTableFixed };

        float? inlineSize = null;
        float? minInlineSize = null;
        if (style.Width.IsFixed())
        {
            inlineSize = style.Width.FixedValue();
            minInlineSize = inlineSize;
        }
        if (style.MinWidth.IsFixed())
        {
            minInlineSize = Math.Max(minInlineSize ?? 0, style.MinWidth.FixedValue());
            if (inlineSize.HasValue)
                inlineSize = Math.Max(inlineSize.Value, minInlineSize.Value);
        }
        if (!inlineSize.HasValue && minInlineSize.HasValue)
            inlineSize = minInlineSize;
        if (!inlineSize.HasValue)
            inlineSize = defaultInlineSize;

        float? percent = null;
        if (style.Width.IsPercent() && style.Width.PercentValue() > 0)
            percent = style.Width.PercentValue();

        if (inlineSize.HasValue)
        {
            constraint.min_inline_size = K(minInlineSize ?? 0);
            constraint.max_inline_size = inlineSize.Value;
            constraint.is_constrained = true;
        }
        if (percent.HasValue)
            constraint.percent = percent;

        constraint.min_inline_size = K(constraint.min_inline_size);
        constraint.max_inline_size = K(constraint.max_inline_size);
        constraint.is_collapsed = style.Visibility == VisibilityType.Collapse;
        constraint.is_mergeable = !isTableFixed && constraint.max_inline_size == 0 && !constraint.percent.HasValue;

        return new TableTypes.Column(constraint, 0, 1);
    }

    /// <summary>Entry point: computes the table's column constraints. Mirrors
    /// ComputeColumnConstraints in table_layout_utils.cc.</summary>
    public static List<TableTypes.Column> ComputeColumnConstraints(
        Element table, TableGroupedChildren groupedChildren, TableBorders tableBorders, BoxStrut borderPadding)
    {
        var tableStyle = table.ComputedStyle;
        bool isFixedLayout = tableStyle != null && tableStyle.TableLayout == "fixed";
        float inlineBorderSpacing = isFixedLayout ? 0 : (tableStyle?.BorderSpacing ?? 0);

        var cellInlineConstraints = new List<TableTypes.CellInlineConstraint?>();
        var colspanCellConstraints = new List<TableTypes.ColspanCell>();
        var columnConstraints = new List<TableTypes.Column>();

        VisitTableColumns(groupedChildren.Columns, isFixedLayout, columnConstraints);

        bool isFirstSection = true;
        int rowIndex = 0;
        foreach (var section in groupedChildren.Begin().Sections())
        {
            if (!IsEmptyTableSection(section))
            {
                ComputeSectionInlineConstraints(section, isFixedLayout, isFirstSection, tableBorders,
                    ref rowIndex, cellInlineConstraints, colspanCellConstraints);
                isFirstSection = false;
            }
        }

        ApplyCellConstraintsToColumnConstraints(cellInlineConstraints, inlineBorderSpacing, isFixedLayout,
            colspanCellConstraints, columnConstraints);
        return columnConstraints;
    }

    private static void VisitTableColumns(List<Element> columns, bool isFixedLayout, List<TableTypes.Column> columnConstraints)
    {
        var builder = new ColumnConstraintsBuilder(columnConstraints, isFixedLayout);
        foreach (var col in columns)
        {
            if (col.ComputedStyle?.Display == DisplayType.TableColumnGroup)
            {
                int span = Math.Max(1, int.TryParse(col.GetAttribute("span"), out var sv) ? sv : 1);
                bool hasChildren = false;
                var children = col.Children;
                foreach (var child in children)
                {
                    if (child is Element childEl && childEl.TagName == "COL")
                    {
                        hasChildren = true;
                        break;
                    }
                }
                int start = columnConstraints.Count;
                builder.EnterColgroup(col, start);
                if (hasChildren)
                {
                    foreach (var child in children)
                    {
                        if (child is not Element childEl || childEl.TagName != "COL")
                            continue;
                        int childSpan = Math.Max(1, int.TryParse(childEl.GetAttribute("span"), out var cspan) ? cspan : 1);
                        builder.VisitCol(childEl, columnConstraints.Count, childSpan);
                    }
                }
                builder.LeaveColgroup(col, start, span, hasChildren);
            }
            else
            {
                int span = Math.Max(1, int.TryParse(col.GetAttribute("span"), out var sv) ? sv : 1);
                builder.VisitCol(col, columnConstraints.Count, span);
            }
        }
    }

    private static void ComputeSectionInlineConstraints(Element section, bool isFixedLayout, bool isFirstSection, TableBorders tableBorders,
        ref int rowIndex, List<TableTypes.CellInlineConstraint?> cellInlineConstraints, List<TableTypes.ColspanCell> colspanCellConstraints)
    {
        var tabulator = new TableBorders.ColspanCellTabulator();
        bool isFirstRow = true;
        foreach (var row in GetSectionRows(section))
        {
            if (row.ComputedStyle?.Display == DisplayType.None)
            {
                tabulator.EndRow();
                continue;
            }
            tabulator.StartRow();
            foreach (var cellNode in row.Children)
            {
                if (cellNode is not Element cell)
                    continue;
                if (cell.ComputedStyle?.Display == DisplayType.None)
                {
                    tabulator.ProcessCell(cell);
                    continue;
                }
                tabulator.FindNextFreeColumn();
                int startColumn = tabulator.CurrentColumn;
                int colspan = CellColspan(cell);

                bool ignoreBecauseOfFixedLayout = isFixedLayout && (!isFirstSection || !isFirstRow);

                int maxColumn = ComputeMaxColumn(startColumn, colspan, isFixedLayout);
                while (maxColumn >= cellInlineConstraints.Count)
                    cellInlineConstraints.Add(null);
                if (!ignoreBecauseOfFixedLayout)
                {
                    var constraint = TableTypes.CreateCellInlineConstraint(new List<TableTypes.Column>(), startColumn, colspan, cell,
                            tableBorders, rowIndex);
                    // Content-based min/max sizes only feed the grid in auto
                    // layout. With table-layout: fixed the column widths come from
                    // the specified widths alone and the rest is shared equally
                    // (CSS 2.1 §17.5.2.1).
                    if (!isFixedLayout)
                    {
                        var (contentMin, contentMax) = ComputeContentMinMax(cell);
                        // Collapsed borders contribute only half of each shared edge (CSS 2.1 §17.6.2).
                        var collapsed = tableBorders.IsCollapsed
                            ? tableBorders.CellBorder(rowIndex, startColumn, 1, colspan, tableBorders.SectionIndexOf(rowIndex))
                            : default(BoxStrut?);
                        var cellBorderPadding = TableTypes.ComputeCellBorderPadding(cell, null, collapsed);
                        float bpSum = cellBorderPadding.HorizontalSum;
                        constraint.min_inline_size = MaxMin(constraint.min_inline_size, contentMin + bpSum);
                        constraint.max_inline_size = MaxMin(constraint.max_inline_size, contentMax + bpSum);
                    }
                    constraint.min_inline_size = K(constraint.min_inline_size);
                    constraint.max_inline_size = Math.Max(K(constraint.max_inline_size), K(constraint.min_inline_size));

                    if (colspan == 1)
                    {
                        if (cellInlineConstraints[startColumn] != null)
                            cellInlineConstraints[startColumn]!.Encompass(constraint);
                        else
                            cellInlineConstraints[startColumn] = constraint;
                    }
                    else
                    {
                        var colspanCell = new TableTypes.ColspanCell(startColumn, colspan, cell);
                        colspanCell.constraint.Encompass(constraint);
                        colspanCellConstraints.Add(colspanCell);
                    }
                }
                tabulator.ProcessCell(cell);
            }
            isFirstRow = false;
            rowIndex++;
            tabulator.EndRow();
        }
    }

    private static float MaxMin(float a, float b) =>
        TableTypes.IsIndefinite(a) ? b : TableTypes.IsIndefinite(b) ? a : Math.Max(a, b);

    /// <summary>Applies cell/wide-vell constraints to columns and distributes
    /// colspan cells. Mirrors ApplyCellConstraintsToColumnConstraints.</summary>
    public static void ApplyCellConstraintsToColumnConstraints(
        List<TableTypes.CellInlineConstraint?> cellConstraints, float inlineBorderSpacing, bool isFixedLayout,
        List<TableTypes.ColspanCell> colspanCellConstraints, List<TableTypes.Column> columnConstraints)
    {
        // Satisfy prerequisites for cell merging: a column constraint must exist
        // for each cell.
        // The cell traversal reserves one trailing empty slot (the border-spacing
        // after the last column); it is not a real column, so drop it here. Auto
        // layout used to remove it again via the mergeable trim below, but fixed
        // layout marks its columns non-mergeable and would keep a phantom column.
        while (cellConstraints.Count > 0 && cellConstraints[^1] == null)
            cellConstraints.RemoveAt(cellConstraints.Count - 1);

        if (columnConstraints.Count < cellConstraints.Count)
        {
            int columnCount = cellConstraints.Count - columnConstraints.Count;
            for (int i = 0; i < columnCount; i++)
            {
                var constraint = new TableTypes.CellInlineConstraint { is_table_fixed = isFixedLayout };
                columnConstraints.Add(new TableTypes.Column(constraint, 0, 1)
                {
                    is_table_fixed = isFixedLayout,
                    is_mergeable = !isFixedLayout
                });
            }
        }
        else if (columnConstraints.Count > cellConstraints.Count)
        {
            // Trim mergeable columns off the end.
            int lastNonMerged = columnConstraints.Count - 1;
            while (lastNonMerged + 1 > cellConstraints.Count && columnConstraints[lastNonMerged].is_mergeable)
                lastNonMerged--;
            int keep = lastNonMerged + 1;
            if (keep < columnConstraints.Count)
                columnConstraints.RemoveRange(keep, columnConstraints.Count - keep);
        }

        // Make sure there exists a non-mergeable column for each colspanned cell.
        foreach (var colspanCell in colspanCellConstraints)
            EnsureDistributableColumnExists(colspanCell.start_column, colspanCell.span, columnConstraints);

        // Distribute cell constraints to column constraints.
        for (int i = 0; i < cellConstraints.Count && i < columnConstraints.Count; i++)
        {
            var cell = cellConstraints[i];
            if (cell == null)
                continue;
            var column = columnConstraints[i];
            // Constrained columns in fixed tables take precedence over cells.
            if (column.constraint.is_constrained && column.is_table_fixed)
                continue;
            if (!column.is_table_fixed)
                column.is_mergeable = false;
            column.constraint.Encompass(cell);
            column.is_collapsed |= cell.is_collapsed;
            column.is_table_fixed |= cell.is_table_fixed;
        }

        // Wide cell constraints are sorted by span length/starting column.
        colspanCellConstraints.Sort((lhs, rhs) =>
        {
            int cmp = lhs.span.CompareTo(rhs.span);
            return cmp != 0 ? cmp : lhs.start_column.CompareTo(rhs.start_column);
        });

        DistributeColspanCellsToColumns(colspanCellConstraints, inlineBorderSpacing, isFixedLayout, columnConstraints);

        // Column total percentage inline-size is clamped to 100%.
        float totalPercentage = 0;
        for (int i = 0; i < columnConstraints.Count; i++)
        {
            var column = columnConstraints[i];
            if (column.constraint.percent.HasValue)
            {
                if (!isFixedLayout && (column.constraint.percent.Value + totalPercentage > 100.0f))
                    column.constraint.percent = 100 - totalPercentage;
                totalPercentage += column.constraint.percent.Value;
            }
            column.constraint.min_inline_size = K(column.constraint.min_inline_size);
            column.constraint.max_inline_size = K(column.constraint.max_inline_size);
        }

        if (isFixedLayout && totalPercentage > 100.0f)
        {
            foreach (var column in columnConstraints)
            {
                if (column.constraint.percent.HasValue)
                    column.constraint.percent = column.constraint.percent.Value * 100 / totalPercentage;
            }
        }
    }

    /// <summary>We cannot distribute space to mergeable columns; mark at least
    /// one of the spanned columns as distributable (non-mergeable).</summary>
    public static void EnsureDistributableColumnExists(int startColumnIndex, int span, List<TableTypes.Column> columnConstraints)
    {
        if (startColumnIndex >= columnConstraints.Count)
            return;
        int effectiveSpan = Math.Min(span, columnConstraints.Count - startColumnIndex);
        for (int i = startColumnIndex; i < startColumnIndex + effectiveSpan; i++)
        {
            if (!columnConstraints[i].is_collapsed)
            {
                columnConstraints[i].is_mergeable = false;
                return;
            }
        }
        columnConstraints[startColumnIndex].is_mergeable = false;
    }

    public static void DistributeColspanCellsToColumns(List<TableTypes.ColspanCell> colspanCells, float inlineBorderSpacing,
        bool isFixedLayout, List<TableTypes.Column> columnConstraints)
    {
        foreach (var colspanCell in colspanCells)
        {
            if (colspanCell.span <= 1)
                continue;
            if (isFixedLayout)
                DistributeColspanCellToColumnsFixed(colspanCell, inlineBorderSpacing, columnConstraints);
            else
                DistributeColspanCellToColumnsAuto(colspanCell, inlineBorderSpacing, columnConstraints);
        }
    }

    public static void DistributeColspanCellToColumnsFixed(TableTypes.ColspanCell colspanCell, float inlineBorderSpacing,
        List<TableTypes.Column> columnConstraints)
    {
        int endColumn = Math.Min(colspanCell.start_column + colspanCell.span, columnConstraints.Count);
        if (colspanCell.start_column >= endColumn)
            return;

        // Inline sizes for redistribution exclude border spacing.
        float totalInnerBorderSpacing = 0;
        int effectiveSpan = 0;
        bool isFirstColumn = true;
        for (int i = colspanCell.start_column; i < endColumn; i++)
        {
            if (columnConstraints[i].is_mergeable)
                continue;
            effectiveSpan++;
            if (!isFirstColumn)
                totalInnerBorderSpacing += inlineBorderSpacing;
            else
                isFirstColumn = false;
        }
        if (effectiveSpan == 0)
            return;

        var cellConstraint = colspanCell.constraint;
        float colspanCellMinInlineSize = cellConstraint.is_constrained
            ? ClampNeg(K(cellConstraint.min_inline_size) - totalInnerBorderSpacing)
            : 0;
        float colspanCellMaxInlineSize = ClampNeg(K(cellConstraint.max_inline_size) - totalInnerBorderSpacing);

        float roundingErrorMin = colspanCellMinInlineSize;
        float roundingErrorMax = colspanCellMaxInlineSize;
        float newMinSize = colspanCellMinInlineSize / effectiveSpan;
        float newMaxSize = colspanCellMaxInlineSize / effectiveSpan;
        float? newPercent = cellConstraint.percent.HasValue ? cellConstraint.percent.Value / effectiveSpan : null;

        int lastColumn = -1;
        for (int i = colspanCell.start_column; i < endColumn; i++)
        {
            var column = columnConstraints[i];
            if (column.is_mergeable)
                continue;
            lastColumn = i;
            roundingErrorMin -= newMinSize;
            roundingErrorMax -= newMaxSize;

            if (TableTypes.IsIndefinite(column.constraint.min_inline_size))
            {
                column.constraint.is_constrained |= cellConstraint.is_constrained;
                column.constraint.min_inline_size = newMinSize;
            }
            if (TableTypes.IsIndefinite(column.constraint.max_inline_size))
            {
                column.constraint.is_constrained |= cellConstraint.is_constrained;
                column.constraint.max_inline_size = newMaxSize;
            }
            // Percentages only get distributed over auto columns.
            if (!column.constraint.percent.HasValue && !column.constraint.is_constrained && newPercent.HasValue)
                column.constraint.percent = newPercent;
        }
        if (lastColumn >= 0)
        {
            columnConstraints[lastColumn].constraint.min_inline_size = K(columnConstraints[lastColumn].constraint.min_inline_size) + roundingErrorMin;
            columnConstraints[lastColumn].constraint.max_inline_size = K(columnConstraints[lastColumn].constraint.max_inline_size) + roundingErrorMax;
        }
    }

    public static void DistributeColspanCellToColumnsAuto(TableTypes.ColspanCell colspanCell, float inlineBorderSpacing,
        List<TableTypes.Column> columnConstraints)
    {
        if (columnConstraints.Count == 0)
            return;
        int start = colspanCell.start_column;
        if (start >= columnConstraints.Count)
            return;
        int effectiveSpan = Math.Min(colspanCell.span, columnConstraints.Count - start);
        int end = start + effectiveSpan;
        if (end <= start)
            return;

        float totalInnerBorderSpacing = 0;
        bool isFirstColumn = true;
        for (int i = start; i < end; i++)
        {
            if (!columnConstraints[i].is_mergeable)
            {
                if (!isFirstColumn)
                    totalInnerBorderSpacing += inlineBorderSpacing;
                else
                    isFirstColumn = false;
            }
        }

        var cellConstraint = colspanCell.constraint;
        float colspanCellMinInlineSize = ClampNeg(K(cellConstraint.min_inline_size) - totalInnerBorderSpacing);
        float colspanCellMaxInlineSize = ClampNeg(K(cellConstraint.max_inline_size) - totalInnerBorderSpacing);
        float? colspanCellPercent = cellConstraint.percent;

        if (colspanCellPercent.HasValue)
        {
            float columnsPercent = 0.0f;
            int allColumnsCount = 0;
            int percentColumnsCount = 0;
            int nonpercentColumnsCount = 0;
            float nonpercentColumnsMaxInlineSize = 0;
            for (int i = start; i < end; i++)
            {
                var column = columnConstraints[i];
                column.constraint.min_inline_size = K(column.constraint.min_inline_size);
                column.constraint.max_inline_size = K(column.constraint.max_inline_size);
                if (column.is_mergeable)
                    continue;
                allColumnsCount++;
                if (column.constraint.percent.HasValue)
                {
                    percentColumnsCount++;
                    columnsPercent += column.constraint.percent.Value;
                }
                else
                {
                    nonpercentColumnsCount++;
                    nonpercentColumnsMaxInlineSize += column.constraint.max_inline_size;
                }
            }
            float surplusPercent = colspanCellPercent.Value - columnsPercent;
            if (surplusPercent > 0.0f && allColumnsCount > percentColumnsCount)
            {
                for (int i = start; i < end; i++)
                {
                    var column = columnConstraints[i];
                    if (column.constraint.percent.HasValue || column.is_mergeable)
                        continue;
                    float columnPercent;
                    if (nonpercentColumnsMaxInlineSize != 0)
                        columnPercent = surplusPercent * column.constraint.max_inline_size / nonpercentColumnsMaxInlineSize;
                    else
                        columnPercent = surplusPercent / nonpercentColumnsCount;
                    column.constraint.percent = columnPercent;
                }
            }
        }

        for (int i = start; i < end; i++)
        {
            columnConstraints[i].constraint.min_inline_size = K(columnConstraints[i].constraint.min_inline_size);
            columnConstraints[i].constraint.max_inline_size = K(columnConstraints[i].constraint.max_inline_size);
        }

        var computedMin = DistributeInlineSizeToComputedInlineSizeAuto(colspanCellMinInlineSize, start, end, true, columnConstraints);
        for (int i = 0; i < end - start; i++)
        {
            var column = columnConstraints[start + i];
            column.constraint.min_inline_size = Math.Max(column.constraint.min_inline_size, computedMin[i]);
        }

        var computedMax = DistributeInlineSizeToComputedInlineSizeAuto(colspanCellMaxInlineSize, start, end,
            cellConstraint.is_constrained, columnConstraints);
        for (int i = 0; i < end - start; i++)
        {
            var column = columnConstraints[start + i];
            column.constraint.max_inline_size = Math.Max(Math.Max(column.constraint.min_inline_size, column.constraint.max_inline_size), computedMax[i]);
        }
    }

    // ==========================================================================
    // Inline-size distribution (css-tables-3 width distribution algorithm).
    // ==========================================================================

    public static List<float> DistributeInlineSizeToComputedInlineSizeAuto(
        float targetInlineSize, int startColumnIndex, int endColumnIndex, bool treatTargetSizeAsConstrained, List<TableTypes.Column> columnConstraints)
    {
        const int kMinGuess = 0, kPercentageGuess = 1, kSpecifiedGuess = 2, kMaxGuess = 3, kAboveMax = 4;

        int allColumnsCount = 0;
        int percentColumnsCount = 0;
        int fixedColumnsCount = 0;
        int autoColumnsCount = 0;
        float[] guessSizes = new float[kAboveMax];
        float[] guessSizeTotalIncreases = new float[kAboveMax];
        float totalPercent = 0.0f;
        float totalAutoMaxInlineSize = 0;
        float totalFixedMaxInlineSize = 0;

        for (int i = startColumnIndex; i < endColumnIndex; i++)
        {
            var column = columnConstraints[i];
            var c = column.constraint;
            allColumnsCount++;
            if (c.is_mergeable)
                continue;
            float min = K(c.min_inline_size);
            float max = K(c.max_inline_size);

            if (c.percent.HasValue)
            {
                percentColumnsCount++;
                totalPercent += c.percent.Value;
                float percentInlineSize = column.ResolvePercentInlineSize(targetInlineSize);
                guessSizes[kMinGuess] += min;
                guessSizes[kPercentageGuess] += percentInlineSize;
                guessSizes[kSpecifiedGuess] += percentInlineSize;
                guessSizes[kMaxGuess] += percentInlineSize;
                guessSizeTotalIncreases[kPercentageGuess] += percentInlineSize - min;
            }
            else if (c.is_constrained)
            {
                // Fixed column.
                fixedColumnsCount++;
                totalFixedMaxInlineSize += max;
                guessSizes[kMinGuess] += min;
                guessSizes[kPercentageGuess] += min;
                guessSizes[kSpecifiedGuess] += max;
                guessSizes[kMaxGuess] += max;
                guessSizeTotalIncreases[kSpecifiedGuess] += max - min;
            }
            else
            {
                // Auto column.
                autoColumnsCount++;
                totalAutoMaxInlineSize += max;
                guessSizes[kMinGuess] += min;
                guessSizes[kPercentageGuess] += min;
                guessSizes[kSpecifiedGuess] += min;
                guessSizes[kMaxGuess] += max;
                guessSizeTotalIncreases[kMaxGuess] += max - min;
            }
        }

        var computedSizes = new float[allColumnsCount];

        // Distributing inline sizes can never cause cells to be < min_inline_size.
        targetInlineSize = Math.Max(targetInlineSize, guessSizes[kMinGuess]);

        int startingGuess = kAboveMax;
        for (int i = kMinGuess; i < kAboveMax; i++)
        {
            if (guessSizes[i] >= targetInlineSize)
            {
                startingGuess = i;
                break;
            }
        }

        int count = endColumnIndex - startColumnIndex;
        switch (startingGuess)
        {
            case kMinGuess:
            {
                for (int i = startColumnIndex, j = 0; i < endColumnIndex; i++, j++)
                {
                    if (columnConstraints[i].constraint.is_mergeable)
                        continue;
                    computedSizes[j] = K(columnConstraints[i].constraint.min_inline_size);
                }
                break;
            }
            case kPercentageGuess:
            {
                float percentIncrease = guessSizeTotalIncreases[kPercentageGuess];
                float distributable = targetInlineSize - guessSizes[kMinGuess];
                float remaining = distributable;
                int lastIdx = -1;
                for (int i = startColumnIndex, j = 0; i < endColumnIndex; i++, j++)
                {
                    var column = columnConstraints[i];
                    if (column.constraint.is_mergeable)
                        continue;
                    if (column.constraint.percent.HasValue)
                    {
                        lastIdx = j;
                        float percentInlineSize = column.ResolvePercentInlineSize(targetInlineSize);
                        float increase = percentInlineSize - K(column.constraint.min_inline_size);
                        float delta;
                        if (increase > 0)
                            delta = MulDiv(distributable, increase, percentIncrease);
                        else
                            delta = percentColumnsCount > 0 ? distributable / percentColumnsCount : 0;
                        remaining -= delta;
                        computedSizes[j] = K(column.constraint.min_inline_size) + delta;
                    }
                    else
                    {
                        computedSizes[j] = K(column.constraint.min_inline_size);
                    }
                }
                if (remaining != 0 && lastIdx >= 0)
                    computedSizes[lastIdx] += remaining;
                break;
            }
            case kSpecifiedGuess:
            {
                float fixedIncrease = guessSizeTotalIncreases[kSpecifiedGuess];
                float distributable = targetInlineSize - guessSizes[kPercentageGuess];
                float remaining = distributable;
                int lastIdx = -1;
                for (int i = startColumnIndex, j = 0; i < endColumnIndex; i++, j++)
                {
                    var column = columnConstraints[i];
                    if (column.constraint.is_mergeable)
                        continue;
                    if (column.constraint.percent.HasValue)
                    {
                        computedSizes[j] = column.ResolvePercentInlineSize(targetInlineSize);
                    }
                    else if (column.constraint.is_constrained)
                    {
                        lastIdx = j;
                        float increase = K(column.constraint.max_inline_size) - K(column.constraint.min_inline_size);
                        float delta;
                        if (fixedIncrease > 0)
                            delta = MulDiv(distributable, increase, fixedIncrease);
                        else
                            delta = fixedColumnsCount > 0 ? distributable / fixedColumnsCount : 0;
                        remaining -= delta;
                        computedSizes[j] = K(column.constraint.min_inline_size) + delta;
                    }
                    else
                    {
                        computedSizes[j] = K(column.constraint.min_inline_size);
                    }
                }
                if (remaining != 0 && lastIdx >= 0)
                    computedSizes[lastIdx] += remaining;
                break;
            }
            case kMaxGuess:
            {
                float autoIncrease = guessSizeTotalIncreases[kMaxGuess];
                float distributable = targetInlineSize - guessSizes[kSpecifiedGuess];
                bool isExactMatch = targetInlineSize == guessSizes[kMaxGuess];
                float remaining = isExactMatch ? 0 : distributable;
                int lastIdx = -1;
                for (int i = startColumnIndex, j = 0; i < endColumnIndex; i++, j++)
                {
                    var column = columnConstraints[i];
                    if (column.constraint.is_mergeable)
                        continue;
                    if (column.constraint.percent.HasValue)
                    {
                        computedSizes[j] = column.ResolvePercentInlineSize(targetInlineSize);
                    }
                    else if (column.constraint.is_constrained || isExactMatch)
                    {
                        computedSizes[j] = K(column.constraint.max_inline_size);
                    }
                    else
                    {
                        lastIdx = j;
                        float increase = K(column.constraint.max_inline_size) - K(column.constraint.min_inline_size);
                        float delta;
                        if (autoIncrease > 0)
                            delta = MulDiv(distributable, increase, autoIncrease);
                        else
                            delta = autoColumnsCount > 0 ? distributable / autoColumnsCount : 0;
                        remaining -= delta;
                        computedSizes[j] = K(column.constraint.min_inline_size) + delta;
                    }
                }
                if (remaining != 0 && lastIdx >= 0)
                    computedSizes[lastIdx] += remaining;
                break;
            }
            case kAboveMax:
            {
                float distributable = targetInlineSize - guessSizes[kMaxGuess];
                if (autoColumnsCount > 0)
                {
                    float remaining = distributable;
                    int lastIdx = -1;
                    for (int i = startColumnIndex, j = 0; i < endColumnIndex; i++, j++)
                    {
                        var column = columnConstraints[i];
                        if (column.constraint.is_mergeable)
                            continue;
                        if (column.constraint.percent.HasValue)
                        {
                            computedSizes[j] = column.ResolvePercentInlineSize(targetInlineSize);
                        }
                        else if (column.constraint.is_constrained)
                        {
                            computedSizes[j] = K(column.constraint.max_inline_size);
                        }
                        else
                        {
                            lastIdx = j;
                            float delta;
                            if (totalAutoMaxInlineSize > 0)
                                delta = MulDiv(distributable, K(column.constraint.max_inline_size), totalAutoMaxInlineSize);
                            else
                                delta = distributable / autoColumnsCount;
                            remaining -= delta;
                            computedSizes[j] = K(column.constraint.max_inline_size) + delta;
                        }
                    }
                    if (remaining != 0 && lastIdx >= 0)
                        computedSizes[lastIdx] += remaining;
                }
                else if (fixedColumnsCount > 0 && treatTargetSizeAsConstrained)
                {
                    float remaining = distributable;
                    int lastIdx = -1;
                    for (int i = startColumnIndex, j = 0; i < endColumnIndex; i++, j++)
                    {
                        var column = columnConstraints[i];
                        if (column.constraint.is_mergeable)
                            continue;
                        if (column.constraint.percent.HasValue)
                        {
                            computedSizes[j] = column.ResolvePercentInlineSize(targetInlineSize);
                        }
                        else if (column.constraint.is_constrained)
                        {
                            lastIdx = j;
                            float delta;
                            if (totalFixedMaxInlineSize > 0)
                                delta = MulDiv(distributable, K(column.constraint.max_inline_size), totalFixedMaxInlineSize);
                            else
                                delta = distributable / fixedColumnsCount;
                            remaining -= delta;
                            computedSizes[j] = K(column.constraint.max_inline_size) + delta;
                        }
                        else
                        {
                            computedSizes[j] = K(column.constraint.min_inline_size);
                        }
                    }
                    if (remaining != 0 && lastIdx >= 0)
                        computedSizes[lastIdx] += remaining;
                }
                else if (percentColumnsCount > 0)
                {
                    float remaining = distributable;
                    int lastIdx = -1;
                    for (int i = startColumnIndex, j = 0; i < endColumnIndex; i++, j++)
                    {
                        var column = columnConstraints[i];
                        if (column.constraint.is_mergeable || !column.constraint.percent.HasValue)
                            continue;
                        lastIdx = j;
                        float percentInlineSize = column.ResolvePercentInlineSize(targetInlineSize);
                        float delta;
                        if (totalPercent != 0)
                            delta = distributable * column.constraint.percent.Value / totalPercent;
                        else
                            delta = percentColumnsCount > 0 ? distributable / percentColumnsCount : 0;
                        remaining -= delta;
                        computedSizes[j] = percentInlineSize + delta;
                    }
                    if (remaining != 0 && lastIdx >= 0)
                        computedSizes[lastIdx] += remaining;
                }
                break;
            }
        }

        var result = new List<float>(count);
        for (int i = 0; i < count; i++)
            result.Add(computedSizes[i]);
        return result;
    }

    public static List<float> SynchronizeAssignableTableInlineSizeAndColumns(
        float assignableTableInlineSize, bool isFixedLayout, List<TableTypes.Column> columnConstraints)
    {
        if (columnConstraints.Count == 0)
            return new List<float>();
        if (isFixedLayout)
            return SynchronizeAssignableTableInlineSizeAndColumnsFixed(assignableTableInlineSize, columnConstraints);
        return DistributeInlineSizeToComputedInlineSizeAuto(assignableTableInlineSize, 0, columnConstraints.Count, true, columnConstraints);
    }

    public static List<float> SynchronizeAssignableTableInlineSizeAndColumnsFixed(float targetInlineSize, List<TableTypes.Column> columnConstraints)
    {
        bool TreatAsFixed(TableTypes.Column column) =>
            column.IsFixed() && K(column.constraint.max_inline_size) != 0;

        bool IsZeroInlineSizeConstrained(TableTypes.Column column) =>
            column.constraint.is_constrained && K(column.constraint.max_inline_size) == 0;

        int allColumnsCount = 0;
        int percentColumnsCount = 0;
        int autoColumnsCount = 0;
        int fixedColumnsCount = 0;
        int zeroInlineConstrainedCount = 0;

        float totalPercentInlineSize = 0;
        float totalAutoMaxInlineSize = 0;
        float totalFixedInlineSize = 0;
        float assigned = 0;
        var columnSizes = new float[columnConstraints.Count];

        for (int i = 0; i < columnConstraints.Count; i++)
        {
            var column = columnConstraints[i];
            allColumnsCount++;
            if (column.constraint.percent.HasValue)
            {
                percentColumnsCount++;
                totalPercentInlineSize += column.ResolvePercentInlineSize(targetInlineSize);
            }
            else if (TreatAsFixed(column))
            {
                fixedColumnsCount++;
                totalFixedInlineSize += K(column.constraint.max_inline_size);
            }
            else if (IsZeroInlineSizeConstrained(column))
            {
                zeroInlineConstrainedCount++;
            }
            else
            {
                autoColumnsCount++;
                totalAutoMaxInlineSize += K(column.constraint.max_inline_size);
            }
        }

        int lastColumnIndex = -1;

        // Distribute to fixed columns.
        if (fixedColumnsCount > 0)
        {
            float scale = 1.0f;
            bool scaleAvailable = true;
            float targetFixedSize = ClampNeg(targetInlineSize - totalPercentInlineSize);
            bool scaleUp = totalFixedInlineSize < targetFixedSize && autoColumnsCount == 0;
            bool scaleDown = totalFixedInlineSize > targetInlineSize;
            if (scaleUp || scaleDown)
            {
                if (totalFixedInlineSize != 0)
                    scale = targetFixedSize / totalFixedInlineSize;
                else
                    scaleAvailable = false;
            }
            for (int i = 0; i < columnConstraints.Count; i++)
            {
                var column = columnConstraints[i];
                if (!TreatAsFixed(column))
                    continue;
                lastColumnIndex = i;
                if (scaleAvailable)
                    columnSizes[i] = scale * K(column.constraint.max_inline_size);
                else
                    columnSizes[i] = targetInlineSize / fixedColumnsCount;
                assigned += columnSizes[i];
            }
        }
        if (assigned >= targetInlineSize)
        {
            var result = new List<float>(columnSizes.Length);
            for (int i = 0; i < columnSizes.Length; i++)
                result.Add(columnSizes[i]);
            return result;
        }

        // Distribute to percent columns.
        if (percentColumnsCount > 0)
        {
            float scale = 1.0f;
            bool scaleAvailable = true;
            bool scaleUp = totalPercentInlineSize < (targetInlineSize - assigned) && autoColumnsCount == 0;
            bool scaleDown = totalPercentInlineSize > (targetInlineSize - assigned);
            if (scaleUp || scaleDown)
            {
                if (totalPercentInlineSize != 0)
                    scale = (targetInlineSize - assigned) / totalPercentInlineSize;
                else
                    scaleAvailable = false;
            }
            for (int i = 0; i < columnConstraints.Count; i++)
            {
                var column = columnConstraints[i];
                if (!column.constraint.percent.HasValue)
                    continue;
                lastColumnIndex = i;
                if (scaleAvailable)
                    columnSizes[i] = scale * column.ResolvePercentInlineSize(targetInlineSize);
                else
                    columnSizes[i] = (targetInlineSize - assigned) / percentColumnsCount;
                assigned += columnSizes[i];
            }
        }

        // Distribute to auto, and zero inline size columns.
        float distributing = targetInlineSize - assigned;
        bool distributeZeroInlineSize = zeroInlineConstrainedCount == allColumnsCount;
        for (int i = 0; i < columnConstraints.Count; i++)
        {
            var column = columnConstraints[i];
            if (column.constraint.percent.HasValue || TreatAsFixed(column))
                continue;
            if (IsZeroInlineSizeConstrained(column) && !distributeZeroInlineSize)
                continue;
            lastColumnIndex = i;
            float divisor = distributeZeroInlineSize ? zeroInlineConstrainedCount : autoColumnsCount;
            columnSizes[i] = divisor > 0 ? distributing / divisor : 0;
            assigned += columnSizes[i];
        }
        float delta = targetInlineSize - assigned;
        if (lastColumnIndex >= 0)
            columnSizes[lastColumnIndex] += delta;

        var list = new List<float>(columnSizes.Length);
        for (int i = 0; i < columnSizes.Length; i++)
            list.Add(columnSizes[i]);
        return list;
    }

    // ==========================================================================
    // Grid inline min/max and table inline size helpers.
    // ==========================================================================

    public static float ComputeUndistributableTableSpace(List<TableTypes.Column> columnConstraints, float inlineTableBorderPadding, float inlineBorderSpacing)
    {
        int inlineSpaceCount = 2;
        bool isFirstColumn = true;
        foreach (var column in columnConstraints)
        {
            if (!column.is_mergeable)
            {
                if (isFirstColumn)
                    isFirstColumn = false;
                else
                    inlineSpaceCount++;
            }
        }
        return inlineTableBorderPadding + inlineSpaceCount * inlineBorderSpacing;
    }

    public static MinMaxSizes ComputeGridInlineMinMax(bool isFixedLayout, List<TableTypes.Column> columnConstraints,
        float undistributableSpace, bool allowColumnPercentages)
    {
        var minMax = new MinMaxSizes();
        float percentMaxEstimate = 0;
        float nonPercentMaxSum = 0;
        float percentSum = 0;

        foreach (var column in columnConstraints)
        {
            var c = column.constraint;
            if (!TableTypes.IsIndefinite(c.min_inline_size))
            {
                if (isFixedLayout && column.IsFixed())
                    minMax = new MinMaxSizes(minMax.MinSize + K(c.max_inline_size), minMax.MaxSize);
                else
                    minMax = new MinMaxSizes(minMax.MinSize + K(c.min_inline_size), minMax.MaxSize);
                if (c.percent.HasValue && c.percent.Value > 0)
                {
                    if (K(c.max_inline_size) > 0)
                    {
                        float estimate = 100 / c.percent.Value * (K(c.max_inline_size) - c.percent_border_padding);
                        percentMaxEstimate = Math.Max(percentMaxEstimate, estimate);
                    }
                }
                else
                {
                    nonPercentMaxSum += K(c.max_inline_size);
                }
            }
            if (!TableTypes.IsIndefinite(c.max_inline_size))
                minMax = new MinMaxSizes(minMax.MinSize, minMax.MaxSize + K(c.max_inline_size));
            if (c.percent.HasValue)
                percentSum += c.percent.Value;
        }

        percentSum = Math.Min(percentSum, 100.0f);

        if (percentSum > 0 && allowColumnPercentages)
        {
            float sizeFromPercentAndFixed = 0;
            if (nonPercentMaxSum != 0)
            {
                if (percentSum == 100.0f)
                    sizeFromPercentAndFixed = TableTypes.kTableMaxInlineSize;
                else
                    sizeFromPercentAndFixed = (100 / (100 - percentSum)) * nonPercentMaxSum;
            }
            minMax = new MinMaxSizes(minMax.MinSize, Math.Max(minMax.MaxSize, sizeFromPercentAndFixed));
            minMax = new MinMaxSizes(minMax.MinSize, Math.Max(minMax.MaxSize, percentMaxEstimate));
        }

        minMax = new MinMaxSizes(minMax.MinSize, Math.Max(minMax.MinSize, minMax.MaxSize));
        minMax = new MinMaxSizes(minMax.MinSize + undistributableSpace, minMax.MaxSize + undistributableSpace);
        return minMax;
    }

    /// <summary>Empty-table inline size. Mirrors ComputeEmptyTableInlineSize.</summary>
    public static float ComputeEmptyTableInlineSize(bool isFixedInlineSize, bool isInlineAutoBehaviorStretch, ComputedStyle tableStyle,
        float assignableTableInlineSize, float undistributableSpace, TableTypes.Caption captionConstraint, BoxStrut tableBorderPadding,
        bool hasCollapsedBorders)
    {
        bool widthAuto = tableStyle.Width is AutoLength;
        bool minWidthAuto = tableStyle.MinWidth == null || tableStyle.MinWidth is AutoLength;
        if (isFixedInlineSize || isInlineAutoBehaviorStretch || !widthAuto || !minWidthAuto)
            return assignableTableInlineSize + undistributableSpace;
        if (captionConstraint.min_inline_size > 0)
            return Math.Max(captionConstraint.min_inline_size, tableBorderPadding.HorizontalSum);
        if (hasCollapsedBorders)
            return 0;
        return assignableTableInlineSize + tableBorderPadding.HorizontalSum;
    }

    public static float ComputeTableSizeFromColumns(List<TableColumnLocation> columnLocations, BoxStrut tableBorderPadding, float inlineBorderSpacing)
    {
        if (columnLocations.Count == 0)
            return tableBorderPadding.HorizontalSum + inlineBorderSpacing;
        return columnLocations[columnLocations.Count - 1].Offset + columnLocations[columnLocations.Count - 1].Size
            + tableBorderPadding.HorizontalSum + inlineBorderSpacing;
    }

    /// <summary>Computes column offsets from sizes. Mirrors ComputeLocationsFromColumns.</summary>
    public static void ComputeLocationsFromColumns(List<TableTypes.Column> columnConstraints, List<float> columnSizes,
        float inlineBorderSpacing, bool shrinkCollapsed, List<TableColumnLocation> columnLocations, out bool hasCollapsedColumns)
    {
        hasCollapsedColumns = false;
        columnLocations.Clear();
        columnLocations.Capacity = columnConstraints.Count;
        if (columnConstraints.Count == 0)
            return;
        bool isFirstNonCollapsed = true;
        float columnOffset = inlineBorderSpacing;
        for (int i = 0; i < columnConstraints.Count; i++)
        {
            var constraint = columnConstraints[i];
            var location = new TableColumnLocation();
            hasCollapsedColumns |= constraint.is_collapsed;
            float size = i < columnSizes.Count ? columnSizes[i] : TableTypes.kIndefiniteSize;

            if (constraint.is_mergeable && (TableTypes.IsIndefinite(size) || size == 0))
            {
                location.Offset = columnOffset;
                location.Size = 0;
                location.IsCollapsed = true;
            }
            else if (shrinkCollapsed && constraint.is_collapsed)
            {
                location.Offset = columnOffset;
                location.Size = 0;
                location.IsCollapsed = true;
            }
            else
            {
                if (isFirstNonCollapsed)
                    isFirstNonCollapsed = false;
                else
                    columnOffset += inlineBorderSpacing;
                location.Offset = columnOffset;
                location.Size = TableTypes.IsIndefinite(size) ? 0 : size;
                location.IsCollapsed = false;
                columnOffset += location.Size;
            }
            columnLocations.Add(location);
        }
    }

    // ==========================================================================
    // Cell constraint-space building.
    // ==========================================================================

    /// <summary>Creates the constraint space for laying out a table cell.
    /// Mirrors SetupTableCellConstraintSpaceBuilder. 'cellCollapsedBorders' is the
    /// half of each shared grid line that belongs to this cell; the cell's box is
    /// built from it instead of its own border-widths (CSS 2.1 §17.6.2).</summary>
    public static ConstraintSpace SetupTableCellConstraintSpaceBuilder(Element cell, BoxStrut cellBorderPadding,
        List<TableColumnLocation> columnLocations, float cellBlockSize, float percentageInlineSize, int startColumn,
        bool isInitialBlockSizeIndefinite, bool isTableBlockSizeSpecified, bool hasCollapsedBorders,
        ConstraintSpace? space = null, BoxStrut? cellCollapsedBorders = null)
    {
        if (columnLocations.Count == 0)
        {
            var empty = (space?.InheritBuilder(0, TableTypes.IsIndefinite(cellBlockSize) ? float.NaN : cellBlockSize)
                         ?? ConstraintSpace.Builder(0, TableTypes.IsIndefinite(cellBlockSize) ? float.NaN : cellBlockSize));
            empty.SetIsNewFormattingContext(true);
            empty.SetIsTableCell(true);
            empty.SetCollapsedCellBorders(cellCollapsedBorders);
            if (!TableTypes.IsIndefinite(cellBlockSize))
                empty.SetIsFixedBlockSize(true);
            return empty.ToConstraintSpace();
        }

        int colspan = Math.Max(1, CellColspan(cell));
        int start = Math.Clamp(startColumn, 0, columnLocations.Count - 1);
        int endColumn = Math.Min(start + colspan - 1, columnLocations.Count - 1);
        float cellInlineSize = columnLocations[endColumn].Offset + columnLocations[endColumn].Size - columnLocations[start].Offset;

        var builder = space?.InheritBuilder(cellInlineSize, TableTypes.IsIndefinite(cellBlockSize) ? float.NaN : cellBlockSize)
                      ?? ConstraintSpace.Builder(cellInlineSize, TableTypes.IsIndefinite(cellBlockSize) ? float.NaN : cellBlockSize);
        builder.SetIsFixedInlineSize(true);
        builder.SetIsNewFormattingContext(true);
        builder.SetIsTableCell(true);
        builder.SetCollapsedCellBorders(cellCollapsedBorders);
        if (!TableTypes.IsIndefinite(cellBlockSize))
            builder.SetIsFixedBlockSize(true);
        // Percentage resolution inline size comes from the table grid; block sizes
        // of percentages inside cells resolve as indefinite here.
        float pctBlock = TableTypes.IsIndefinite(percentageInlineSize) ? float.NaN : percentageInlineSize;
        builder.SetPercentageResolution(Math.Max(0, TableTypes.IsIndefinite(percentageInlineSize) ? cellInlineSize : percentageInlineSize), pctBlock);
        return builder.ToConstraintSpace();
    }

    // ==========================================================================
    // Row (block-size) computation.
    // ==========================================================================

    /// <summary>Computes the minimum block size of a single row. Mirrors
    /// ComputeMinimumRowBlockSize in table_layout_utils.cc.</summary>
    public static TableTypes.Row ComputeMinimumRowBlockSize(int rowCount, Element row, float cellPercentageInlineSize,
        bool isTableBlockSizeSpecified, List<TableColumnLocation> columnLocations, TableBorders tableBorders,
        int startRowIndex, int rowIndex, int sectionIndex, bool isSectionCollapsed,
        List<TableTypes.CellBlockConstraint> cellBlockConstraints, List<TableTypes.RowspanCell> rowspanCells,
        TableBorders.ColspanCellTabulator colspanCellTabulator)
    {
        bool hasCollapsedBorders = tableBorders.IsCollapsed;
        float maxCellBlockSize = 0;
        float? rowPercent = null;
        bool isConstrained = false;
        bool hasRowspanStart = false;
        int startCellIndex = cellBlockConstraints.Count;

        foreach (var child in row.Children)
        {
            if (child is not Element cell)
                continue;
            var cellStyle = cell.ComputedStyle;
            if (cellStyle == null || cellStyle.Display == DisplayType.None)
            {
                colspanCellTabulator.ProcessCell(cell);
                continue;
            }

            colspanCellTabulator.FindNextFreeColumn();
            int currentColumn = colspanCellTabulator.CurrentColumn;
            int rowspan = CellRowspan(cell);
            int effectiveRowspan = rowspan;
            if (effectiveRowspan > 1)
            {
                int maxRows = rowCount - (rowIndex - startRowIndex);
                if (maxRows < 1) maxRows = 1;
                effectiveRowspan = Math.Min(maxRows, effectiveRowspan);
            }
            bool hasEffectiveRowspan = effectiveRowspan > 1;
            // A collapsed cell owns only half of each shared edge, and that half
            // belongs to its border box (CSS 2.1 §17.6.2).
            var collapsedEdge = hasCollapsedBorders
                ? tableBorders.CellBorder(rowIndex, currentColumn, effectiveRowspan,
                    CellColspan(cell), sectionIndex)
                : default(BoxStrut?);
            var cellBorderPadding = TableTypes.ComputeCellBorderPadding(cell, null, collapsedEdge);

            var cellSpace = SetupTableCellConstraintSpaceBuilder(cell, cellBorderPadding, columnLocations,
                TableTypes.kIndefiniteSize, cellPercentageInlineSize, currentColumn,
                /* isInitialBlockSizeIndefinite */ true, isTableBlockSizeSpecified, hasCollapsedBorders,
                cellCollapsedBorders: collapsedEdge);

            var layoutResult = new BlockLayoutAlgorithm(cell, cellSpace).Layout();
            // A cell is a block, so an empty one has no line boxes and contributes no
            // height: a table whose only cell is empty collapses to 0x0 (measured against
            // Chrome). Keeping a strut here inflated every empty row by one line-height.
            float fragmentBlockSize = K(layoutResult.Fragment.BlockSize);

            bool hasDescendantDependingOnPercentage = false;
            bool heightIsFixed = cellStyle.Height.IsFixed();

            var constraint = new TableTypes.CellBlockConstraint(fragmentBlockSize, cellBorderPadding, currentColumn,
                effectiveRowspan, heightIsFixed, hasDescendantDependingOnPercentage);
            colspanCellTabulator.ProcessCell(cell);
            cellBlockConstraints.Add(constraint);
            isConstrained |= constraint.is_constrained && !hasEffectiveRowspan;
            hasRowspanStart |= hasEffectiveRowspan;

            // Compute the cell's css block size.
            float? cellCssBlockSize = null;
            float? cellCssPercent = null;
            var blockSize = cellStyle.Height;
            if (blockSize.IsPercent())
            {
                cellCssPercent = blockSize.PercentValue();
            }
            else if (blockSize.IsFixed())
            {
                float borderBlock = cellBorderPadding.VerticalSum;
                float value = blockSize.FixedValue();
                cellCssBlockSize = cellStyle.BoxSizing == BoxSizingType.BorderBox
                    ? Math.Max(borderBlock, value)
                    : borderBlock + value;
            }

            if (!hasEffectiveRowspan)
            {
                if (cellCssBlockSize.HasValue || cellCssPercent.HasValue)
                    isConstrained = true;
                if (cellCssPercent.HasValue)
                    rowPercent = Math.Max(rowPercent ?? 0, cellCssPercent.Value);
                maxCellBlockSize = Math.Max(maxCellBlockSize, Math.Max(constraint.block_size, cellCssBlockSize ?? 0));
            }
            else
            {
                float minBlockSize = constraint.block_size;
                if (cellCssBlockSize.HasValue)
                    minBlockSize = Math.Max(minBlockSize, cellCssBlockSize.Value);
                rowspanCells.Add(new TableTypes.RowspanCell(rowIndex, effectiveRowspan, cell, cellBorderPadding, currentColumn)
                {
                    block_size = minBlockSize
                });
            }
        }

        // Apply the row's css block size.
        var rowStyle = row.ComputedStyle;
        if (rowStyle != null)
        {
            var rowHeight = rowStyle.Height;
            if (rowHeight.IsPercent())
            {
                isConstrained = true;
                rowPercent = Math.Max(rowPercent ?? 0, rowHeight.PercentValue());
            }
            else if (rowHeight.IsFixed())
            {
                isConstrained = true;
                maxCellBlockSize = Math.Max(rowHeight.FixedValue(), maxCellBlockSize);
            }
        }

        var tabulator = new RowBaselineTabulator();
        float rowBlockSize = tabulator.ComputeRowBlockSize(maxCellBlockSize);

        var result = new TableTypes.Row(row, startRowIndex, rowIndex, isSectionCollapsed)
        {
            is_constrained = isConstrained,
            has_rowspan_start = hasRowspanStart,
            percent_block_size = rowPercent ?? TableTypes.kIndefiniteSize,
        };
        result.block_size = rowBlockSize;
        if (rowPercent.HasValue && !rowPercent.Value.Equals(0f))
            result.is_constrained = true;
        for (int i = startCellIndex; i < cellBlockConstraints.Count; i++)
            result.AddCell(cellBlockConstraints[i]);
        return result;
    }

    /// <summary>Computes minimum block sizes for all rows of a section.
    /// Mirrors ComputeSectionMinimumRowBlockSizes.</summary>
    public static void ComputeSectionMinimumRowBlockSizes(Element section, float cellPercentageInlineSize,
        bool isTableBlockSizeSpecified, List<TableColumnLocation> columnLocations, TableBorders tableBorders,
        float blockBorderSpacing, int sectionIndex, TableConstraintSpaceData data)
    {
        var rows = data.rows;
        var sectionRows = GetSectionRows(section);
        int totalRowCount = sectionRows.Count;
        int startRow = rows.Count;
        int currentRow = startRow;
        var rowspanCells = new List<TableTypes.RowspanCell>();
        float sectionBlockSize = 0;
        float totalRowPercent = 0;
        var colspanCellTabulator = new TableBorders.ColspanCellTabulator();
        bool isSectionCollapsed = section.ComputedStyle?.Visibility == VisibilityType.Collapse;

        for (int i = 0; i < sectionRows.Count; i++)
        {
            var row = sectionRows[i];
            colspanCellTabulator.StartRow();
            var rowConstraint = ComputeMinimumRowBlockSize(totalRowCount, row, cellPercentageInlineSize, isTableBlockSizeSpecified,
                columnLocations, tableBorders, startRow, currentRow++, sectionIndex, isSectionCollapsed,
                data.cell_block_constraints, rowspanCells, colspanCellTabulator);

            if (!TableTypes.IsIndefinite(rowConstraint.percent_block_size))
            {
                rowConstraint.percent_block_size = Math.Min(100.0f - totalRowPercent, rowConstraint.percent_block_size);
                totalRowPercent += rowConstraint.percent_block_size;
            }
            rows.Add(rowConstraint);
            sectionBlockSize += rowConstraint.block_size;
            colspanCellTabulator.EndRow();
        }

        // Redistribute rowspanned cell block sizes.
        rowspanCells.Sort((a, b) => a.start_row.CompareTo(b.start_row));
        foreach (var rowspanCell in rowspanCells)
            DistributeRowspanCellToRows(rowspanCell, blockBorderSpacing, rows);

        int blockSpacingCount = currentRow == startRow || currentRow - startRow == 0 ? 0 : currentRow - startRow - 1;
        sectionBlockSize += blockBorderSpacing * blockSpacingCount;

        // Redistribute the section's css block size.
        var sectionStyle = section.ComputedStyle;
        if (sectionStyle != null && sectionStyle.Height.IsFixed())
        {
            float sectionFixedBlockSize = sectionStyle.Height.FixedValue();
            if (sectionFixedBlockSize > sectionBlockSize)
            {
                DistributeSectionFixedBlockSizeToRows(startRow, currentRow - startRow, sectionFixedBlockSize,
                    blockBorderSpacing, sectionFixedBlockSize, rows);
                sectionBlockSize = sectionFixedBlockSize;
            }
        }

        data.sections.Add(TableTypes.CreateSection(section, startRow, currentRow - startRow, sectionBlockSize));
    }

    // ==========================================================================
    // Rowspans / excess block-size distribution.
    // ==========================================================================

    public static void DistributeRowspanCellToRows(TableTypes.RowspanCell rowspanCell, float borderBlockSpacing, List<TableTypes.Row> rows)
    {
        if (rowspanCell.rowspan <= 1)
            return;
        DistributeExcessBlockSizeToRows(rowspanCell.start_row, rowspanCell.rowspan, rowspanCell.block_size,
            /* isRowspanDistribution */ true, borderBlockSpacing, TableTypes.kIndefiniteSize, rows);
    }

    public static void DistributeSectionFixedBlockSizeToRows(int startRow, int rowspan, float sectionFixedBlockSize,
        float borderBlockSpacing, float percentageResolutionBlockSize, List<TableTypes.Row> rows)
    {
        DistributeExcessBlockSizeToRows(startRow, rowspan, sectionFixedBlockSize,
            /* isRowspanDistribution */ false, borderBlockSpacing, percentageResolutionBlockSize, rows);
    }

    /// <summary>Distributes excess block size to rows, per css-tables-3.
    /// Mirrors DistributeExcessBlockSizeToRows.</summary>
    public static void DistributeExcessBlockSizeToRows(int startRowIndex, int rowCount, float desiredBlockSize,
        bool isRowspanDistribution, float borderBlockSpacing, float percentageResolutionBlockSize, List<TableTypes.Row> rows)
    {
        if (desiredBlockSize < 0)
            return;
        if (rowCount == 0)
            return;

        int endRowIndex = Math.Min(startRowIndex + rowCount, rows.Count);
        if (startRowIndex >= endRowIndex)
            return;

        float RowBlockSizeDeficit(TableTypes.Row row)
        {
            if (TableTypes.IsIndefinite(row.percent_block_size) || TableTypes.IsIndefinite(percentageResolutionBlockSize))
                return 0;
            return ClampNeg(row.percent_block_size * percentageResolutionBlockSize / 100 - K(row.block_size));
        }

        var rowsWithOriginatingRowspan = new List<int>();
        var percentRowsWithDeficit = new List<int>();
        var unconstrainedNonEmptyRows = new List<int>();
        var emptyRows = new List<int>();
        var nonEmptyRows = new List<int>();
        var unconstrainedEmptyRows = new List<int>();
        int constrainedNonEmptyRowCount = 0;

        float totalBlockSize = 0;
        float percentBlockSizeDeficit = 0;
        float unconstrainedNonEmptyRowBlockSize = 0;

        for (int index = startRowIndex; index < endRowIndex; index++)
        {
            var row = rows[index];
            float rowBlockSize = K(index < rows.Count ? rows[index].block_size : 0);
            totalBlockSize += rowBlockSize;

            bool isRowWithOriginatingRowspan = isRowspanDistribution && index != startRowIndex && rows[index].has_rowspan_start;
            if (isRowWithOriginatingRowspan)
                rowsWithOriginatingRowspan.Add(index);

            bool isRowEmpty = rowBlockSize == 0;

            if (!TableTypes.IsIndefinite(row.percent_block_size) && row.percent_block_size != 0
                && !TableTypes.IsIndefinite(percentageResolutionBlockSize))
            {
                float deficit = RowBlockSizeDeficit(row);
                if (deficit != 0)
                {
                    percentRowsWithDeficit.Add(index);
                    percentBlockSizeDeficit += deficit;
                    isRowEmpty = false;
                }
            }

            bool isRowConstrained = row.is_constrained &&
                (TableTypes.IsIndefinite(row.percent_block_size) || !TableTypes.IsIndefinite(percentageResolutionBlockSize));

            if (isRowEmpty)
            {
                emptyRows.Add(index);
                if (!isRowConstrained)
                    unconstrainedEmptyRows.Add(index);
            }
            else
            {
                nonEmptyRows.Add(index);
                if (isRowConstrained)
                    constrainedNonEmptyRowCount++;
                else
                {
                    unconstrainedNonEmptyRows.Add(index);
                    unconstrainedNonEmptyRowBlockSize += rowBlockSize;
                }
            }
        }

        float distributableBlockSize = (desiredBlockSize - borderBlockSpacing * (rowCount - 1)) - totalBlockSize;
        if (distributableBlockSize <= 0)
            return;

        // Step 1: percentage rows grow to no more than their percentage size.
        if (percentRowsWithDeficit.Count > 0)
        {
            float percentDistributable = Math.Min(percentBlockSizeDeficit, distributableBlockSize);
            float remaining = percentDistributable;
            for (int i = 0; i < percentRowsWithDeficit.Count; i++)
            {
                int index = percentRowsWithDeficit[i];
                var row = rows[index];
                float delta = MulDiv(percentDistributable, RowBlockSizeDeficit(row), percentBlockSizeDeficit);
                row.block_size = K(row.block_size) + delta;
                distributableBlockSize -= delta;
                remaining -= delta;
            }
            var lastRow = rows[percentRowsWithDeficit[percentRowsWithDeficit.Count - 1]];
            lastRow.block_size = K(lastRow.block_size) + remaining;
            distributableBlockSize -= remaining;
            if (distributableBlockSize <= 0)
                return;
        }

        // Step 2: distribute to rows that have an originating rowspan.
        if (rowsWithOriginatingRowspan.Count > 0)
        {
            float remaining = distributableBlockSize;
            for (int i = 0; i < rowsWithOriginatingRowspan.Count; i++)
            {
                int index = rowsWithOriginatingRowspan[i];
                var row = rows[index];
                float delta = distributableBlockSize / rowsWithOriginatingRowspan.Count;
                row.block_size = K(row.block_size) + delta;
                remaining -= delta;
            }
            var last = rows[rowsWithOriginatingRowspan[rowsWithOriginatingRowspan.Count - 1]];
            last.block_size = Math.Max(K(last.block_size) + remaining, 0);
            return;
        }

        // Step 3: unconstrained non-empty rows grow proportionally.
        if (unconstrainedNonEmptyRows.Count > 0)
        {
            float remaining = distributableBlockSize;
            for (int i = 0; i < unconstrainedNonEmptyRows.Count; i++)
            {
                int index = unconstrainedNonEmptyRows[i];
                var row = rows[index];
                float delta = MulDiv(distributableBlockSize, K(row.block_size), unconstrainedNonEmptyRowBlockSize);
                row.block_size = K(row.block_size) + delta;
                remaining -= delta;
            }
            var last = rows[unconstrainedNonEmptyRows[unconstrainedNonEmptyRows.Count - 1]];
            last.block_size = K(last.block_size) + remaining;
            return;
        }

        // Step 4: empty row distribution.
        if (emptyRows.Count > 0)
        {
            bool hasOnlyEmptyRows = emptyRows.Count == rowCount;
            if (isRowspanDistribution)
            {
                if (hasOnlyEmptyRows)
                {
                    rows[emptyRows[emptyRows.Count - 1]].block_size = K(rows[emptyRows[emptyRows.Count - 1]].block_size) + distributableBlockSize;
                    return;
                }
            }
            else if (hasOnlyEmptyRows || (emptyRows.Count + constrainedNonEmptyRowCount == rowCount))
            {
                float remaining = distributableBlockSize;
                var rowsToGrow = unconstrainedEmptyRows.Count > 0 ? unconstrainedEmptyRows : emptyRows;
                for (int i = 0; i < rowsToGrow.Count; i++)
                {
                    int index = rowsToGrow[i];
                    float delta = distributableBlockSize / rowsToGrow.Count;
                    rows[index].block_size = delta;
                    remaining -= delta;
                }
                var lastRow = rows[rowsToGrow[rowsToGrow.Count - 1]];
                lastRow.block_size = K(lastRow.block_size) + remaining;
                return;
            }
        }

        // Step 5: grow non-empty rows proportionally.
        if (nonEmptyRows.Count > 0)
        {
            float remaining = distributableBlockSize;
            for (int i = 0; i < nonEmptyRows.Count; i++)
            {
                int index = nonEmptyRows[i];
                var row = rows[index];
                float delta = MulDiv(distributableBlockSize, K(row.block_size), totalBlockSize);
                row.block_size = K(row.block_size) + delta;
                remaining -= delta;
            }
            var last = rows[nonEmptyRows[nonEmptyRows.Count - 1]];
            last.block_size = K(last.block_size) + remaining;
        }
    }

    /// <summary>Simplified version of DistributeTableBlockSizeToSections:
    /// grows sections (and their rows) when the table has extra block size.</summary>
    public static void DistributeTableBlockSizeToSections(float borderBlockSpacing, float tableBlockSize,
        List<TableTypes.Section> sections, List<TableTypes.Row> rows)
    {
        if (sections.Count == 0)
            return;
        float undistributable = (sections.Count + 1) * borderBlockSpacing;
        float distributable = ClampNeg(tableBlockSize - undistributable);
        float minimum = 0;
        var originalSizes = new float[sections.Count];
        for (int i = 0; i < sections.Count; i++)
        {
            originalSizes[i] = sections[i].block_size;
            minimum += sections[i].block_size;
        }
        if (distributable <= minimum)
            return;

        float deficit = distributable - minimum;
        float remaining = deficit;
        for (int i = 0; i < sections.Count; i++)
        {
            float delta = minimum > 0 ? deficit * (sections[i].block_size / minimum) : deficit / sections.Count;
            sections[i].block_size += delta;
            remaining -= delta;
        }
        sections[sections.Count - 1].block_size += remaining;

        for (int i = 0; i < sections.Count; i++)
        {
            if (sections[i].block_size > originalSizes[i])
            {
                DistributeExcessBlockSizeToRows(sections[i].start_row_index, sections[i].row_count, sections[i].block_size,
                    /* isRowspanDistribution */ false, borderBlockSpacing, sections[i].block_size, rows);
            }
        }
    }

    /// <summary>Computes the assigned block size of a cell given its rowspan.
    /// Mirrors ComputeCellBlockSize.</summary>
    public static float ComputeCellBlockSize(TableTypes.CellBlockConstraint cellBlockConstraint, List<TableTypes.Row> rows,
        int rowIndex, float blockBorderSpacing, bool isTableBlockSizeSpecified)
    {
        float cellBlockSize = 0;
        int endRow = Math.Min(rowIndex + cellBlockConstraint.rowspan, rows.Count);
        if (rowIndex >= 0 && rowIndex < rows.Count && !rows[rowIndex].is_collapsed)
        {
            for (int i = rowIndex; i < endRow; i++)
            {
                if (rows[i].is_collapsed)
                    continue;
                cellBlockSize += K(rows[i].block_size);
                if (i != rowIndex)
                    cellBlockSize += blockBorderSpacing;
            }
        }
        return cellBlockSize;
    }

    // ==========================================================================
    // RowBaselineTabulator (fallback-only path: the engine has no baseline
    // alignment machinery, so baselines never contribute to row sizing).
    // ==========================================================================

    public class RowBaselineTabulator
    {
        public void ProcessCell(float cellBlockSize, bool isRowspanned, bool descendantDependsOnPercentageBlockSize)
        {
        }

        public void ProcessFallback(float cellBlockEndBorderPadding, bool descendantDependsOnPercentageBlockSize)
        {
        }

        /// <summary>Returns the bigger of the css/measured row size and any
        /// baseline-derived size; with no baselines this is |maxCellBlockSize|.</summary>
        public float ComputeRowBlockSize(float maxCellBlockSize) => Math.Max(0, maxCellBlockSize);

        public float ComputeBaseline(float rowBlockSize) => 0;

        public bool BaselineDependsOnPercentageBlockDescendant() => false;
    }
}