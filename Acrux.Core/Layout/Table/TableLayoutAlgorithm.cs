using System;
using System.Collections.Generic;
using Acrux.Core.Dom;
using Acrux.Core.Layout.Geometry;

namespace Acrux.Core.Layout.Table;

/// <summary>
/// Main CSS table layout algorithm. Mirrors TableLayoutAlgorithm in
/// table_layout_algorithm.cc (column constraints, table inline size,
/// synchronization, row/block distribution, fragment generation).
/// </summary>
public class TableLayoutAlgorithm : LayoutAlgorithm
{
    private readonly BlockLayoutAlgorithm? _parent;

    private BoxStrut _borders;
    private BoxStrut _padding;
    private BoxStrut _borderPadding;
    private List<TableTypes.Column> _columnConstraints = new();
    private List<TableLayoutUtils.TableColumnLocation> _columnLocations = new();

    public TableLayoutAlgorithm(Element node, in ConstraintSpace space, BlockLayoutAlgorithm? parent = null)
        : base(node, space)
    {
        _parent = parent;
    }

    private static float K(float v) => TableTypes.IsIndefinite(v) ? 0 : v;
    private static float ClampNeg(float v) => Math.Max(0, v);

    public override LayoutResult Layout()
    {
        var style = Style;
        bool isFixedLayout = style.TableLayout == "fixed";
        bool hasCollapsedBorders = style.BorderCollapse;
        // border-spacing is ignored only by border-collapse: collapse; the
        // table-layout algorithm choice does not affect it (CSS 2.1 §17.5).
        float borderSpacing = hasCollapsedBorders ? 0 : Math.Max(0, style.BorderSpacing);
        // The second value of 'border-spacing' only applies to the row gaps.
        float borderRowSpacing = hasCollapsedBorders ? 0 : Math.Max(0, style.UsedBorderRowSpacing);

        var groupedChildren = new TableGroupedChildren(Node);
        var tableBorders = TableBorders.ComputeTableBorders(Node);

        var borders = LengthUtils.ComputeBorders(style);
        var padding = LengthUtils.ComputePadding(Space, style);
        if (hasCollapsedBorders)
        {
            borders = tableBorders.TableBorderValue;
            padding = BoxStrut.Zero;
        }
        _borders = borders;
        _padding = padding;
        _borderPadding = new BoxStrut(borders.Top + padding.Top, borders.Right + padding.Right,
            borders.Bottom + padding.Bottom, borders.Left + padding.Left);

        // Column constraints.
        _columnConstraints = TableLayoutUtils.ComputeColumnConstraints(Node, groupedChildren, tableBorders, _borderPadding);

        var captionConstraint = ComputeCaptionConstraint(groupedChildren);
        float undistributableSpace = TableLayoutUtils.ComputeUndistributableTableSpace(_columnConstraints, _borderPadding.HorizontalSum, borderSpacing);

        float assignableTableInlineSize = ComputeAssignableTableInlineSize(captionConstraint, undistributableSpace, isFixedLayout);

        // Distribute the assignable width, compute locations.
        var columnSizes = TableLayoutUtils.SynchronizeAssignableTableInlineSizeAndColumns(assignableTableInlineSize, isFixedLayout, _columnConstraints);

        bool hasSections = false;
        foreach (var section in groupedChildren.Begin().Sections())
        {
            hasSections = true;
            break;
        }

        bool hasCollapsedColumns;
        TableLayoutUtils.ComputeLocationsFromColumns(_columnConstraints, columnSizes, borderSpacing,
            /* shrinkCollapsed */ false, _columnLocations, out hasCollapsedColumns);
        bool isGridEmpty = _columnLocations.Count == 0;

        float tableInlineSizeBeforeCollapse;
        if (isGridEmpty)
        {
            tableInlineSizeBeforeCollapse = TableLayoutUtils.ComputeEmptyTableInlineSize(
                Space.IsFixedInlineSize, Space.InlineAutoBehavior == AutoBehavior.StretchImplicit, style,
                assignableTableInlineSize, undistributableSpace, captionConstraint, _borderPadding, hasCollapsedBorders);
        }
        else
        {
            tableInlineSizeBeforeCollapse = TableLayoutUtils.ComputeTableSizeFromColumns(_columnLocations, _borderPadding, borderSpacing);
        }

        // Captions (their block sizes contribute to the table's height). A caption
        // lives in the table's border box: it spans the full border-box inline size
        // and overlays the table's own border and padding.
        var captions = new List<BoxFragment>();
        LayoutCaptions(groupedChildren, ClampNeg(tableInlineSizeBeforeCollapse), captions);

        // Rows / sections.
        var data = new TableConstraintSpaceData
        {
            is_fixed_layout = isFixedLayout,
            is_table_block_size_specified = !(style.Height is AutoLength),
            has_collapsed_borders = hasCollapsedBorders,
            table_border_spacing = borderSpacing,
            table_border_row_spacing = borderRowSpacing,
            table_inline_size_before_collapse = tableInlineSizeBeforeCollapse,
            table_column_count = _columnLocations.Count,
            table_available_inline_size = assignableTableInlineSize,
        };

        float minimalTableGridBlockSize;
        ComputeRows(hasSections ? ClampNeg(tableInlineSizeBeforeCollapse - _borderPadding.HorizontalSum) : 0,
            groupedChildren, _columnLocations, tableBorders, data, out minimalTableGridBlockSize);

        if (hasCollapsedColumns)
        {
            TableLayoutUtils.ComputeLocationsFromColumns(_columnConstraints, columnSizes, borderSpacing,
                /* shrinkCollapsed */ true, _columnLocations, out hasCollapsedColumns);
        }

        float tableInlineSize;
        if (isGridEmpty)
        {
            tableInlineSize = tableInlineSizeBeforeCollapse;
        }
        else
        {
            tableInlineSize = Math.Max(TableLayoutUtils.ComputeTableSizeFromColumns(_columnLocations, _borderPadding, borderSpacing),
                captionConstraint.min_inline_size);
        }
        Builder.InlineSize = tableInlineSize;

        return GenerateFragment(tableInlineSize, minimalTableGridBlockSize, data, captions, tableBorders, isGridEmpty,
            borderSpacing, borderRowSpacing);
    }

