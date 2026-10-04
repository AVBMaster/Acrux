using SkiaSharp;
using Acrux.Core.Dom;
using Acrux.Core.Layout;

namespace Acrux.Rendering;

/// <summary>
/// Paints table-specific decorations. Mirrors table_painters.cc.
/// Table backgrounds paint on the grid rect (union of all cells), and
/// table part backgrounds (table, section, row, column) are propagated
/// into cells so they show through cell gaps.
/// </summary>
internal static class TablePainter
{
    public static bool IsTable(Element element) =>
        element.TagName.Equals("TABLE", StringComparison.OrdinalIgnoreCase);

    public static bool IsTableSection(Element element) =>
        element.TagName is "TBODY" or "THEAD" or "TFOOT";

    public static bool IsTableRow(Element element) =>
        element.TagName.Equals("TR", StringComparison.OrdinalIgnoreCase);

    public static bool IsTableCell(Element element) =>
        element.TagName is "TD" or "TH";

    /// <summary>
    /// empty-cells: hide — an empty table cell paints no borders or background
    /// (CSS 2.1 §17.6.3); its box still occupies space.
    /// </summary>
    public static bool ShouldHideEmptyCell(Element element, ComputedStyle style)
    {
        if (!IsTableCell(element) || style.EmptyCells != "hide")
            return false;
        foreach (var child in element.Children)
        {
            if (child is Acrux.Core.Dom.TextNode text)
            {
                if (!string.IsNullOrWhiteSpace(text.Data))
                    return false;
            }
            else if (child is Element el)
            {
                if (el.ComputedStyle == null || el.ComputedStyle.Display != Acrux.Core.Dom.DisplayType.None)
                    return false;
            }
        }
        return true;
    }

    /// <summary>True when the element is a cell of a border-collapse: collapse table.</summary>
    public static bool InCollapsedTable(Element element) => OwningCollapsedTable(element) != null;

    /// <summary>The collapse table a cell belongs to, or null. The table element's own
    /// border takes part in resolving the outer grid lines (CSS 2.1 §17.6.2.1), so the
    /// painter has to be able to reach it from a cell.</summary>
    public static Element? OwningCollapsedTable(Element element)
    {
        var row = element.ParentElement;
        var table = row == null ? null : (row.ParentElement?.TagName is "TBODY" or "THEAD" or "TFOOT"
            ? row.ParentElement?.ParentElement : row);
        return table != null && IsTable(table) && table.ComputedStyle?.BorderCollapse == true ? table : null;
    }

    /// <summary>
    /// Paints the collapsed borders of one table cell (CSS 2.1 §17.6.2.1). Each
    /// shared grid line gets a single border: the wider of the two facing borders
    /// wins, ties go to the earlier element in document order (the cell above /
    /// to the left). The winning border is drawn centered on the grid line, so
    /// both neighbours resolve the same strip and painting it twice is idempotent.
    /// </summary>
    public static void PaintCollapsedCellBorders(DisplayList displayList, Element cell, ComputedStyle style,
        SKRect rect, float contentOffsetY)
    {
        var above = FacingCell(cell, above: true);
        var below = FacingCell(cell, above: false);
        var (prev, next) = HorizontalNeighbors(cell);
        // With no cell facing an edge, the table element's own border is the other
        // candidate for that grid line (CSS 2.1 §17.6.2.1).
        var tableStyle = OwningCollapsedTable(cell)?.ComputedStyle;

        PaintCollapsedSide(displayList, rect, Side.Top,
            (style.BorderTopWidth, style.BorderTopColor),
            FacingEdge(above, Side.Top, contentOffsetY), neighborWinsTies: true,
            outer: above == null ? OuterEdge(tableStyle, Side.Top) : null);
        PaintCollapsedSide(displayList, rect, Side.Bottom,
            (style.BorderBottomWidth, style.BorderBottomColor),
            FacingEdge(below, Side.Bottom, contentOffsetY), neighborWinsTies: false,
            outer: below == null ? OuterEdge(tableStyle, Side.Bottom) : null);
        PaintCollapsedSide(displayList, rect, Side.Left,
            (style.BorderLeftWidth, style.BorderLeftColor),
            FacingEdge(prev, Side.Left, contentOffsetY), neighborWinsTies: true,
            outer: prev == null ? OuterEdge(tableStyle, Side.Left) : null);
        PaintCollapsedSide(displayList, rect, Side.Right,
            (style.BorderRightWidth, style.BorderRightColor),
            FacingEdge(next, Side.Right, contentOffsetY), neighborWinsTies: false,
            outer: next == null ? OuterEdge(tableStyle, Side.Right) : null);
    }

