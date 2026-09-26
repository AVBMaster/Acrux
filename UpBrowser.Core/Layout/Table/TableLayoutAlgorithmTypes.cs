using System;
using System.Collections.Generic;
using UpBrowser.Core.Dom;
using UpBrowser.Core.Layout.Geometry;

namespace UpBrowser.Core.Layout.Table;

/// <summary>
/// Constants and value types for the table layout algorithm. Mirrors
/// TableLayoutAlgorithmTypes in table_layout_algorithm_types.h/.cc.
/// </summary>
public static class TableTypes
{
    public const float kTableMaxInlineSize = 1000000.0f;
    public const float kIndefiniteSize = float.NaN;

    public static bool IsIndefinite(float value) => float.IsNaN(value);
    public static bool IsKnown(float value) => !float.IsNaN(value);
    public static bool IsZero(float value) => value == 0;

    // ==========================================================================
    // Length / box helpers (mirroring style/length_utils helpers used by the
    // table algorithm).
    // ==========================================================================

    public static bool IsFixed(this Length? length) => length is PixelLength;
    public static bool IsPercent(this Length? length) => length is PercentLength;
    public static float FixedValue(this Length? length) => length is PixelLength pl ? pl.Value : 0;
    public static float PercentValue(this Length? length) => length is PercentLength pcl ? pcl.Value * 100f : 0;
    public static bool IsSpecified(this Length? length) => length is not null and not AutoLength;

    public static float MulDiv(float a, float b, float c) => c != 0 ? a * b / c : 0;
    public static float ClampNegativeToZero(float v) => Math.Max(0, v);

    public static BoxStrut ClampNegativeToZero(this BoxStrut strut) =>
        new(ClampNegativeToZero(strut.Top), ClampNegativeToZero(strut.Right),
            ClampNegativeToZero(strut.Bottom), ClampNegativeToZero(strut.Left));

    // ==========================================================================
    // CellInlineConstraint.
    // ==========================================================================

    /// <summary>
    /// The inline size constraint of a table cell. Mirrors
    /// TableTypes::CellInlineConstraint.
    /// </summary>
    public class CellInlineConstraint
    {
        public float min_inline_size = kIndefiniteSize;
        public float? percent;
        public float percent_border_padding = 0;
        public float max_inline_size = kIndefiniteSize;
        public bool is_constrained;
        public bool is_mergeable;
        public bool is_initial_constraint;
        public bool is_collapsed;
        public bool is_table_fixed;

        public void Encompass(CellInlineConstraint other)
        {
            is_constrained |= other.is_constrained;
            is_collapsed |= other.is_collapsed;
            is_table_fixed |= other.is_table_fixed;
            min_inline_size = MaxMinSize(min_inline_size, other.min_inline_size);
            if (percent.HasValue && other.percent.HasValue)
                percent = Math.Max(percent.Value, other.percent.Value);
            else if (!percent.HasValue)
                percent = other.percent;
            percent_border_padding = Math.Max(percent_border_padding, other.percent_border_padding);
            max_inline_size = MaxMinSize(max_inline_size, other.max_inline_size);
            if (other.is_initial_constraint)
                is_initial_constraint = true;
        }

        private static float MaxMinSize(float a, float b) =>
            IsIndefinite(a) ? b : IsIndefinite(b) ? a : Math.Max(a, b);
    }

    // ==========================================================================
    // ColspanCell.
    // ==========================================================================

    public class ColspanCell
    {
        public int start_column;
        public int span;
        public Element? cell;
        public CellInlineConstraint constraint = new();

        public ColspanCell() { }

        public ColspanCell(int startColumn, int span, Element cell)
        {
            start_column = startColumn;
            this.span = span;
            this.cell = cell;
        }

        public void Encompass(CellInlineConstraint other) => constraint.Encompass(other);