    // ==========================================================================
    // Inline size.
    // ==========================================================================

    private TableTypes.Caption ComputeCaptionConstraint(TableGroupedChildren groupedChildren)
    {
        float min = 0, max = 0;
        float blockSize = 0;
        foreach (var caption in groupedChildren.Captions)
        {
            var capStyle = caption.ComputedStyle;
            if (capStyle == null || capStyle.Display == DisplayType.None)
                continue;
            var (cmin, cmax) = TableLayoutUtils.ComputeContentMinMax(caption);
            float font = capStyle.FontSize;
            float bpSum = capStyle.BorderLeftWidth + capStyle.BorderRightWidth;
            bpSum += capStyle.PaddingLeft.ToPixels(font, RootFontSize, ViewportWidth, ViewportHeight);
            bpSum += capStyle.PaddingRight.ToPixels(font, RootFontSize, ViewportWidth, ViewportHeight);
            bpSum += capStyle.MarginLeft.ToPixels(font, RootFontSize, ViewportWidth, ViewportHeight);
            bpSum += capStyle.MarginRight.ToPixels(font, RootFontSize, ViewportWidth, ViewportHeight);
            min = Math.Max(min, cmin + bpSum);
            max = Math.Max(max, cmax + bpSum);
        }
        return new TableTypes.Caption(new MinMaxSizes(min, max), blockSize);
    }

    private float ComputeAssignableTableInlineSize(TableTypes.Caption captionConstraint, float undistributableSpace, bool isFixedLayout)
    {
        float availableInline = Space.HasDefiniteInlineSize ? Space.AvailableInlineSize : ViewportWidth;

        var gridMinMax = TableLayoutUtils.ComputeGridInlineMinMax(isFixedLayout, _columnConstraints, undistributableSpace,
            /* allowColumnPercentages */ true);
        float used = ResolveUsedTableInlineSize(availableInline, gridMinMax);
        // A specified table width wins over the grid's min-content contribution
        // (CSS 2.1 §17.5.2.1): the content overflows instead of inflating the box.
        if (!HasSpecifiedInlineSize(availableInline))
            used = Math.Max(used, gridMinMax.MinSize);
        used = Math.Max(used, captionConstraint.min_inline_size);
        return ClampNeg(used - undistributableSpace);
    }