    /// <summary>The table's own border for an outer grid line. It only wins when it is
    /// wider than the cell's: the table is the last candidate in the border-priority
    /// order, so ties go to the cell.</summary>
    private static (float width, SKColor color, float line)? OuterEdge(ComputedStyle? tableStyle, Side side)
    {
        if (tableStyle == null) return null;
        float width = side switch
        {
            Side.Top => tableStyle.BorderTopWidth,
            Side.Bottom => tableStyle.BorderBottomWidth,
            Side.Left => tableStyle.BorderLeftWidth,
            _ => tableStyle.BorderRightWidth,
        };
        var color = side switch
        {
            Side.Top => tableStyle.BorderTopColor,
            Side.Bottom => tableStyle.BorderBottomColor,
            Side.Left => tableStyle.BorderLeftColor,
            _ => tableStyle.BorderRightColor,
        };
        return width > 0 ? (width, color, float.NaN) : null;
    }

    private enum Side { Top, Bottom, Left, Right }

    /// <summary>The facing border of a neighbour plus the grid line it shares with
    /// the current cell (the midpoint of the two cells' edges, so the resolved
    /// strip lands at the same place no matter which side computes it).</summary>
    private static (float width, SKColor color, float line)? FacingEdge(Element? neighbor, Side side, float contentOffsetY)
    {
        if (neighbor?.LayoutBox == null || neighbor.ComputedStyle == null)
            return null;
        var n = neighbor.LayoutBox.BorderBox;
        float line = side switch
        {
            Side.Top => n.Bottom + contentOffsetY,
            Side.Bottom => n.Top + contentOffsetY,
            Side.Left => n.Right,
            _ => n.Left,
        };
        var e = Edge(neighbor, side);
        return (e.width, e.color, line);
    }

    private static (float width, SKColor color) Edge(Element cell, Side side)
    {
        var s = cell.ComputedStyle!;
        return side switch
        {
            Side.Top => (s.BorderTopWidth, s.BorderTopColor),
            Side.Bottom => (s.BorderBottomWidth, s.BorderBottomColor),
            Side.Left => (s.BorderLeftWidth, s.BorderLeftColor),
            _ => (s.BorderRightWidth, s.BorderRightColor),
        };
    }

    private static void PaintCollapsedSide(DisplayList displayList, SKRect rect, Side side,
        (float width, SKColor color) own, (float width, SKColor color, float line)? neighbor, bool neighborWinsTies,
        (float width, SKColor color, float line)? outer = null)
    {
        // A facing cell shares the grid line; with none, the table's own border is the
        // other candidate for that outer line and ties go to the cell (§17.6.2.1).
        var candidate = neighbor ?? outer;
        bool candidateWinsTies = neighbor != null ? neighborWinsTies : false;
        var winner = own;
        if (candidate != null &&
            (candidate.Value.width > own.width ||
             (candidate.Value.width == own.width && candidateWinsTies)))
            winner = (candidate.Value.width, candidate.Value.color);
        if (winner.width <= 0 || winner.color.Alpha == 0)
            return;

        // The collapsed border is centered on the shared grid line; both facing
        // cells resolve the same winner and line, so the strip is painted twice
        // identically.
        float half = winner.width / 2f;
        float ownEdge = side switch
        {
            Side.Top => rect.Top,
            Side.Bottom => rect.Bottom,
            Side.Left => rect.Left,
            _ => rect.Right,
        };
        // Grid line = midpoint between this cell's edge and the neighbour's facing
        // edge (they coincide when there is no slack between the cells).
        float line = neighbor == null ? ownEdge : (ownEdge + neighbor.Value.line) / 2f;
        var strip = side switch
        {
            Side.Top => new SKRect(rect.Left - half, line - half, rect.Right + half, line + half),
            Side.Bottom => new SKRect(rect.Left - half, line - half, rect.Right + half, line + half),
            Side.Left => new SKRect(line - half, rect.Top - half, line + half, rect.Bottom + half),
            _ => new SKRect(line - half, rect.Top - half, line + half, rect.Bottom + half),
        };
        var op = PaintOpPool.GetDrawRectOp();
        op.Rect = strip;
        op.FillColor = winner.color;
        op.Bounds = strip;
        displayList.Add(op);
    }

    private static Element? FacingCell(Element cell, bool above)
    {
        var row = cell.ParentElement;
        var rowParent = row?.ParentElement;
        if (row == null || rowParent == null) return null;
        var rows = DirectRows(rowParent);
        int rowIndex = rows.IndexOf(row);
        int colIndex = DirectCells(row).IndexOf(cell);
        if (rowIndex < 0 || colIndex < 0) return null;
        for (int i = rowIndex + (above ? -1 : 1); i >= 0 && i < rows.Count; i += above ? -1 : 1)
        {
            var cells = DirectCells(rows[i]);
            if (colIndex < cells.Count)
                return cells[colIndex];
        }
        return null;
    }

    private static (Element? prev, Element? next) HorizontalNeighbors(Element cell)
    {
        var row = cell.ParentElement;
        if (row == null) return (null, null);
        var cells = DirectCells(row);
        int idx = cells.IndexOf(cell);
        return (idx > 0 ? cells[idx - 1] : null,
                idx >= 0 && idx + 1 < cells.Count ? cells[idx + 1] : null);
    }