        public void Encompass(ColspanCell other)
        {
            if (other.cell == null) return;
            if (cell == null)
            {
                cell = other.cell;
                constraint.Encompass(other.constraint);
                return;
            }
            if (other.constraint.is_initial_constraint)
            {
                constraint.Encompass(other.constraint);
                return;
            }
            var otherStyle = other.cell.ComputedStyle;
            var cellStyle = cell.ComputedStyle;
            if (otherStyle == null || cellStyle == null) return;
            bool otherFixed = otherStyle.Width.IsFixed() || otherStyle.MinWidth.IsFixed();
            bool cellFixed = cellStyle.Width.IsFixed() || cellStyle.MinWidth.IsFixed();
            if (otherFixed && !cellFixed)
            {
                cell = other.cell;
                constraint.Encompass(other.constraint);
            }
        }

        public bool IsMergeable()
        {
            if (cell == null) return true;
            var style = cell.ComputedStyle;
            return style != null && style.Visibility == VisibilityType.Collapse;
        }

        public void Merge()
        {
            cell = null;
            constraint = new CellInlineConstraint();
        }
    }

    // ==========================================================================
    // Column.
    // ==========================================================================

    public class Column
    {
        public CellInlineConstraint constraint = new();
        public int start_column;
        public int span;
        public bool is_collapsed;
        public bool is_table_fixed;
        public bool is_mergeable;

        public Column(CellInlineConstraint constraint, int startColumn, int span)
        {
            this.constraint = constraint;
            start_column = startColumn;
            this.span = span;
            is_collapsed = constraint.is_collapsed;
            is_table_fixed = constraint.is_table_fixed;
            is_mergeable = constraint.is_mergeable;
        }

        public bool IsFixed() =>
            constraint.percent.HasValue || constraint.min_inline_size > 0 ||
            constraint.max_inline_size != TableTypes.kIndefiniteSize ||
            constraint.is_mergeable;

        public bool IsCollapsed() => is_collapsed;

        public bool IsMergeable() => is_mergeable;

        public float ResolvePercentInlineSize(float percentageResolutionInlineSize)
        {
            var computedPercentageInlineSize =
                (constraint.percent ?? 0) / 100f * percentageResolutionInlineSize +
                constraint.percent_border_padding;
            return MaxMinSize(constraint.min_inline_size, computedPercentageInlineSize);
        }

        public void Encompass(Column other)
        {
            constraint.Encompass(other.constraint);
            is_collapsed |= other.is_collapsed;
            is_table_fixed |= other.is_table_fixed;
            is_mergeable &= other.is_mergeable;
        }

        public void Merge()
        {
            constraint.min_inline_size = 0;
            constraint.percent = null;
            constraint.percent_border_padding = 0;
            constraint.max_inline_size = 0;
            constraint.is_constrained = false;
            constraint.is_initial_constraint = false;
            is_mergeable = false;
        }

        private static float MaxMinSize(float a, float b) =>
            IsIndefinite(a) ? b : IsIndefinite(b) ? a : Math.Max(a, b);
    }

    // ==========================================================================
    // CellBlockConstraint.
    // ==========================================================================

    public class CellBlockConstraint
    {
        public float block_size;
        public BoxStrut borders;
        public int column_index;
        public int rowspan;
        public bool is_constrained;
        public bool has_descendant_that_depends_on_percentage_block_size;

        public CellBlockConstraint(float blockSize, BoxStrut cellBorders, int columnIndex,
            int rowspan, bool isConstrained, bool hasDescendantThatDependsOnPercentageBlockSize)
        {
            block_size = blockSize;
            borders = cellBorders;
            column_index = columnIndex;
            this.rowspan = rowspan;
            is_constrained = isConstrained;
            has_descendant_that_depends_on_percentage_block_size = hasDescendantThatDependsOnPercentageBlockSize;
        }
    }

    // ==========================================================================
    // RowspanCell.
    // ==========================================================================

