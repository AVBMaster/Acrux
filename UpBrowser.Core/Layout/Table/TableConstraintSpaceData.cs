using System.Collections.Generic;
using UpBrowser.Core.Layout.Geometry;

namespace UpBrowser.Core.Layout.Table;

/// <summary>
/// Data shared between a table and its section/row fragments during layout.
/// Mirrors TableConstraintSpaceData in table_constraint_space_data.h.
/// </summary>
public class TableConstraintSpaceData
{
    public int table_column_count;
    public List<float> column_sizes = new();        // table_column_count
    public List<float> column_min_sizes = new();    // table_column_count
    public List<float> column_basins = new();       // 0 or table_column_count (colspan cells)
    public List<float> column_borders = new();      // 0 or table_column_count (collapsed borders)
    public bool is_fixed_layout;
    public bool is_table_block_size_specified;
    public bool is_table_block_size_containing_block_specified;
    public bool has_collapsed_borders;
    public bool has_collapsed_border_geometry;
    public float table_available_inline_size;
    public float table_inline_size_before_collapse;
    public float table_block_size = TableTypes.kIndefiniteSize;
    public float table_border_spacing;
    /// <summary>Row (block-axis) border-spacing; differs from the column spacing only
    /// for the two-value 'border-spacing' syntax.</summary>
    public float table_border_row_spacing;
    public List<TableTypes.RowspanCell> rowspan_cells = new();
    public List<TableTypes.Section> sections = new();
    public List<TableTypes.Row> rows = new();
    public List<TableTypes.ColspanCell> colspan_cells = new();
    public List<TableTypes.CellBlockConstraint> cell_block_constraints = new();

    public TableConstraintSpaceData()
    {
    }

    public TableTypes.Section Section(int startRowIndex, int rowCount)
    {
        for (int i = 0; i < sections.Count; i++)
        {
            var section = sections[i];
            if (section.start_row_index == startRowIndex && section.row_count == rowCount)
                return section;
        }
        // The section is shared between all fragments that span it; if we don't
        // already have one, create it (mirrors the .cc, which looks it up).
        return new TableTypes.Section(null, startRowIndex, rowCount, TableTypes.kIndefiniteSize);
    }

    public float TableAvailableBlockSize()
    {
        if (!is_table_block_size_specified)
            return TableTypes.kIndefiniteSize;
        float table_available_block_size = table_block_size;
        for (int i = 0; i < sections.Count; i++)
        {
            float section_block_size = sections[i].block_size;
            if (section_block_size != TableTypes.kIndefiniteSize)
                table_available_block_size -= section_block_size;
        }
        return table_available_block_size;
    }

    public float TableMinBlockSize()
    {
        float table_min_block_size = 0;
        for (int i = 0; i < sections.Count; i++)
        {
            float section_min_block_size = 0;
            var section = sections[i];
            for (int j = 0; j < section.row_count; j++)
            {
                var row = rows[section.start_row_index + j];
                if (!TableTypes.IsIndefinite(row.block_size))
                    section_min_block_size += row.block_size;
                else if (!TableTypes.IsIndefinite(row.percent_block_size))
                    section_min_block_size += row.percent_block_size;
            }
            if (!TableTypes.IsIndefinite(section.block_size))
                section_min_block_size = Math.Max(section_min_block_size, section.block_size);
            table_min_block_size += section_min_block_size;
        }
        if (!TableTypes.IsIndefinite(table_block_size))
            table_min_block_size = Math.Max(table_min_block_size, table_block_size);
        return table_min_block_size;
    }
}