    private static List<Element> DirectRows(Element rowParent)
    {
        var rows = new List<Element>();
        foreach (var child in rowParent.Children)
        {
            if (child is not Element el) continue;
            if (IsTableRow(el)) rows.Add(el);
            else if (IsTableSection(el))
                foreach (var r in el.Children)
                    if (r is Element re && IsTableRow(re)) rows.Add(re);
        }
        return rows;
    }

    private static List<Element> DirectCells(Element row)
    {
        var cells = new List<Element>();
        foreach (var child in row.Children)
            if (child is Element el && IsTableCell(el))
                cells.Add(el);
        return cells;
    }

    public static void PaintTableBackground(DisplayList displayList, Element element, ComputedStyle style, LayoutBox box, float contentOffsetY)
    {
        if (!IsTable(element)) return;

        bool hasBgColor = style.BackgroundColor.HasValue && style.BackgroundColor.Value.Alpha > 0;
        bool hasBgImage = style.BackgroundImage is { Count: > 0 } && style.BackgroundImage!.Any(s => s != "none");
        if (!hasBgColor && !hasBgImage) return;

        // Paint the background on the grid rect (union of all cells), not the
        // border rect, so the table background is visible through cell gaps.
        var gridRect = ComputeGridRect(element, box, contentOffsetY);
        if (gridRect.Width <= 0 || gridRect.Height <= 0) return;

        if (hasBgColor)
        {
            var op = PaintOpPool.GetDrawRectOp();
            op.Rect = gridRect;
            op.FillColor = style.BackgroundColor.Value;
            op.Bounds = gridRect;
            displayList.Add(op);
        }

        if (hasBgImage && style.BackgroundImage!.Any(s => s.Contains("gradient", StringComparison.OrdinalIgnoreCase)))
        {
            var shader = GradientRenderer.CreateGradient(style.BackgroundImage!.FirstOrDefault() ?? "", gridRect);
            if (shader != null)
            {
                var path = new SKPath();
                path.AddRect(gridRect);
                var op = PaintOpPool.GetDrawPathOp();
                op.Path = path;
                op.FillPaint = new SKPaint { Style = SKPaintStyle.Fill, Shader = shader, IsAntialias = true };
                op.Bounds = gridRect;
                displayList.Add(op);
            }
        }
    }

    /// <summary>
    /// Paints table part backgrounds (table, section, row) into cells.
    /// The table part's background is painted behind each cell so it shows
    /// through cell padding gaps.
    /// </summary>
    public static void PaintTablePartBackgroundIntoCell(DisplayList displayList, Element tablePart, ComputedStyle partStyle, SKRect partRect, Element cell, SKRect cellRect)
    {
        if (partStyle.Visibility != VisibilityType.Visible) return;

        bool hasBgColor = partStyle.BackgroundColor.HasValue && partStyle.BackgroundColor.Value.Alpha > 0;
        bool hasBgImage = partStyle.BackgroundImage is { Count: > 0 } && partStyle.BackgroundImage!.Any(s => s != "none");
        if (!hasBgColor && !hasBgImage) return;

        // Clip the part background to the cell rect so it only shows through
        // this cell.
        if (hasBgColor)
        {
            var op = PaintOpPool.GetDrawRectOp();
            op.Rect = cellRect;
            op.FillColor = partStyle.BackgroundColor.Value;
            op.Bounds = cellRect;
            displayList.Add(op);
        }
    }

    private static SKRect ComputeGridRect(Element table, LayoutBox box, float contentOffsetY)
    {
        float left = float.MaxValue, top = float.MaxValue;
        float right = float.MinValue, bottom = float.MinValue;
        bool foundCell = false;

        foreach (var section in table.Children)
        {
            if (section is not Element sectionEl || !IsTableSection(sectionEl)) continue;
            foreach (var row in sectionEl.Children)
            {
                if (row is not Element rowEl || !IsTableRow(rowEl)) continue;
                foreach (var cell in rowEl.Children)
                {
                    if (cell is not Element cellEl || !IsTableCell(cellEl)) continue;
                    var cellBox = cellEl.LayoutBox;
                    if (cellBox == null) continue;

                    left = Math.Min(left, cellBox.BorderBox.Left);
                    top = Math.Min(top, cellBox.BorderBox.Top);
                    right = Math.Max(right, cellBox.BorderBox.Right);
                    bottom = Math.Max(bottom, cellBox.BorderBox.Bottom);
                    foundCell = true;
                }
            }
        }

        if (!foundCell)
            return new SKRect(box.BorderBox.Left, box.BorderBox.Top + contentOffsetY,
                box.BorderBox.Right, box.BorderBox.Bottom + contentOffsetY);

        return new SKRect(left, top + contentOffsetY, right, bottom + contentOffsetY);
    }
}