    public class RowspanCell
    {
        public int start_row;
        public int rowspan;
        public Element? cell;
        public BoxStrut cell_borders;
        public int column_index;
        public float block_size = kIndefiniteSize;

        public RowspanCell(int startRow, int rowspan, Element cell, BoxStrut cellBorders, int columnIndex)
        {
            start_row = startRow;
            this.rowspan = rowspan;
            this.cell = cell;
            cell_borders = cellBorders;
            column_index = columnIndex;
        }

        public bool IsInRow(int rowIndex) => rowIndex >= start_row && rowIndex < start_row + rowspan;

        public bool IsInSection(int sectionStartRow, int sectionEndRow) =>
            start_row < sectionEndRow && start_row + rowspan > sectionStartRow;

        public void SetBlockSize(float blockSize) => block_size = blockSize;

        public bool DependsOnPercentageBlockSize()
        {
            var style = cell?.ComputedStyle;
            return style != null && style.Height.IsPercent() && !style.BorderCollapse;
        }
    }

    // ==========================================================================
    // Row.
    // ==========================================================================

    public class Row
    {
        public readonly Element? node;
        public readonly int start_row_index;
        public readonly int row_index;
        public readonly bool is_collapsed;
        public float block_size = kIndefiniteSize;
        public float percent_block_size = kIndefiniteSize;
        public bool is_constrained;
        public bool has_rowspan_start;
        public bool has_descendant_that_depends_on_percentage_block_size;
        public float baseline;
        public bool has_baseline;
        public List<CellBlockConstraint> cell_block_constraints = new();

        public Row(Element? node, int startRowIndex, int rowIndex, bool hasCollapsedSection)
        {
            this.node = node;
            start_row_index = startRowIndex;
            row_index = rowIndex;
            is_collapsed = hasCollapsedSection || (node?.ComputedStyle?.Visibility ?? VisibilityType.Visible) == VisibilityType.Collapse;
        }

        public int RowIndex() => row_index;

        public bool IsCollapsed() => is_collapsed;

        public void AddCell(CellBlockConstraint cellBlockConstraint)
        {
            cell_block_constraints.Add(cellBlockConstraint);
        }

        public float ComputeBlockSize(List<CellBlockConstraint> cellBlockConstraints, List<RowspanCell> rowspanCells)
        {
            float row_block_size = kIndefiniteSize;
            for (int i = 0; i < cell_block_constraints.Count; i++)
            {
                var cell_block_constraint = cell_block_constraints[i];
                // If the cell has a rowspan, we don't include it in the row's
                // block size calculation.
                if (cell_block_constraint.rowspan != 1)
                    continue;
                row_block_size = MaxMinSize(row_block_size, cell_block_constraint.block_size);
            }

            foreach (var rowspan_cell in rowspanCells)
            {
                if (rowspan_cell.IsInRow(row_index))
                    row_block_size = MaxMinSize(row_block_size, rowspan_cell.block_size);
            }

            // Check if the row has a specified block size.
            float row_specified_block_size = kIndefiniteSize;
            float row_percent_block_size = kIndefiniteSize;
            var style = node?.ComputedStyle;
            if (style != null)
            {
                var block_size = style.Height;
                if (block_size.IsSpecified())
                {
                    if (block_size.IsFixed())
                        row_specified_block_size = block_size.FixedValue();
                    else if (block_size.IsPercent())
                        row_percent_block_size = block_size.PercentValue();
                }
            }

            // If the row has a specified block size, and it is larger than the
            // content, use that.
            if (IsKnown(row_specified_block_size) && row_specified_block_size > row_block_size)
                row_block_size = row_specified_block_size;

            if (IsKnown(row_percent_block_size))
                percent_block_size = row_percent_block_size;

            return row_block_size;
        }

        private static float MaxMinSize(float a, float b) =>
            IsIndefinite(a) ? b : IsIndefinite(b) ? a : Math.Max(a, b);
    }

    // ==========================================================================
    // Section.
    // ==========================================================================

