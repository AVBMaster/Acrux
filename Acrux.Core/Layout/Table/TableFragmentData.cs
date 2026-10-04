using System.Collections.Generic;

namespace Acrux.Core.Layout.Table;

/// <summary>
/// Per-fragment data for a table fragment. Mirrors TableFragmentData in
/// table_fragment_data.h.
/// </summary>
public class TableFragmentData
{
    public struct ColumnGeometry
    {
        public int start_column;
        public int span;
        public float inline_offset;
        public float inline_size;
        public object? node;
        public TableTypes.CellInlineConstraint constraint;
        public bool is_collapsed;
        public bool is_table_fixed;
    }

    public struct CollapsedBordersGeometry
    {
        public List<float> columns;
        public int table_column_count;
    }

    public bool has_collapsed_borders;
    public bool has_collapsed_border_geometry;
    public float table_grid_rect_inline_offset;   // x
    public float table_grid_rect_block_offset;    // y
    public float table_grid_rect_inline_size;     // width
    public float table_grid_rect_block_size;      // height
    public int table_column_count;
    public List<ColumnGeometry> table_column_geometries = new();
    public List<int> table_cell_column_indexes = new();
    public CollapsedBordersGeometry? collapsed_borders_geometry;

    public void SetTableGridRect(float inlineOffset, float blockOffset, float inlineSize, float blockSize)
    {
        table_grid_rect_inline_offset = inlineOffset;
        table_grid_rect_block_offset = blockOffset;
        table_grid_rect_inline_size = inlineSize;
        table_grid_rect_block_size = blockSize;
    }

    public void SetTableColumnCount(int tableColumnCount) => table_column_count = tableColumnCount;

    public void SetTableColumnGeometries(List<ColumnGeometry> tableColumnGeometries)
    {
        table_column_geometries = tableColumnGeometries;
        table_column_count = tableColumnGeometries.Count;
    }

    public void SetTableCollapsedBordersGeometry(CollapsedBordersGeometry tableCollapsedBordersGeometry)
    {
        collapsed_borders_geometry = tableCollapsedBordersGeometry;
        has_collapsed_border_geometry = true;
    }

    public void SetTableCellColumnIndex(BoxFragment cell, int columnIndex)
    {
        table_cell_column_indexes.Add(columnIndex);
    }
}