    private float ResolveUsedTableInlineSize(float availableInline, MinMaxSizes gridMinMax)
    {
        var style = Style;
        // With 'box-sizing: border-box' (the default for tables) the specified width
        // already includes the box's own border and padding.
        float borderPaddingInline = style.BoxSizing == BoxSizingType.BorderBox
            ? 0 : _borderPadding.HorizontalSum;
        float used;
        var width = style.Width;
        if (width.IsFixed())
        {
            used = width.FixedValue() + borderPaddingInline;
        }
        else if (width.IsPercent() && TableTypes.IsKnown(availableInline))
        {
            used = width.PercentValue() / 100f * availableInline + borderPaddingInline;
        }
        else
        {
            // auto: shrink-to-fit between content min and max, capped by the
            // available inline size. DCHECK in the .cc: used >= grid_min.
            used = Math.Min(Math.Max(availableInline, gridMinMax.MinSize), gridMinMax.MaxSize);
            used = Math.Clamp(used, gridMinMax.MinSize, gridMinMax.MaxSize);
        }
        if (!HasSpecifiedInlineSize(availableInline))
            used = Math.Clamp(used, gridMinMax.MinSize, Math.Max(gridMinMax.MinSize, gridMinMax.MaxSize));
        return used;
    }

    /// <summary>True when the table's own inline size comes from a specified
    /// width/height rather than from its content.</summary>
    private bool HasSpecifiedInlineSize(float availableInline)
    {
        var width = Style.Width;
        return width.IsFixed() || (width.IsPercent() && TableTypes.IsKnown(availableInline));
    }

    private void LayoutCaptions(TableGroupedChildren groupedChildren, float availableInline, List<BoxFragment> captions)
    {
        float avail = Math.Max(0, availableInline);
        foreach (var caption in groupedChildren.Captions)
        {
            var capStyle = caption.ComputedStyle;
            if (capStyle == null || capStyle.Display == DisplayType.None)
                continue;
            var builder = Space.InheritBuilder(avail, float.PositiveInfinity);
            builder.SetIsFixedInlineSize(true);
            builder.SetIsNewFormattingContext(true);
            var result = new BlockLayoutAlgorithm(caption, builder.ToConstraintSpace()).Layout();
            var fragment = result.Fragment;
            if (TableTypes.IsIndefinite(fragment.BlockSize))
                fragment.BlockSize = 0;
            captions.Add(fragment);
        }
    }

    // ==========================================================================
    // Rows.
    // ==========================================================================

    private void ComputeRows(float tableGridInlineSize, TableGroupedChildren groupedChildren,
        List<TableLayoutUtils.TableColumnLocation> columnLocations, TableBorders tableBorders,
        TableConstraintSpaceData data, out float minimalTableGridBlockSize)
    {
        bool isTableBlockSizeSpecified = data.is_table_block_size_specified;
        int sectionIndex = 0;
        foreach (var section in groupedChildren.Begin().Sections())
        {
            if (TableLayoutUtils.IsEmptyTableSection(section))
            {
                sectionIndex++;
                continue;
            }
            TableLayoutUtils.ComputeSectionMinimumRowBlockSizes(section, tableGridInlineSize, isTableBlockSizeSpecified,
                columnLocations, tableBorders, data.table_border_row_spacing, sectionIndex++, data);
        }

        float totalTableMinBlockSize = 0;
        foreach (var section in data.sections)
            totalTableMinBlockSize += section.block_size;

        float cssTableBlockSize = TableTypes.kIndefiniteSize;
        if (Space.HasDefiniteBlockSize)
        {
            cssTableBlockSize = LengthUtils.ComputeBlockSizeForFragment(Space, Style, _borderPadding,
                Style.MinHeight.IsSpecified() ? _borderPadding.VerticalSum : TableTypes.kIndefiniteSize, tableGridInlineSize);
        }
        else
        {
            cssTableBlockSize = LengthUtils.ComputeBlockSizeForFragment(Space, Style, _borderPadding,
                Style.MinHeight.IsSpecified() ? _borderPadding.VerticalSum : TableTypes.kIndefiniteSize, tableGridInlineSize);
        }

        if (TableTypes.IsKnown(cssTableBlockSize))
        {
            minimalTableGridBlockSize = cssTableBlockSize;
            float distributable = Math.Max(0, cssTableBlockSize - _borderPadding.VerticalSum);
            if (distributable > totalTableMinBlockSize)
                TableLayoutUtils.DistributeTableBlockSizeToSections(data.table_border_row_spacing, distributable, data.sections, data.rows);
        }
        else
        {
            minimalTableGridBlockSize = totalTableMinBlockSize;
        }

        // Collapsed rows get zero block-size and shrink the minimum table size.
        for (int i = 0; i < data.rows.Count; i++)
        {
            var row = data.rows[i];
            if (!row.is_collapsed)
                continue;
            if (minimalTableGridBlockSize != 0)
            {
                minimalTableGridBlockSize -= K(row.block_size);
                if (data.rows.Count > 1)
                    minimalTableGridBlockSize -= data.table_border_row_spacing;
            }
            row.block_size = 0;
        }
        if (minimalTableGridBlockSize < 0)
            minimalTableGridBlockSize = 0;
    }