    public class Section
    {
        public readonly Element? node;
        public readonly int start_row_index;
        public int row_count;
        public float block_size;
        public List<int> row_offsets = new();

        public Section(Element? node, int startRowIndex, int rowCount, float blockSize)
        {
            this.node = node;
            start_row_index = startRowIndex;
            row_count = rowCount;
            block_size = blockSize;
        }

        public void AddRow(Row row)
        {
            row_offsets.Add(row.start_row_index + row.row_index);
        }
    }

    // ==========================================================================
    // Caption.
    // ==========================================================================

    public class Caption
    {
        public float min_inline_size;
        public float max_inline_size;
        public float block_size;

        public Caption(MinMaxSizes minMax, float blockSize)
        {
            min_inline_size = minMax.MinSize;
            max_inline_size = minMax.MaxSize;
            block_size = blockSize;
        }

        public void Encompass(Caption other)
        {
            min_inline_size = Math.Max(min_inline_size, other.min_inline_size);
            max_inline_size = Math.Max(max_inline_size, other.max_inline_size);
            block_size = Math.Max(block_size, other.block_size);
        }
    }

    // ==========================================================================
    // Factory helpers (mirrors the Create* functions in table_layout_algorithm_types.cc).
    // ==========================================================================

    /// <summary>True when the cell inherits `visibility: collapse` from its own
    /// &lt;tr&gt; (row-level collapse) rather than declaring it itself.</summary>
    private static bool InCollapsedRow(Element cell)
    {
        var row = cell.ParentElement;
        return row != null && row.TagName.Equals("TR", StringComparison.OrdinalIgnoreCase)
            && row.ComputedStyle?.Visibility == VisibilityType.Collapse;
    }

    public static CellInlineConstraint CreateCellInlineConstraint(
        List<Column> columnConstraints, int startColumn, int span, Element cell,
        TableBorders? collapsedEdges = null, int row = 0)
    {
        CellInlineConstraint constraint = new();        // |constraint.is_initial_constraint| is reset by this loop.
        var style = cell.ComputedStyle;
        // Collapsed cells own half of each shared edge, and the specified cell
        // width is a content width on top of that (CSS 2.1 §17.6.2).
        var edge = collapsedEdges is { IsCollapsed: true }
            ? collapsedEdges.CellBorder(row, startColumn, 1, span, collapsedEdges.SectionIndexOf(row))
            : default(BoxStrut?);
        var borderPadding = ComputeCellBorderPadding(cell, null, edge);

        bool is_constrained = false;
        // If this cell has a colspan, we merge the widths of all the spanned columns.
        if (span > 1)
        {
            int end_column = Math.Min(startColumn + span, columnConstraints.Count);
            for (int column_index = startColumn; column_index < end_column; column_index++)
            {
                constraint.Encompass(columnConstraints[column_index].constraint);
                is_constrained |= columnConstraints[column_index].constraint.is_constrained;
            }
        }

        // Choose the type of width to use.
        Length? width = null;
        if (style != null)
        {
            if (style.Width.IsSpecified())
                width = style.Width;
            else if (style.MinWidth.IsSpecified())
                width = style.MinWidth;
            else if (style.MaxWidth.IsSpecified())
                width = style.MaxWidth;
        }

        if (width.IsFixed())
        {
            constraint.min_inline_size = width.FixedValue() + borderPadding.HorizontalSum;
            constraint.max_inline_size = constraint.min_inline_size;
        }
        else if (width.IsPercent())
        {
            constraint.percent = width.PercentValue();
            constraint.percent_border_padding = borderPadding.HorizontalSum;
        }

        constraint.is_constrained = is_constrained || (style != null && style.Width.IsSpecified());
        // `visibility: collapse` on a cell removes its COLUMN; on a row it removes
        // the ROW only. Computed visibility is inherited, so a cell inside a
        // collapsed row must not be mistaken for a collapsed column, which would
        // shrink every column of the table to zero (CSS 2.1 §17.6.3).
        constraint.is_collapsed = style != null && style.Visibility == VisibilityType.Collapse
            && !InCollapsedRow(cell);
        constraint.is_table_fixed = style != null && style.TableLayout == "fixed";

        // Colspan cells are treated as a single column (the one that spans them).
        // We reset the min/max sizes, and encompass the widths of the spanned columns.
        if (span > 1)
        {
            constraint.min_inline_size = kIndefiniteSize;
            constraint.max_inline_size = kIndefiniteSize;
            int end_column = Math.Min(startColumn + span, columnConstraints.Count);
            for (int column_index = startColumn; column_index < end_column; column_index++)
            {
                var other = columnConstraints[column_index].constraint;
                constraint.min_inline_size = MaxMinSize(constraint.min_inline_size, other.min_inline_size);
                constraint.max_inline_size = MaxMinSize(constraint.max_inline_size, other.max_inline_size);
                if (other.percent.HasValue)
                {
                    if (constraint.percent.HasValue)
                        constraint.percent = Math.Max(constraint.percent.Value, other.percent.Value);
                    else
                        constraint.percent = other.percent;
                    constraint.percent_border_padding = Math.Max(constraint.percent_border_padding, other.percent_border_padding);
                }
            }
        }

        return constraint;
    }

    public static Column CreateColumn(CellInlineConstraint constraint, int startColumn, int span) =>
        new(constraint, startColumn, span);

    public static Row CreateRow(Element node, int startRowIndex, int rowIndex, bool hasCollapsedSection) =>
        new(node, startRowIndex, rowIndex, hasCollapsedSection);

    public static Section CreateSection(Element node, int startRowIndex, int rowCount, float blockSize) =>
        new(node, startRowIndex, rowCount, blockSize);

    private static float MaxMinSize(float a, float b) =>
        IsIndefinite(a) ? b : IsIndefinite(b) ? a : Math.Max(a, b);

    /// <summary>Border + padding inline/block of a cell (separated model: cell
    /// own borders; collapsed model: zero borders, padding only).</summary>
    /// <summary>Border and padding a cell contributes to the grid. In the collapsing
    /// model a shared edge is split between its two neighbours, so only half of each
    /// collapsed border belongs to this cell (CSS 2.1 §17.6.2); callers that know the
    /// edge table pass it as 'collapsedBorders'. Without it the cell keeps no border
    /// width of its own, which is what made thick collapsed borders vanish from the
    /// table's used size.</summary>
    public static BoxStrut ComputeCellBorderPadding(Element cell, ConstraintSpace? space = null,
        BoxStrut? collapsedBorders = null)
    {
        var style = cell.ComputedStyle;
        if (style == null) return BoxStrut.Zero;
        float fontSize = style.FontSize;
        // Resolve rem/vw/vh against the real root-font/viewport when a
        // constraint space is supplied; without one keep the historical constants
        // so callers that cannot see a space behave exactly as before.
        float rootFont = space?.RootFontSize ?? ConstraintSpace.DefaultRootFontSize;
        float vw = space?.ViewportWidth ?? 1024f, vh = space?.ViewportHeight ?? 768f;
        var padding = new BoxStrut(
            style.PaddingTop.ToPixels(fontSize, rootFont, vw, vh),
            style.PaddingRight.ToPixels(fontSize, rootFont, vw, vh),
            style.PaddingBottom.ToPixels(fontSize, rootFont, vw, vh),
            style.PaddingLeft.ToPixels(fontSize, rootFont, vw, vh));
        var borders = style.BorderCollapse
            ? collapsedBorders ?? BoxStrut.Zero
            : LengthUtils.ComputeBorders(style);
        return new BoxStrut(borders.Top + padding.Top, borders.Right + padding.Right,
            borders.Bottom + padding.Bottom, borders.Left + padding.Left);
    }
}