    // ==========================================================================
    // Fragment generation.
    // ==========================================================================

    private LayoutResult GenerateFragment(float tableInlineSize, float minimalTableGridBlockSize,
        TableConstraintSpaceData data, List<BoxFragment> captions, TableBorders tableBorders, bool isGridEmpty,
        float borderSpacing, float borderRowSpacing)
    {
        Builder.BorderLeft = _borders.Left;
        Builder.BorderTop = _borders.Top;
        Builder.BorderRight = _borders.Right;
        Builder.BorderBottom = _borders.Bottom;
        Builder.PaddingLeft = _padding.Left;
        Builder.PaddingTop = _padding.Top;
        Builder.PaddingRight = _padding.Right;
        Builder.PaddingBottom = _padding.Bottom;
        Builder.Element = Node;
        Builder.InlineSize = tableInlineSize;

        float contentInline = Math.Max(0, tableInlineSize - _borderPadding.HorizontalSum);
        float gridInline = Math.Max(0, contentInline - borderSpacing * 2);

        float blockOffset = 0;
        // Child offsets are relative to the content box, so a caption that spans the
        // border box starts at its own negative border/padding inset.
        float captionInline = -_borderPadding.Left;
        float captionBlockOffset = -_borderPadding.Top;

        // Top captions.
        float topCaptionsEnd = 0;
        foreach (var cap in captions)
        {
            if (cap.Element?.ComputedStyle?.CaptionSide != "top")
                continue;
            cap.InlineOffset = captionInline;
            cap.BlockOffset = captionBlockOffset;
            Builder.Children.Add(cap);
            captionBlockOffset += cap.BlockSize;
            topCaptionsEnd = captionBlockOffset;
        }
        if (topCaptionsEnd > 0)
        {
            blockOffset = Math.Max(blockOffset, topCaptionsEnd + borderRowSpacing);
        }

        // Sections / rows / cells.
        if (!isGridEmpty)
        {
            // The outer border-spacing separates the first row from the table's top
            // edge just like two rows from each other (CSS 2.1 §17.5.2).
            if (topCaptionsEnd <= 0)
                blockOffset = borderRowSpacing;
            float sectionAvailableInlineSize = gridInline;
            foreach (var section in data.sections)
            {
                var sectionFrag = new BoxFragment
                {
                    Element = section.node,
                    InlineOffset = borderSpacing,
                    BlockOffset = blockOffset,
                    InlineSize = sectionAvailableInlineSize,
                    BlockSize = K(section.block_size),
                };

                float rowBlockOffset = 0;
                var tabulator = new TableBorders.ColspanCellTabulator();
                for (int r = 0; r < section.row_count; r++)
                {
                    int rowIndex = section.start_row_index + r;
                    var row = data.rows[rowIndex];
                    var rowFrag = new BoxFragment
                    {
                        Element = row.node,
                        InlineOffset = 0,
                        BlockOffset = rowBlockOffset,
                        InlineSize = sectionAvailableInlineSize,
                        BlockSize = K(row.block_size),
                    };
                    if (row.node != null)
                        PlaceCells(rowFrag, row.node, tableBorders, _columnLocations, data.rows,
                            section, rowIndex, tabulator, gridInline, borderRowSpacing, data.is_table_block_size_specified);
                    sectionFrag.Children.Add(rowFrag);
                    rowBlockOffset += K(row.block_size);
                    if (r != section.row_count - 1)
                        rowBlockOffset += borderRowSpacing;
                }
                sectionFrag.BlockSize = rowBlockOffset;
                Builder.Children.Add(sectionFrag);
                blockOffset += sectionFrag.BlockSize;
                blockOffset += borderRowSpacing;
            }
        }

        // The css table block size may force a minimum grid height.
        if (TableTypes.IsKnown(minimalTableGridBlockSize))
        {
            float minimalContent = ClampNeg(minimalTableGridBlockSize - _borderPadding.VerticalSum);
            blockOffset = Math.Max(blockOffset, minimalContent);
        }

        // Bottom captions. They extend the content box, and the stack is flush with
        // the table's bottom edge the same way a top caption starts at its top edge.
        float bottomCaptionsHeight = 0;
        float bottomCaptionOffset = blockOffset + _borderPadding.Bottom;
        foreach (var cap in captions)
        {
            if (cap.Element?.ComputedStyle?.CaptionSide != "bottom")
                continue;
            cap.InlineOffset = captionInline;
            cap.BlockOffset = bottomCaptionOffset;
            Builder.Children.Add(cap);
            bottomCaptionOffset += cap.BlockSize;
            bottomCaptionsHeight += cap.BlockSize;
        }
        blockOffset += bottomCaptionsHeight;

        // Everything above accumulates in content-box coordinates, so the table's own
        // borders and the outer border-spacing still have to be added - exactly like
        // the used inline size does.
        float tableBlockSize = blockOffset + _borderPadding.VerticalSum;
        Builder.BlockSize = tableBlockSize;
        Builder.IntrinsicBlockSize = tableBlockSize;
        Builder.HasSeenAllChildren = true;

        var frag = Builder.ToBoxFragment();
        frag.Children.AddRange(Builder.Children);

        var result = LayoutResult.FromFragment(frag);
        result.IntrinsicBlockSize = tableBlockSize;
        return result;
    }

    private void PlaceCells(BoxFragment rowFrag, Element row, TableBorders tableBorders,
        List<TableLayoutUtils.TableColumnLocation> columnLocations, List<TableTypes.Row> rows,
        TableTypes.Section section, int rowIndex, TableBorders.ColspanCellTabulator tabulator,
        float gridInlineSize, float borderRowSpacing, bool isTableBlockSizeSpecified)
    {
        if (columnLocations.Count == 0)
            return;
        bool hasCollapsedBorders = tableBorders.IsCollapsed;
        int sectionRowCount = Math.Max(1, section.row_count);

        // Each row restarts column assignment; without this the second row of a
        // section would continue counting columns from the first row's end.
        tabulator.StartRow();
        foreach (var child in row.Children)
        {
            if (child is not Element cell)
                continue;
            var cellStyle = cell.ComputedStyle;
            if (cellStyle == null || cellStyle.Display == DisplayType.None)
            {
                tabulator.ProcessCell(cell);
                continue;
            }

            tabulator.FindNextFreeColumn();
            int startColumn = tabulator.CurrentColumn;
            int colspan = TableLayoutUtils.CellColspan(cell);
            int rowspan = TableLayoutUtils.CellRowspan(cell);
            int effectiveRowspan = rowspan;
            if (effectiveRowspan > 1)
            {
                int maxRows = sectionRowCount - (rowIndex - section.start_row_index);
                if (maxRows < 1) maxRows = 1;
                effectiveRowspan = Math.Min(maxRows, effectiveRowspan);
            }

            int start = Math.Clamp(startColumn, 0, columnLocations.Count - 1);
            int endColumn = Math.Min(start + Math.Max(1, colspan) - 1, columnLocations.Count - 1);
            float cellInlineOffset = columnLocations[start].Offset - columnLocations[0].Offset;
            float cellInlineSize = columnLocations[endColumn].Offset + columnLocations[endColumn].Size - columnLocations[start].Offset;
            cellInlineSize = Math.Max(0, cellInlineSize);

            // Assigned block size: the sum of the spanned rows' block sizes,
            // plus border spacing between them.
            float cellBlockSize = 0;
            int rowSpanEnd = Math.Min(rowIndex + effectiveRowspan, rows.Count);
            for (int i = rowIndex; i < rowSpanEnd; i++)
            {
                cellBlockSize += K(rows[i].block_size);
                if (i != rowIndex)
                    cellBlockSize += borderRowSpacing;
            }

            // Collapsed edges are shared: the cell's border box owns half of each
            // (CSS 2.1 §17.6.2), which is what the table's used size is built from.
            var collapsedEdge = hasCollapsedBorders
                ? tableBorders.CellBorder(rowIndex, startColumn, effectiveRowspan, colspan,
                    tableBorders.SectionIndexOf(rowIndex))
                : default(BoxStrut?);
            var cellBorderPadding = TableTypes.ComputeCellBorderPadding(cell, Space, collapsedEdge);
            var cellSpace = TableLayoutUtils.SetupTableCellConstraintSpaceBuilder(cell, cellBorderPadding, columnLocations,
                cellBlockSize, gridInlineSize, startColumn,
                /* isInitialBlockSizeIndefinite */ !TableTypes.IsKnown(cellBlockSize),
                isTableBlockSizeSpecified, hasCollapsedBorders, Space, collapsedEdge);

            var layoutResult = new BlockLayoutAlgorithm(cell, cellSpace).Layout();
            var cellFragment = layoutResult.Fragment;
            cellFragment.InlineOffset = cellInlineOffset;
            cellFragment.BlockOffset = 0;
            cellFragment.InlineSize = cellInlineSize;
            // The content keeps its own height; the cell grows to the rows it spans.
            float contentBlockSize = ContentExtentOf(cellFragment);
            float freeBlockSize = Math.Max(0, cellBlockSize - contentBlockSize);
            if (TableTypes.IsKnown(cellFragment.BlockSize))
                cellFragment.BlockSize = Math.Max(cellFragment.BlockSize, cellBlockSize);
            else
                cellFragment.BlockSize = cellBlockSize;

            // 'vertical-align' on a cell moves its content inside the cell box
            // (CSS 2.1 §17.5.2.6). 'baseline' takes the row's baseline, which for the
            // first row of a section is the top edge.
            float contentShift = cellStyle.VerticalAlign switch
            {
                VerticalAlignType.Middle => freeBlockSize / 2f,
                VerticalAlignType.Bottom => freeBlockSize,
                _ => 0f,
            };
            if (contentShift > 0)
                ShiftFragmentContent(cellFragment, contentShift);

            rowFrag.Children.Add(cellFragment);

            tabulator.ProcessCell(cell);
        }
        tabulator.EndRow();
    }

    /// <summary>How far a cell's content actually reaches below the top of its
    /// content box. The cell's own specified height is not content, so it must not
    /// take part in the free space that 'vertical-align' distributes.</summary>
    private static float ContentExtentOf(BoxFragment fragment)
    {
        float extent = 0;
        foreach (var line in fragment.Lines)
            extent = Math.Max(extent, line.BlockEnd);
        foreach (var child in fragment.Children)
        {
            if (TableTypes.IsKnown(child.BlockSize))
                extent = Math.Max(extent, child.BlockOffset + child.BlockSize + child.MarginBottom);
        }
        return Math.Max(0, extent);
    }

    /// <summary>Move everything a fragment contains down by 'shift' — its line boxes
    /// and its child boxes — without touching the fragment's own box, so the
    /// background and border stay put while the content slides.</summary>
    private static void ShiftFragmentContent(BoxFragment fragment, float shift)
    {
        foreach (var line in fragment.Lines)
        {
            line.BlockOffset += shift;
            line.BaselineOffset += shift;
        }
        foreach (var child in fragment.Children)
        {
            child.BlockOffset += shift;
            child.MarginTop += shift;
        }
        if (fragment.FragmentItems == null) return;
        foreach (var item in fragment.FragmentItems.Items)
            item.Offset = new PhysicalOffset(item.Offset.Left, item.Offset.Top + shift);
    }
}