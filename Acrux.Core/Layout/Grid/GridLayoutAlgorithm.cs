using SkiaSharp;
using Acrux.Core.Dom;
using Acrux.Core.Layout;

namespace Acrux.Core.Layout.Grid;

/// <summary>
/// CSS Grid Layout Algorithm, mirroring the engine's grid layout algorithm.
/// Implements the full grid track sizing algorithm per CSS Grid spec:
/// 1. Init track sizes
/// 2. Resolve intrinsic track sizes (min-content, max-content, auto)
/// 3. Maximize tracks (distribute positive free space)
/// 4. Stretch auto tracks
/// 5. Expand flexible tracks (fr units)
/// Also handles item placement, alignment, and auto-placement with dense packing.
/// </summary>
public class GridLayoutAlgorithm
{
    private readonly ITextMeasurer? _textMeasurer;
    private readonly ConstraintSpace _space;
    private readonly float _rootFontSize;
    private readonly float _viewportWidth;
    private readonly float _viewportHeight;
    private ComputedStyle? _containerStyle;
    private float _containerWidth;
    private float _containerHeight;
    private bool _containerHeightAuto;

    /// <summary>Sum of resolved column base sizes + inter-column gaps, set by
    /// <see cref="Layout"/>. Used by the adapter to size a 'width: max-content'
    /// grid container to its tracks.</summary>
    public float ResolvedContentInlineSize { get; private set; }

    public GridLayoutAlgorithm(
        ITextMeasurer? textMeasurer,
        in ConstraintSpace space)
    {
        _textMeasurer = textMeasurer;
        _space = space;
        _rootFontSize = space.RootFontSize;
        _viewportWidth = space.ViewportWidth;
        _viewportHeight = space.ViewportHeight;
    }

    private float ResolveGap(Length gap, float reference)
    {
        if (gap is AutoLength) return 0;
        // A percentage gap resolves against the container's content box on that
        // axis; an indefinite (auto) axis makes it 0.
        if (gap is PercentLength && !float.IsFinite(reference))
            return 0;
        return gap.ToPixels(reference, _rootFontSize, _viewportWidth, _viewportHeight);
    }

    public void Layout(Element gridContainer, LayoutBox containerBox, float availableWidth)
    {
        _containerStyle = gridContainer.ComputedStyle;
        if (_containerStyle == null) return;

        _containerWidth = containerBox.ContentBox.Width;
        _containerHeight = containerBox.ContentBox.Height;
        _containerHeightAuto = _containerStyle.Height is not PixelLength;


        // Gap percentages resolve against the container's own content box (CSS Box
        // Alignment): column-gap against the inline size, row-gap against the block
        // size. An indefinite axis (NaN) makes the percentage 0.
        float rowGap = ResolveGap(_containerStyle.RowGap, _containerHeight);
        float columnGap = ResolveGap(_containerStyle.ColumnGap, _containerWidth);

        var explicitColumns = ParseTrackList("grid-template-columns", _containerWidth);
        var explicitRows = ParseTrackList("grid-template-rows", _containerHeight);
        var areas = ParseTemplateAreas(_containerStyle.GridTemplateAreas);

        // 'grid-template-areas' declares explicit tracks of its own (CSS Grid §7.3): one row per
        // string, and as many columns as the widest string has names. They are auto-sized, like a
        // row the author wrote as 'auto', and they exist even when no item is placed in them.
        if (areas.Count > 0)
        {
            while (explicitRows.Count < areas.Count)
                explicitRows.Add(new GridTrack { SizeType = TrackSizeType.Auto });
            int areaColumns = areas.Max(row => row.Length);
            while (explicitColumns.Count < areaColumns)
                explicitColumns.Add(new GridTrack { SizeType = TrackSizeType.Auto });
        }

        var items = CollectAndPlaceItems(gridContainer, explicitColumns.Count, explicitRows.Count, areas, columnGap, rowGap);

        ExpandImplicitTracks(items, ref explicitColumns, ref explicitRows);

        IntrinsicSizeKind? inlineKind = _containerStyle.Width is IntrinsicLength il
            ? (il.Kind == IntrinsicSizeKind.FitContent ? IntrinsicSizeKind.MaxContent : il.Kind)
            : null;

        // Track sizing algorithm. For an intrinsic inline container
        // (`width: min-content|max-content`) resolve the column tracks from the
        // items' contributions directly, without the free-space distribution the
        // definite-container path does — an indefinite container has no free space
        // to stretch auto tracks into (that stretch made them +∞ → NaN).
        if (inlineKind is { } kk)
            SolveIntrinsicTracks(explicitColumns, items, columnGap, isColumn: true, kk);
        else
            ResolveTracks(explicitColumns, items, _containerWidth, columnGap, isColumn: true);

        // The grid's content inline size (non-collapsed column bases + gaps). The
        // adapter reads this back to shrink a 'width: min/max-content' container.
        ResolvedContentInlineSize = TrackGroupSize(explicitColumns, columnGap);

        // Lay every item out once at its resolved column width BEFORE row track
        // sizing: an auto row must be the max content height of its items, and
        // PositionItems reuses these boxes instead of laying out a second time.
        MeasureItems(items, explicitColumns, columnGap, containerBox);

        ResolveTracks(explicitRows, items, _containerHeight, rowGap, isColumn: false);

        PositionItems(items, explicitColumns, explicitRows, containerBox, columnGap, rowGap);

        RecordUsedTrackSizes(gridContainer, explicitColumns, explicitRows, columnGap, rowGap);
    }

    /// <summary>Publish the resolved track list for the CSSOM. A reference engine answers
    /// 'grid-template-columns' with the sizes layout gave the tracks rather than the list the
    /// author wrote (measured: 'grid-template-columns: 1fr 2fr' in a 100px grid reads back
    /// '33.3281px 66.6719px'), so the numbers have to survive the algorithm.</summary>
    private void RecordUsedTrackSizes(Element container, List<GridTrack> columns, List<GridTrack> rows,
        float columnGap, float rowGap)
    {
        if (_containerStyle == null) return;
        var usedColumns = UsedTrackSizes(columns, columnGap);
        var usedRows = UsedTrackSizes(rows, rowGap);
        // Loose text is an anonymous grid item, so a grid that holds only text still has one
        // track on each axis even though no element was placed. Its size is the track the
        // 'grid-auto-*' keyword named, or the axis the container itself resolved when that is
        // 'auto' — the single anonymous cell fills it.
        if ((usedColumns == null || usedColumns.Length == 0) && HasAnonymousTextItem(container))
            usedColumns = new[] { ImplicitTrackSize(_containerStyle.GridAutoColumns, _containerWidth, isColumn: true) };
        if ((usedRows == null || usedRows.Length == 0) && HasAnonymousTextItem(container))
            usedRows = new[] { ImplicitTrackSize(_containerStyle.GridAutoRows, _containerHeight, isColumn: false) };
        _containerStyle.GridUsedColumnSizes = usedColumns;
        _containerStyle.GridUsedRowSizes = usedRows;
    }

    /// <summary>The size of the single track a text-only grid container gets: what
    /// 'grid-auto-columns'/'grid-auto-rows' names when the author named it, and otherwise the
    /// axis the container resolved — an 'auto' track is the one that stretches to fill.</summary>
    private float ImplicitTrackSize(string? autoValue, float containerSize, bool isColumn)
    {
        if (!string.IsNullOrWhiteSpace(autoValue) && !autoValue.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            var tracks = new List<GridTrack>();
            ParseTrackListValue(autoValue.Replace(',', ' '), containerSize, tracks);
            if (tracks.Count > 0)
            {
                tracks[0].Initialize(containerSize, _containerStyle?.FontSize ?? 16, _viewportWidth, _viewportHeight);
                return Math.Max(0, tracks[0].BaseSize);
            }
        }
        if (isColumn) return containerSize;
        float lineHeight = _containerStyle!.LineHeightPx ?? _containerStyle.LineHeight * _containerStyle.FontSize;
        return Math.Max(lineHeight, containerSize);
    }

    /// <summary>True when the container's own children include text that is not just the
    /// whitespace between element items.</summary>
    private static bool HasAnonymousTextItem(Element container)
    {
        foreach (var child in container.ChildNodes)
        {
            if (child is TextNode && !string.IsNullOrWhiteSpace(child.TextContent)) return true;
        }
        return false;
    }

    /// <summary>Track sizes as the CSSOM reports them: every track EDGE is snapped to a layout
    /// unit (1/64 px, truncated) and the size is the difference between the track's own two
    /// edges. Snapping the sizes themselves would give a different answer — three 1fr tracks in a
    /// 101px box read back '33.6562px 33.6719px 33.6719px' (measured), and the last two are not
    /// what rounding each size on its own produces. The gap sits between the edges of the group,
    /// never inside one, which is why it is added after the second edge is taken.</summary>
    private static float[]? UsedTrackSizes(List<GridTrack> tracks, float gap)
    {
        if (tracks.Count == 0) return null;
        var sizes = new float[tracks.Count];
        double position = 0;
        for (int i = 0; i < tracks.Count; i++)
        {
            float size = tracks[i].BaseSize;
            if (!float.IsFinite(size) || size < 0) size = 0;
            double start = Math.Floor(position * 64) / 64;
            position += size;
            double end = Math.Floor(position * 64) / 64;
            sizes[i] = (float)(end - start);
            position += gap;
        }
        return sizes;
    }

    private List<string[]> ParseTemplateAreas(string? areasStr)
    {
        var areas = new List<string[]>();
        if (string.IsNullOrEmpty(areasStr)) return areas;

        // Extract quoted strings: each "..." is one row.
        int pos = 0;
        while (pos < areasStr.Length)
        {
            int qStart = areasStr.IndexOf('"', pos);
            if (qStart < 0) break;
            int qEnd = areasStr.IndexOf('"', qStart + 1);
            if (qEnd < 0) break;
            var row = areasStr[(qStart + 1)..qEnd].Trim();
            if (!string.IsNullOrEmpty(row))
                areas.Add(row.Split(' ', StringSplitOptions.RemoveEmptyEntries));
            pos = qEnd + 1;
        }

        // Fallback: if no quoted strings found, try comma-separated format.
        if (areas.Count == 0)
        {
            var rows = areasStr.Split(',', StringSplitOptions.RemoveEmptyEntries);
            foreach (var row in rows)
            {
                var trimmed = row.Trim().Trim('"', '\'');
                if (string.IsNullOrEmpty(trimmed)) continue;
                areas.Add(trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries));
            }
        }

        return areas;
    }

    private List<GridTrack> ParseTrackList(string propertyName, float containerSize)
    {
        var tracks = new List<GridTrack>();
        var value = propertyName == "grid-template-columns"
            ? _containerStyle?.GridTemplateColumns
            : _containerStyle?.GridTemplateRows;
        if (string.IsNullOrEmpty(value) || value == "none") return tracks;
        ParseTrackListValue(value, containerSize, tracks);
        return tracks;
    }

    private void ParseTrackListValue(string value, float containerSize, List<GridTrack> tracks)
    {
        int i = 0;
        while (i < value.Length)
        {
            if (char.IsWhiteSpace(value[i])) { i++; continue; }
            if (value[i] == ',') { i++; continue; }

            if (i + 6 < value.Length && value.Substring(i, 7).ToLowerInvariant() == "repeat(")
            {
                i += 7;
                int endParen = FindMatchingParen(value, i);
                if (endParen < 0) break;
                var repeatContent = value[i..endParen];
                i = endParen + 1;
                int commaIdx = repeatContent.IndexOf(',');
                if (commaIdx < 0) continue;
                var countStr = repeatContent[..commaIdx].Trim().ToLowerInvariant();
                var trackStr = repeatContent[(commaIdx + 1)..].Trim();
                int repeatCount = 0;
                bool autoFit = countStr == "auto-fit";
                bool autoFill = countStr == "auto-fill" || autoFit;

                if (!autoFill)
                {
                    if (!int.TryParse(countStr, out repeatCount) || repeatCount <= 0) continue;
                }

                var repeatTracks = new List<GridTrack>();
                ParseTrackListValue(trackStr, containerSize, repeatTracks);
                if (repeatTracks.Count == 0) continue;

                if (autoFill)
                {
                    // Initialize track base sizes so minmax(80px, 1fr) reports 80
                    // (not 0) in the fitting computation below.
                    foreach (var t in repeatTracks)
                        t.Initialize(containerSize, _containerStyle?.FontSize ?? 16, _viewportWidth, _viewportHeight);

                    float totalGap = _containerStyle?.ColumnGap.ToPixels(_containerStyle.FontSize, _rootFontSize, _viewportWidth, _viewportHeight) ?? 0;
                    float totalTrackSize = 0;
                    foreach (var t in repeatTracks)
                        totalTrackSize += t.BaseSize;
                    float gapTotal = totalGap * (repeatTracks.Count - 1);
                    // One repeat block is its tracks plus the gaps inside it; blocks
                    // are separated by another gap. N blocks fit when
                    // N*blockWidth + (N-1)*totalGap <= containerSize, i.e.
                    // N = (containerSize + totalGap) / (blockWidth + totalGap). The
                    // old formula dropped the between-block gap and over-counted the
                    // repeats (minmax(60px,1fr) in 300px produced 5 tracks, not 4).
                    float blockWidth = totalTrackSize + gapTotal;
                    int fits = blockWidth > 0 ? (int)((containerSize + totalGap) / (blockWidth + totalGap)) : 0;
                    if (fits <= 0 && repeatTracks.Count > 0) fits = 1;
                    repeatCount = Math.Max(1, fits);
                }

                for (int r = 0; r < repeatCount; r++)
                {
                    foreach (var t in repeatTracks)
                    {
                        var clone = t.Clone();
                        clone.AutoFit = autoFit;
                        tracks.Add(clone);
                    }
                }
                continue;
            }

            int endIdx = i;
            if (value[endIdx] == '[')
            {
                // A line-name group (CSS Grid §7.1) says what the line is called; it is
                // not a track, and treating it as one gave every named list a phantom
                // zero-sized track between each real one.
                int close = value.IndexOf(']', endIdx);
                if (close < 0) break;
                i = close + 1;
                continue;
            }
            while (endIdx < value.Length && !char.IsWhiteSpace(value[endIdx]) && value[endIdx] != ',')
            {
                if (value[endIdx] == '(')
                {
                    endIdx = FindMatchingParen(value, endIdx + 1);
                    if (endIdx < 0) break;
                    endIdx++;
                }
                else endIdx++;
            }
            var token = value[i..endIdx].Trim();
            i = endIdx;

            if (!string.IsNullOrEmpty(token))
            {
                if (token.StartsWith("minmax(", StringComparison.OrdinalIgnoreCase))
                    tracks.Add(ParseMinMax(token, containerSize));
                else if (token.StartsWith("fit-content(", StringComparison.OrdinalIgnoreCase))
                    tracks.Add(ParseFitContent(token, containerSize));
                else
                    tracks.Add(ParseTrackSize(token, containerSize));
            }
        }
    }

    private static int FindMatchingParen(string s, int start)
    {
        int depth = 1;
        for (int i = start; i < s.Length; i++)
        {
            if (s[i] == '(') depth++;
            else if (s[i] == ')') { depth--; if (depth == 0) return i; }
        }
        return -1;
    }

    private GridTrack ParseTrackSize(string value, float containerSize)
    {
        var track = new GridTrack();
        value = value.Trim().ToLowerInvariant();

        if (value.EndsWith("fr") && float.TryParse(value[..^2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var fr))
        {
            track.SizeType = TrackSizeType.Fraction; track.Fraction = fr;
        }
        else if (value == "auto") track.SizeType = TrackSizeType.Auto;
        else if (value == "min-content") track.SizeType = TrackSizeType.MinContent;
        else if (value == "max-content") track.SizeType = TrackSizeType.MaxContent;
        else if (value.EndsWith("px") && TryParseFloat(value[..^2], out var px)) { track.SizeType = TrackSizeType.Fixed; track.FixedSize = px; }
        else if (value.EndsWith("%") && TryParseFloat(value[..^1], out var pct)) { track.SizeType = TrackSizeType.Percentage; track.Percentage = pct / 100f; }
        else if (value.EndsWith("em") && TryParseFloat(value[..^2], out var em)) { track.SizeType = TrackSizeType.Fixed; track.FixedSize = em * (_containerStyle?.FontSize ?? 16); }
        else if (value.EndsWith("rem") && TryParseFloat(value[..^3], out var rem)) { track.SizeType = TrackSizeType.Fixed; track.FixedSize = rem * _rootFontSize; }
        else if (value.EndsWith("vw") && TryParseFloat(value[..^2], out var vw)) { track.SizeType = TrackSizeType.Fixed; track.FixedSize = vw * _viewportWidth / 100f; }
        else if (value.EndsWith("vh") && TryParseFloat(value[..^2], out var vh)) { track.SizeType = TrackSizeType.Fixed; track.FixedSize = vh * _viewportHeight / 100f; }
        else if (value == "0") { track.SizeType = TrackSizeType.Fixed; track.FixedSize = 0; }
        else if (value.StartsWith("calc(") || value.StartsWith("min(") || value.StartsWith("max(")
                 || value.StartsWith("clamp("))
        {
            // A math-function track size (CSS Values 3 §4 / css-grid §7.2.1) resolves
            // against the container's content box; treat the result as a fixed track.
            // Percentages inside the expression use containerSize as their base.
            float mathPx = Length.Parse(value).ToPixels(containerSize, _containerStyle?.FontSize ?? 16, _viewportWidth, _viewportHeight);
            if (!float.IsNaN(mathPx) && !float.IsInfinity(mathPx))
            {
                track.SizeType = TrackSizeType.Fixed;
                track.FixedSize = Math.Max(0, mathPx);
            }
            else
            {
                track.SizeType = TrackSizeType.Auto;
            }
        }

        track.BaseSize = track.ResolveSize(containerSize, _containerStyle?.FontSize ?? 16, _viewportWidth, _viewportHeight);
        return track;
    }

    private static bool TryParseFloat(string s, out float result) =>
        float.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out result);

    private GridTrack ParseMinMax(string value, float containerSize)
    {
        var track = new GridTrack { SizeType = TrackSizeType.MinMax };
        var inner = value[7..^1];
        int commaIdx = FindMinMaxComma(inner);
        if (commaIdx > 0)
        {
            track.MinSize = ParseTrackSize(inner[..commaIdx].Trim(), containerSize);
            track.MaxSize = ParseTrackSize(inner[(commaIdx + 1)..].Trim(), containerSize);
        }
        return track;
    }

    private static int FindMinMaxComma(string s)
    {
        int depth = 0;
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '(') depth++;
            else if (s[i] == ')') depth--;
            else if (s[i] == ',' && depth == 0) return i;
        }
        return -1;
    }

    private GridTrack ParseFitContent(string value, float containerSize)
    {
        var track = new GridTrack { SizeType = TrackSizeType.MinMax };
        var inner = value[11..^1];
        track.MinSize = new GridTrack { SizeType = TrackSizeType.Auto };
        track.MaxSize = ParseTrackSize(inner.Trim(), containerSize);
        return track;
    }

    private List<GridItem> CollectAndPlaceItems(Element container, int explicitColCount, int explicitRowCount, List<string[]> areas, float columnGap, float rowGap)
    {
        var items = new List<GridItem>();
        var namedAreas = new Dictionary<string, (int col, int row, int colSpan, int rowSpan)>();

        if (areas.Count > 0) BuildNamedAreaMap(areas, namedAreas);

        var autoFlow = _containerStyle?.GridAutoFlow ?? GridAutoFlowType.Row;
        bool densePacking = autoFlow is GridAutoFlowType.Dense or GridAutoFlowType.ColumnDense;
        // 'column' flow advances the auto-placement cursor down each column
        // before moving to the next (CSS Grid §8.5.1).
        bool columnFlow = autoFlow is GridAutoFlowType.Column or GridAutoFlowType.ColumnDense;

        int autoCursorCol = 0;
        int autoCursorRow = 0;
        int maxCol = explicitColCount;
        int maxRow = explicitRowCount;

        // Collect items with explicit placement first. Items that only set one
        // of the two axes (e.g. grid-column but no grid-row) are "axis-locked":
        // they keep their definite column/row and are auto-placed on the other.
        var explicitItems = new List<GridItem>();
        var columnLockedItems = new List<GridItem>();
        var rowLockedItems = new List<GridItem>();
        var autoItems = new List<GridItem>();

        foreach (var child in container.Children)
        {
            if (child is not Element childElement) continue;
            var childStyle = childElement.ComputedStyle;
            if (childStyle == null || childStyle.Display == DisplayType.None) continue;

            var item = new GridItem { Element = childElement };
            // GridItem fields default to 1..2; start from "auto" so a missing
            // axis is detected as unspecified (0) for auto-placement purposes.
            item.ColumnStart = 0; item.ColumnEnd = 0; item.RowStart = 0; item.RowEnd = 0;
            var style = childElement.ComputedStyle!;
            bool hasExplicit = false;

            // The shorthand expander rewrites `grid-area: <name>` into the four
            // line longhands; identical non-numeric names on both axes is the
            // signature of a named area.
            string? areaCandidate = style.GridArea;
            if (string.IsNullOrEmpty(areaCandidate) &&
                !string.IsNullOrEmpty(style.GridRowStart) &&
                string.Equals(style.GridRowStart, style.GridColumnStart, StringComparison.OrdinalIgnoreCase) &&
                !char.IsDigit(style.GridRowStart[0]) && style.GridRowStart != "auto")
                areaCandidate = style.GridRowStart;
            if (!string.IsNullOrEmpty(areaCandidate))
            {
                var areaName = areaCandidate.Trim().ToLowerInvariant();
                if (namedAreas.TryGetValue(areaName, out var area))
                {
                    item.ColumnStart = area.col + 1;
                    item.ColumnEnd = area.col + area.colSpan + 1;
                    item.RowStart = area.row + 1;
                    item.RowEnd = area.row + area.rowSpan + 1;
                    hasExplicit = true;
                }
            }

            if (!hasExplicit)
            {
                var (colStart, colEnd, colSpan) = ParseGridLine(style, "grid-column-start", "grid-column-end", explicitColCount);
                var (rowStart, rowEnd, rowSpan) = ParseGridLine(style, "grid-row-start", "grid-row-end", explicitRowCount);
                if (colStart != 0 || colEnd != 0) { item.ColumnStart = colStart; item.ColumnEnd = colEnd; hasExplicit = true; }
                else if (colSpan > 1) item.ColumnSpan = colSpan;
                if (rowStart != 0 || rowEnd != 0) { item.RowStart = rowStart; item.RowEnd = rowEnd; hasExplicit = true; }
                else if (rowSpan > 1) item.RowSpan = rowSpan;
            }

            if (!hasExplicit)
            {
                item.ColumnStart = 0; item.ColumnEnd = 0; item.RowStart = 0; item.RowEnd = 0;
            }

            // Resolve spans on the explicitly-optional grid axes. An item that
            // only sets a column (or only a row) is placed on that axis and
            // auto-placed on the other; defaulting the missing axis to row/col 1
            // here would stack e.g. several ".item { grid-column: 3 }" items onto
            // a single row, so the missing axis is left undefined for the
            // auto-placement step instead (CSS Grid auto-placement).
            if (item.ColumnEnd <= item.ColumnStart && item.ColumnEnd != 0) item.ColumnEnd = item.ColumnStart + 1;
            if (item.RowEnd <= item.RowStart && item.RowEnd != 0) item.RowEnd = item.RowStart + 1;
            if (item.ColumnEnd == 0 && item.ColumnStart != 0) item.ColumnEnd = item.ColumnStart + 1;
            if (item.RowEnd == 0 && item.RowStart != 0) item.RowEnd = item.RowStart + 1;
            if (item.ColumnStart == 0 && item.ColumnEnd != 0) item.ColumnStart = Math.Max(1, item.ColumnEnd - 1);
            if (item.RowStart == 0 && item.RowEnd != 0) item.RowStart = Math.Max(1, item.RowEnd - 1);

            bool colSpecified = item.ColumnStart != 0 || item.ColumnEnd != 0;
            bool rowSpecified = item.RowStart != 0 || item.RowEnd != 0;

            if (colSpecified && rowSpecified)
            {
                item.ColumnSpan = item.ColumnEnd - item.ColumnStart;
                item.RowSpan = item.RowEnd - item.RowStart;
                explicitItems.Add(item);
            }
            else if (colSpecified)
            {
                item.ColumnSpan = item.ColumnEnd - item.ColumnStart;
                // The row axis is auto-placed, but an explicit 'grid-row: span N'
                // still sets a row span — keep it rather than forcing 1.
                item.RowSpan = Math.Max(1, item.RowSpan);
                columnLockedItems.Add(item);
            }
            else if (rowSpecified)
            {
                item.ColumnSpan = Math.Max(1, item.ColumnSpan);
                item.RowSpan = item.RowEnd - item.RowStart;
                rowLockedItems.Add(item);
            }
            else
            {
                // Auto-placed on both axes; keep any 'span N' parsed earlier.
                item.ColumnSpan = Math.Max(1, item.ColumnSpan);
                item.RowSpan = Math.Max(1, item.RowSpan);
                autoItems.Add(item);
            }
        }

        // Place explicit items
        foreach (var item in explicitItems)
        {
            maxCol = Math.Max(maxCol, item.ColumnEnd - 1);
            maxRow = Math.Max(maxRow, item.RowEnd - 1);
            item.IsPlaced = true;
            items.Add(item);
        }

        // Place items locked to a definite row (auto column) at the earliest
        // free column of that row (CSS Grid auto-placement step 1).
        PlaceRowLockedItems(rowLockedItems, items, densePacking, ref maxCol, ref maxRow, ref autoCursorCol, ref autoCursorRow);

        // Place items locked to a definite column (auto row) at the first row
        // from the auto-placement cursor whose spanned columns are empty
        // (CSS Grid auto-placement step 2).
        PlaceColumnLockedItems(columnLockedItems, items, densePacking, ref maxCol, ref maxRow, ref autoCursorCol, ref autoCursorRow);

        // Auto-placement (CSS Grid §8.5.1). The cursor advances along the line
        // axis (columns for row flow, rows for column flow) and wraps to the next
        // cross track when the item no longer fits; dense packing restarts the
        // scan from the beginning of the grid for every item.
        int lineCount = Math.Max(1, columnFlow
            ? Math.Max(explicitRowCount, maxRow)
            : Math.Max(explicitColCount, maxCol));
        int cursorOuter = columnFlow ? autoCursorCol : autoCursorRow;
        int cursorInner = columnFlow ? autoCursorRow : autoCursorCol;
        foreach (var item in autoItems)
        {
            int lineSpan = columnFlow ? item.RowSpan : item.ColumnSpan;
            if (lineSpan > lineCount) lineCount = lineSpan;
            if (densePacking)
            {
                cursorOuter = 0;
                cursorInner = 0;
            }
            bool placed = false;
            int o = cursorOuter;
            while (!placed)
            {
                for (int i = (o == cursorOuter ? cursorInner : 0); i + lineSpan <= lineCount; i++)
                {
                    int c = columnFlow ? o : i;
                    int r = columnFlow ? i : o;
                    if (!IsOccupied(items, c, r, item.ColumnSpan, item.RowSpan))
                    {
                        item.ColumnStart = c + 1;
                        item.RowStart = r + 1;
                        item.ColumnEnd = item.ColumnStart + item.ColumnSpan;
                        item.RowEnd = item.RowStart + item.RowSpan;
                        item.IsPlaced = true;
                        maxCol = Math.Max(maxCol, item.ColumnEnd - 1);
                        maxRow = Math.Max(maxRow, item.RowEnd - 1);
                        cursorOuter = o;
                        cursorInner = i + lineSpan;
                        placed = true;
                        break;
                    }
                }
                if (!placed)
                {
                    o++;
                    cursorOuter = o;
                    cursorInner = 0;
                }
            }
            items.Add(item);
        }

        return items;
    }

    private static bool IsOccupied(List<GridItem> items, int col, int row, int colSpan, int rowSpan)
    {
        foreach (var item in items)
        {
            if (!item.IsPlaced) continue;
            int itemColStart = item.ColumnStart - 1;
            int itemRowStart = item.RowStart - 1;
            int itemColEnd = item.ColumnEnd - 1;
            int itemRowEnd = item.RowEnd - 1;

            // Check overlap
            if (col < itemColEnd && col + colSpan > itemColStart &&
                row < itemRowEnd && row + rowSpan > itemRowStart)
                return true;
        }
        return false;
    }

    // Auto-placement step 1: items with a definite row but auto column are
    // placed at the earliest free column of their row, advancing the column
    // cursor (dense packing always starts from column 0).
    private static void PlaceRowLockedItems(List<GridItem> rowLocked, List<GridItem> placed, bool dense,
        ref int maxCol, ref int maxRow, ref int cursorCol, ref int cursorRow)
    {
        foreach (var item in rowLocked)
        {
            int row = item.RowStart - 1;
            int rowSpan = item.RowSpan;
            int colSpan = item.ColumnSpan;
            int c = dense ? 0 : cursorCol;
            for (; ; c++)
            {
                if (!IsOccupied(placed, c, row, colSpan, rowSpan))
                {
                    item.ColumnStart = c + 1;
                    item.ColumnEnd = item.ColumnStart + colSpan;
                    item.RowStart = row + 1;
                    item.RowEnd = item.RowStart + rowSpan;
                    item.IsPlaced = true;
                    maxCol = Math.Max(maxCol, item.ColumnEnd - 1);
                    maxRow = Math.Max(maxRow, item.RowEnd - 1);
                    cursorCol = c + colSpan;
                    break;
                }
            }
            placed.Add(item);
        }
    }

    // Auto-placement step 2: items with a definite column but auto row are
    // placed at the first row from the auto-placement cursor whose spanned
    // columns are all empty, advancing the row cursor (dense packing always
    // starts from row 0).
    private static void PlaceColumnLockedItems(List<GridItem> columnLocked, List<GridItem> placed, bool dense,
        ref int maxCol, ref int maxRow, ref int cursorCol, ref int cursorRow)
    {
        foreach (var item in columnLocked)
        {
            int colStart = item.ColumnStart - 1;
            int colSpan = item.ColumnSpan;
            int rowSpan = item.RowSpan;
            int r = dense ? 0 : cursorRow;
            for (; ; r++)
            {
                if (!IsOccupied(placed, colStart, r, colSpan, rowSpan))
                {
                    item.ColumnStart = colStart + 1;
                    item.ColumnEnd = item.ColumnStart + colSpan;
                    item.RowStart = r + 1;
                    item.RowEnd = item.RowStart + rowSpan;
                    item.IsPlaced = true;
                    maxCol = Math.Max(maxCol, item.ColumnEnd - 1);
                    maxRow = Math.Max(maxRow, item.RowEnd - 1);
                    cursorRow = r + 1;
                    cursorCol = 0;
                    break;
                }
            }
            placed.Add(item);
        }
    }

    private void BuildNamedAreaMap(List<string[]> areas, Dictionary<string, (int col, int row, int colSpan, int rowSpan)> map)
    {
        for (int r = 0; r < areas.Count; r++)
        {
            for (int c = 0; c < areas[r].Length; c++)
            {
                string name = areas[r][c].ToLowerInvariant();
                if (name == "." || string.IsNullOrEmpty(name)) continue;
                if (map.ContainsKey(name)) continue;

                // Find the span of this area
                int colSpan = 1, rowSpan = 1;
                while (c + colSpan < areas[r].Length && areas[r][c + colSpan].ToLowerInvariant() == name) colSpan++;
                for (int rr = r + 1; rr < areas.Count; rr++)
                {
                    bool allMatch = true;
                    for (int cc = c; cc < c + colSpan && cc < areas[rr].Length; cc++)
                    {
                        if (areas[rr][cc].ToLowerInvariant() != name) { allMatch = false; break; }
                    }
                    if (allMatch && areas[rr].Length >= c + colSpan) rowSpan++;
                    else break;
                }
                map[name] = (c, r, colSpan, rowSpan);
            }
        }
    }

    private static (int start, int end, int span) ParseGridLine(ComputedStyle style, string startProp, string endProp, int explicitCount)
    {
        int start = 0, end = 0;
        var startVal = startProp switch
        {
            "grid-column-start" => style.GridColumnStart,
            "grid-column-end" => style.GridColumnEnd,
            "grid-row-start" => style.GridRowStart,
            "grid-row-end" => style.GridRowEnd,
            _ => null
        };
        var endVal = endProp switch
        {
            "grid-column-start" => style.GridColumnStart,
            "grid-column-end" => style.GridColumnEnd,
            "grid-row-start" => style.GridRowStart,
            "grid-row-end" => style.GridRowEnd,
            _ => null
        };

        if (!string.IsNullOrEmpty(startVal))
        {
            if (TryParseSpan(startVal, out var sn))
                start = -sn; // negative encodes "span n, auto line"
            else if (int.TryParse(startVal, out var s))
                start = s > 0 ? s : ResolveNegativeGridLine(s, explicitCount);
        }

        if (!string.IsNullOrEmpty(endVal))
        {
            if (TryParseSpan(endVal, out var en))
                end = -en;
            else if (int.TryParse(endVal, out var e))
                end = e > 0 ? e : ResolveNegativeGridLine(e, explicitCount);
        }

        // Resolve the span encodings against concrete lines.
        int span = 1;
        if (start < 0 && end > 0) start = Math.Max(1, end + start);
        else if (end < 0 && start > 0) end = start - end;
        else if (start < 0 && end < 0) { span = Math.Max(-start, -end); start = 0; end = 0; }
        else if (start < 0) { span = -start; start = 0; }
        else if (end < 0) { span = -end; }

        return (start, end, span);
    }

    private static bool TryParseSpan(string value, out int span)
    {
        span = 0;
        value = value.Trim();
        if (!value.StartsWith("span", StringComparison.OrdinalIgnoreCase)) return false;
        var rest = value.Length > 4 ? value[4..].Trim() : "";
        if (rest.Length == 0) span = 1;
        else if (int.TryParse(rest, out var n) && n > 0) span = n;
        else return false;
        return true;
    }

    /// <summary>
    /// Map a negative grid line (-1 = last line of the explicit grid, -2 =
    /// second to last, ...) onto its positive 1-based line index.
    /// Explicit line count for N tracks is N+1 (edges), so -1 resolves to N+1.
    /// </summary>
    private static int ResolveNegativeGridLine(int line, int explicitTrackCount) =>
        line + explicitTrackCount + 2;

    private void ExpandImplicitTracks(List<GridItem> items, ref List<GridTrack> columns, ref List<GridTrack> rows)
    {
        int maxCol = columns.Count;
        int maxRow = rows.Count;
        foreach (var item in items)
        {
            maxCol = Math.Max(maxCol, item.ColumnEnd - 1);
            maxRow = Math.Max(maxRow, item.RowEnd - 1);
        }

        // Implicit (overflowing) tracks are sized by grid-auto-columns /
        // grid-auto-rows (default "auto"); the pattern repeats for every
        // implicit track, matching the CSS track-list repetition.
        var colPattern = ParseImplicitTrackPattern(_containerStyle?.GridAutoColumns, _containerWidth, isColumn: true);
        var rowPattern = ParseImplicitTrackPattern(_containerStyle?.GridAutoRows, _containerHeight, isColumn: false);
        int ci = 0, ri = 0;
        while (columns.Count < maxCol)
            columns.Add(colPattern[ci++ % colPattern.Count].Clone());
        while (rows.Count < maxRow)
            rows.Add(rowPattern[ri++ % rowPattern.Count].Clone());
    }

    private List<GridTrack> ParseImplicitTrackPattern(string? propertyValue, float containerSize, bool isColumn)
    {
        if (!string.IsNullOrEmpty(propertyValue) && !propertyValue.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            var tracks = new List<GridTrack>();
            ParseTrackListValue(propertyValue.Replace(',', ' '), containerSize, tracks);
            if (tracks.Count > 0) return tracks;
        }
        var fallback = new GridTrack { SizeType = TrackSizeType.Auto };
        // Preserve the historical sizing floor for default auto tracks.
        fallback.BaseSize = isColumn ? 100 : 20;
        return new List<GridTrack> { fallback };
    }

    /// <summary>
    /// Intrinsic inline/block track sizing (CSS Grid §12.6, container
    /// `width: min-content|max-content`). Because the container is indefinite there
    /// is no free space to distribute, so — unlike the definite-container
    /// <see cref="ResolveTracks"/> — the auto-track stretch and fr expansion steps
    /// are skipped and each track resolves straight to its content contribution:
    /// min-content takes the min sizing function, max-content takes the growth
    /// limit, and a flexible track is sized by the largest per-factor contribution
    /// (`frUnit = max(contributionᵢ / factorᵢ)`, then `factorᵢ × frUnit`).
    /// </summary>
    private void SolveIntrinsicTracks(
        List<GridTrack> tracks, List<GridItem> items, float gap, bool isColumn, IntrinsicSizeKind kind)
    {
        int n = tracks.Count;
        if (n == 0) return;

        var minC = new float[n];   // largest item min-content contribution per track
        var maxC = new float[n];   // largest item max-content contribution per track
        var used = new bool[n];

        foreach (var item in items)
        {
            var style = item.Element.ComputedStyle;
            if (style == null) continue;
            int start = isColumn ? item.ColumnStart - 1 : item.RowStart - 1;
            int end = isColumn ? item.ColumnEnd - 1 : item.RowEnd - 1;
            int span = end - start;
            if (span <= 0) continue;

            float outerDef = isColumn
                ? ResolveDefiniteSize(style.Width, float.NaN, style.FontSize, _rootFontSize)
                : ResolveDefiniteSize(style.Height, float.NaN, style.FontSize, _rootFontSize);

            float itemMin, itemMax;
            if (outerDef > 0)
            {
                itemMin = itemMax = outerDef;
            }
            else if (isColumn)
            {
                itemMax = IntrinsicMeasure.MaxContentInlineSize(item.Element);
                itemMin = IntrinsicMeasure.MinContentInlineSize(item.Element);
                if (float.IsNaN(itemMin) || itemMin < 0) itemMin = 0;
                if (float.IsNaN(itemMax) || itemMax < 0) itemMax = 0;
                if (itemMin <= 0) itemMin = itemMax;
                if (itemMax <= 0) itemMax = itemMin;
            }
            else
            {
                itemMin = itemMax = item.MeasuredBox?.BorderBox.Height ?? 0;
            }

            float pm = itemMin / span, pM = itemMax / span;
            for (int i = start; i < end && i < n; i++)
            {
                if (i < 0) continue;
                used[i] = true;
                if (pm > minC[i]) minC[i] = pm;
                if (pM > maxC[i]) maxC[i] = pM;
            }
        }

        // Collapse empty auto-fit tracks (CSS Grid §7.2.3): no item spans them, so
        // they and their gaps vanish.
        if (isColumn)
            for (int i = 0; i < n; i++)
                if (tracks[i].AutoFit && !used[i]) tracks[i].Collapsed = true;

        // Pass 1: base size from the min sizing function (raised by the item's
        // min-content contribution only when that min side is auto/intrinsic), and
        // find the flexible-track unit for max-content sizing.
        float frUnit = 0;
        for (int i = 0; i < n; i++)
        {
            var t = tracks[i];
            if (t.Collapsed) { t.BaseSize = 0; t.GrowLimit = 0; continue; }

            float baseMin = IntrinsicMinFnValue(t);
            if (IntrinsicMinFnIsItemDriven(t)) baseMin = Math.Max(baseMin, minC[i]);
            t.BaseSize = baseMin;

            if (IsFlexible(t))
            {
                float f = FrFactor(t);
                if (f > 0)
                    frUnit = Math.Max(frUnit, Math.Max(baseMin, maxC[i]) / f);
                t.GrowLimit = float.MaxValue;
            }
            else
            {
                t.GrowLimit = IntrinsicMaxFnSize(t, baseMin, maxC[i]);
            }
        }

        // Pass 2: commit each track's size for the requested intrinsic kind.
        for (int i = 0; i < n; i++)
        {
            var t = tracks[i];
            if (t.Collapsed) { t.BaseSize = 0; t.GrowLimit = 0; continue; }

            float size;
            if (IsFlexible(t))
                size = kind == IntrinsicSizeKind.MaxContent
                    ? Math.Max(t.BaseSize, FrFactor(t) * frUnit)
                    : t.BaseSize;
            else if (kind == IntrinsicSizeKind.MaxContent)
                size = t.GrowLimit == float.MaxValue ? t.BaseSize : Math.Max(t.BaseSize, t.GrowLimit);
            else
                size = t.BaseSize;

            t.BaseSize = Math.Max(0, size);
            t.GrowLimit = t.BaseSize;
        }
    }

    /// <summary>Value of a track's min sizing function when it is a definite length
    /// (a fixed track, or a minmax with a fixed min). Returns 0 otherwise so the
    /// item's contribution can fill it.</summary>
    private static float IntrinsicMinFnValue(GridTrack t) => t.SizeType switch
    {
        TrackSizeType.Fixed => t.FixedSize,
        TrackSizeType.MinMax => t.MinSize?.SizeType == TrackSizeType.Fixed ? t.MinSize!.FixedSize : 0f,
        _ => 0f,
    };

    /// <summary>Whether the track's min side is auto/intrinsic (so an item's
    /// min-content contribution can raise it). A definite min (px or a percentage
    /// resolved as definite) and a minmax(50px, …) keep their declared floor instead.
    /// Percentage is treated as auto under an indefinite container.</summary>
    private static bool IntrinsicMinFnIsItemDriven(GridTrack t) => t.SizeType switch
    {
        TrackSizeType.Auto or TrackSizeType.MinContent or TrackSizeType.MaxContent
            or TrackSizeType.Fraction or TrackSizeType.Percentage => true,
        TrackSizeType.MinMax => t.MinSize?.SizeType is null or TrackSizeType.Auto or TrackSizeType.Percentage,
        _ => false,
    };

    /// <summary>Max sizing function resolved against an intrinsic contribution:
    /// min-content → the base; auto / max-content / percentage → the item's
    /// max-content; a fixed or minmax-with-fixed-max keeps its declared ceiling.</summary>
    private static float IntrinsicMaxFnSize(GridTrack t, float baseMin, float maxContrib) => t.SizeType switch
    {
        TrackSizeType.Fixed => t.FixedSize,
        TrackSizeType.MinContent => baseMin,
        TrackSizeType.Auto or TrackSizeType.MaxContent or TrackSizeType.Percentage => Math.Max(baseMin, maxContrib),
        TrackSizeType.MinMax => t.MaxSize?.SizeType switch
        {
            TrackSizeType.Fixed => t.MaxSize!.FixedSize,
            TrackSizeType.MinContent => baseMin,
            _ => Math.Max(baseMin, maxContrib),
        },
        _ => Math.Max(baseMin, maxContrib),
    };

    private void ResolveTracks(List<GridTrack> tracks, List<GridItem> items, float containerSize, float gap, bool isColumn)
    {
        if (tracks.Count == 0) return;

        // Step 1: Initialize base sizes from min/max constraints
        foreach (var track in tracks)
            track.Initialize(containerSize, _containerStyle?.FontSize ?? 16, _viewportWidth, _viewportHeight);

        // Step 2: Calculate item contributions for intrinsic sizing
        foreach (var item in items)
        {
            var style = item.Element.ComputedStyle;
            if (style == null) continue;

            if (isColumn)
            {
                int start = item.ColumnStart - 1;
                int end = item.ColumnEnd - 1;
                float itemSize = ResolveDefiniteSize(style.Width, containerSize, style.FontSize, _rootFontSize);

                if (itemSize > 0)
                {
                    float perTrackSize = itemSize / (end - start);
                    for (int i = start; i < end && i < tracks.Count; i++)
                    {
                        tracks[i].BaseSize = Math.Max(tracks[i].BaseSize, perTrackSize);
                        tracks[i].GrowLimit = Math.Max(tracks[i].GrowLimit, perTrackSize);
                    }
                }
                else if (end > start)
                {
                    // Automatic minimum size (css-grid §6.7): an item whose min-width
                    // is 'auto' cannot be squeezed below its min-content size, so the
                    // tracks it spans are floored at that contribution (an unbreakable
                    // word widens its column instead of overflowing).
                    float span = end - start;
                    if (style.MinWidth is AutoLength or null)
                    {
                        float minContent = IntrinsicMeasure.MinContentInlineSize(item.Element);
                        float perTrackSize = minContent / span;
                        if (perTrackSize > 0)
                            for (int i = start; i < end && i < tracks.Count; i++)
                                RaiseAutoMinimum(tracks[i], perTrackSize);
                    }
                    // The max side of an auto / max-content track is the item's
                    // max-content size (§12.5). Without this an auto track's growth
                    // limit stays 0 (treated as unbounded), so it greedily absorbs all
                    // free space and starves sibling fr tracks; a max-content track
                    // never reaches its content width at all.
                    float maxContent = IntrinsicMeasure.MaxContentInlineSize(item.Element);
                    float perMax = maxContent / span;
                    if (perMax > 0)
                        for (int i = start; i < end && i < tracks.Count; i++)
                            RaiseGrowLimit(tracks[i], perMax);
                }
            }
            else
            {
                int start = item.RowStart - 1;
                int end = item.RowEnd - 1;
                float itemSize = ResolveDefiniteSize(style.Height, containerSize, style.FontSize, _rootFontSize);

                if (itemSize > 0)
                {
                    float perTrackSize = itemSize / (end - start);
                    for (int i = start; i < end && i < tracks.Count; i++)
                    {
                        tracks[i].BaseSize = Math.Max(tracks[i].BaseSize, perTrackSize);
                        tracks[i].GrowLimit = Math.Max(tracks[i].GrowLimit, perTrackSize);
                    }
                }
                else if (item.MeasuredBox != null && end > start)
                {
                    // Auto-height items contribute their measured content height
                    // to intrinsic row tracks (auto/min-content/max-content and
                    // the min side of minmax). Definite tracks keep their
                    // declared size and let taller content overflow, per spec.
                    float perTrackSize = item.MeasuredBox.BorderBox.Height / (end - start);
                    if (perTrackSize > 0)
                    {
                        for (int i = start; i < end && i < tracks.Count; i++)
                            ContributeIntrinsicRowSize(tracks[i], perTrackSize);
                    }
                }
            }
        }

        // Step 2b: Collapse empty auto-fit tracks (CSS Grid §7.2.3). A repeat
        // (auto-fit, …) track that no item spans shrinks to zero and its adjacent
        // gaps vanish, so the remaining tracks (and their fr share) fill the whole
        // container; auto-fill keeps its empty tracks. Only the column axis carries
        // auto-fit here, and the collapse runs after item contributions so the
        // used/empty state is known.
        if (isColumn && tracks.Any(t => t.AutoFit))
        {
            var used = new bool[tracks.Count];
            foreach (var item in items)
            {
                int s = item.ColumnStart - 1, e = item.ColumnEnd - 1;
                for (int i = s; i < e && i < used.Length; i++)
                    if (i >= 0) used[i] = true;
            }
            for (int i = 0; i < tracks.Count; i++)
            {
                var t = tracks[i];
                if (t.AutoFit && !used[i])
                {
                    t.Collapsed = true;
                    t.SizeType = TrackSizeType.Fixed;
                    t.Fraction = 0;
                    t.MinSize = null;
                    t.MaxSize = null;
                    t.BaseSize = 0;
                    t.GrowLimit = 0;
                }
            }
        }

        // Step 3: Maximize tracks (distribute positive free space)
        float totalUsed = 0;
        foreach (var t in tracks)
            totalUsed += t.BaseSize;

        // Gaps only separate non-collapsed tracks, so an auto-fit collapse also
        // removes the space those tracks' gaps would have taken.
        int liveTracks = tracks.Count(t => !t.Collapsed);
        float totalGap = gap * Math.Max(0, liveTracks - 1);
        float freeSpace = containerSize - totalUsed - totalGap;

        if (freeSpace > 0)
        {
            // Distribute to non-flexible tracks first (§12.6 maximizes only tracks
            // with an intrinsic min sizing function; fr tracks wait for §12.7).
            // Collapsed auto-fit tracks stay frozen at zero and must not absorb any
            // of this space, or the fr tracks would never expand.
            bool hasFlexible = tracks.Any(IsFlexible);
            int nonFrCount = tracks.Count(t => !IsFlexible(t) && !t.Collapsed && Maximizable(t, hasFlexible));
            if (nonFrCount > 0)
            {
                float perTrack = freeSpace / nonFrCount;
                foreach (var t in tracks)
                {
                    if (!IsFlexible(t) && !t.Collapsed && Maximizable(t, hasFlexible))
                    {
                        float growLimit = t.GrowLimit > 0 ? t.GrowLimit : float.MaxValue;
                        float add = Math.Min(perTrack, growLimit - t.BaseSize);
                        if (add > 0)
                        {
                            t.BaseSize += add;
                            freeSpace -= add;
                        }
                    }
                }
            }
        }

        // Step 4: Stretch auto tracks. Only when there is no flexible (fr) track to
        // compete for the leftover space; otherwise fr must resolve first (§12.7), or
        // an auto track would absorb all free space and starve the fr track.
        if (freeSpace > 0 && !tracks.Any(IsFlexible))
        {
            int autoCount = tracks.Count(t => t.SizeType == TrackSizeType.Auto);
            if (autoCount > 0)
            {
                float perTrack = freeSpace / autoCount;
                foreach (var t in tracks)
                {
                    if (t.SizeType == TrackSizeType.Auto)
                    {
                        t.BaseSize += perTrack;
                    }
                }
                freeSpace = 0;
            }
        }

        // Step 5: Expand flexible (fr) tracks
        if (freeSpace > 0 || tracks.Any(t => t.SizeType == TrackSizeType.Fraction))
        {
            ExpandFlexibleTracks(tracks, containerSize, freeSpace, totalGap);
        }

        // Clamp
        foreach (var t in tracks)
            t.BaseSize = Math.Max(t.BaseSize, 0);
    }

    /// <summary>
    /// CSS Grid §12.7 "find the size of an fr". The space to fill is what is left of
    /// the container once the non-flexible tracks took their share; a flexible track
    /// never ends up smaller than its base size (which already carries the items'
    /// automatic minimum sizes), so such a track is frozen at that base size and the
    /// remaining space is divided again.
    /// </summary>
    private static void ExpandFlexibleTracks(List<GridTrack> tracks, float containerSize, float freeSpace, float totalGap)
    {
        // Under an indefinite (max-content) container there is no free space to
        // distribute, so flexible tracks keep their base size (their content
        // contribution) rather than expanding to infinity.
        if (float.IsInfinity(containerSize)) return;
        var flexible = new bool[tracks.Count];
        float totalFr = 0;
        float nonFrUsed = 0;
        for (int i = 0; i < tracks.Count; i++)
        {
            flexible[i] = IsFlexible(tracks[i]);
            if (flexible[i])
                totalFr += FrFactor(tracks[i]);
            else
                nonFrUsed += tracks[i].BaseSize;
        }

        if (totalFr <= 0) return;

        float spaceToFill = containerSize - nonFrUsed - totalGap;
        for (;;)
        {
            float hypothetical = spaceToFill > 0 ? spaceToFill / totalFr : 0f;

            int frozen = -1;
            for (int i = 0; i < tracks.Count; i++)
            {
                if (!flexible[i]) continue;
                if (tracks[i].BaseSize > hypothetical * FrFactor(tracks[i]))
                {
                    frozen = i;
                    break;
                }
            }
            if (frozen < 0)
            {
                for (int i = 0; i < tracks.Count; i++)
                {
                    if (!flexible[i]) continue;
                    tracks[i].BaseSize = Math.Max(tracks[i].BaseSize, hypothetical * FrFactor(tracks[i]));
                }
                return;
            }

            // Freeze the offending track at its base size and restart the search.
            spaceToFill -= tracks[frozen].BaseSize;
            totalFr -= FrFactor(tracks[frozen]);
            flexible[frozen] = false;
            if (totalFr <= 0) return;
        }
    }

    /// <summary>A track is flexible when its max sizing function is a fr unit, which
    /// covers both "1fr" and "minmax(auto, 1fr)".</summary>
    private static bool IsFlexible(GridTrack track) =>
        track.SizeType == TrackSizeType.Fraction
        || (track.SizeType == TrackSizeType.MinMax && track.MaxSize?.SizeType == TrackSizeType.Fraction);

    /// <summary>Whether the maximize step may grow this track. A track with no growth limit
    /// measured — an 'auto' whose content never reached the sizing algorithm — has nowhere
    /// sensible to stop, so it must not drink the free space an 'fr' track in the same list is
    /// waiting for (measured: 'grid-template-rows: auto 1fr' in a 60px box is '0px 60px').</summary>
    private static bool Maximizable(GridTrack track, bool hasFlexible)
    {
        if (!hasFlexible) return true;
        return track.GrowLimit > 0 && float.IsFinite(track.GrowLimit);
    }

    private static float FrFactor(GridTrack track) =>
        track.SizeType == TrackSizeType.Fraction ? track.Fraction : (track.MaxSize?.Fraction ?? 0f);

    /// <summary>
    /// Floor a track's base size with an item's automatic minimum size. Only a track
    /// whose min sizing function is 'auto' takes the floor; a definite min
    /// (minmax(10px, …)) or a definite track size wins over the item's contribution
    /// (css-grid §6.7).
    /// </summary>
    private static void RaiseAutoMinimum(GridTrack track, float minimum)
    {
        bool minIsAuto = track.SizeType switch
        {
            TrackSizeType.Auto or TrackSizeType.MinContent or TrackSizeType.MaxContent or TrackSizeType.Fraction => true,
            TrackSizeType.MinMax => track.MinSize?.SizeType is null or TrackSizeType.Auto,
            _ => false,
        };
        if (!minIsAuto || minimum <= track.BaseSize)
            return;
        track.BaseSize = minimum;
        if (track.GrowLimit < minimum)
            track.GrowLimit = minimum;
    }

    /// <summary>
    /// Raise a track's growth limit to an item's max-content contribution. Only a
    /// track whose max sizing function is intrinsic (auto / max-content, or a
    /// minmax with an auto/max-content max) takes it; a definite max or a fixed
    /// track keeps its declared size (css-grid §12.5). A max-content track's base
    /// size also equals the contribution.
    /// </summary>
    private static void RaiseGrowLimit(GridTrack track, float value)
    {
        bool maxIsIntrinsic = track.SizeType switch
        {
            TrackSizeType.Auto or TrackSizeType.MaxContent => true,
            TrackSizeType.MinMax => track.MaxSize?.SizeType is null or TrackSizeType.Auto or TrackSizeType.MaxContent,
            _ => false,
        };
        if (!maxIsIntrinsic)
            return;
        if (value > track.GrowLimit)
            track.GrowLimit = value;
        if (track.SizeType == TrackSizeType.MaxContent && value > track.BaseSize)
            track.BaseSize = value;
    }

    private static float ResolveDefiniteSize(Length? length, float containerSize, float fontSize, float rootFontSize)
    {
        if (length is PixelLength px) return px.Value;
        if (length is PercentLength pct) return pct.Value * containerSize;
        if (length is EmLength em) return em.Value * fontSize;
        if (length is RemLength rem) return rem.Value * rootFontSize;
        return 0;
    }

    /// <summary>
    /// Fold an item's measured content height into an intrinsic row track.
    /// Fixed/percentage/fraction tracks are left alone (definite tracks never
    /// grow to fit content); minmax clamps to its resolved max (GrowLimit).
    /// </summary>
    private static void ContributeIntrinsicRowSize(GridTrack track, float contribution)
    {
        switch (track.SizeType)
        {
            case TrackSizeType.Auto:
            case TrackSizeType.MinContent:
            case TrackSizeType.MaxContent:
                track.BaseSize = Math.Max(track.BaseSize, contribution);
                track.GrowLimit = Math.Max(track.GrowLimit, contribution);
                break;
            case TrackSizeType.MinMax:
                track.BaseSize = Math.Clamp(contribution, track.BaseSize, track.GrowLimit);
                break;
        }
    }

    /// <summary>
    /// Lay out each item once at its resolved column width and keep the
    /// converted box: row track sizing reads real content heights from it and
    /// PositionItems positions the same box instead of re-laying out.
    /// </summary>
    private void MeasureItems(List<GridItem> items, List<GridTrack> columns, float columnGap, LayoutBox containerBox)
    {
        var colOffsets = ComputeTrackOffsets(columns, containerBox.ContentBox.Left, columnGap);
        foreach (var item in items)
        {
            int col = item.ColumnStart - 1;
            int colEnd = Math.Min(item.ColumnEnd - 1, columns.Count);
            if (col < 0 || col >= colOffsets.Length || colEnd < col) continue;

            float cellW = GetTrackSpanSize(columns, col, colEnd, columnGap);
            var mStyle = item.Element.ComputedStyle;
            float marginInline = mStyle != null
                ? LengthUtils.ComputeMargins(_space.WithPercentageResolution(cellW, 0), mStyle).HorizontalSum
                : 0f;
            float fillW = Math.Max(0, cellW - marginInline);
            var childSpace = _space.InheritBuilder(fillW, float.PositiveInfinity)
                .SetIsFixedInlineSize(true)
                .SetIsNewFormattingContext(true)
                .SetPercentageResolution(cellW, float.NaN)
                .ToConstraintSpace();
            var itemResult = new BlockLayoutAlgorithm(item.Element, childSpace).Layout();
            item.MeasuredBox = AuroraFragmentConverter.ToLayoutBox(itemResult.Fragment, item.Element, containerBox);
        }
    }

    private static float[] ComputeTrackOffsets(List<GridTrack> tracks, float origin, float gap)
        => ComputeTrackOffsets(tracks, origin, gap, 0f, 0f);

    private static float[] ComputeTrackOffsets(List<GridTrack> tracks, float origin, float gap,
        float extraGap, float leadingOffset)
    {
        var offsets = new float[tracks.Count + 1];
        float offset = origin + leadingOffset;
        for (int i = 0; i < tracks.Count; i++)
        {
            offsets[i] = offset;
            offset += tracks[i].BaseSize + extraGap;
            // A gap only separates two live tracks; collapsed (auto-fit empty)
            // tracks and the gaps beside them vanish, so skip the gap when either
            // neighbour is collapsed.
            if (!tracks[i].Collapsed && i + 1 < tracks.Count && !tracks[i + 1].Collapsed)
                offset += gap;
        }
        offsets[tracks.Count] = offset;
        return offsets;
    }

    /// <summary>
    /// CSS Grid §12.6 auto track stretching: when the container has a definite
    /// size in an axis, the leftover space is shared equally between the
    /// auto-sized tracks of that axis (fr tracks were already grown).
    /// </summary>
    private static void StretchAutoTracks(List<GridTrack> tracks, float definiteContainer, float gap, float paddingBleed)
    {
        if (tracks.Count == 0 || float.IsNaN(definiteContainer) || float.IsInfinity(definiteContainer))
            return;
        float used = paddingBleed + gap * Math.Max(0, tracks.Count - 1);
        foreach (var t in tracks) used += t.BaseSize;
        float free = definiteContainer - used;
        if (free <= 0) return;
        int autoCount = 0;
        foreach (var t in tracks)
            if (t.SizeType == TrackSizeType.Auto) autoCount++;
        if (autoCount == 0) return;
        float each = free / autoCount;
        foreach (var t in tracks)
            if (t.SizeType == TrackSizeType.Auto) t.BaseSize += each;
    }

    /// <summary>
    /// Resolve `align-content` / `justify-content` for the track grid: returns the
    /// extra gap inserted between tracks and the leading offset of the first track.
    /// </summary>
    private static (float extraGap, float leading) ContentAlignment(string mode, float free, int trackCount)
    {
        if (free <= 0 || trackCount == 0) return (0f, 0f);
        // Callers pass either a raw CSS string ('space-around', from align-content) or
        // an enum name ('SpaceAround', from justify-content.ToString()); normalise both
        // to a de-hyphenated lowercase key so the switch can never silently miss.
        string key = (mode ?? "normal").Trim().ToLowerInvariant().Replace("-", "");
        return key switch
        {
            "center" => (0f, free / 2f),
            "end" or "flexend" or "right" => (0f, free),
            "spacebetween" => trackCount > 1 ? (free / (trackCount - 1), 0f) : (0f, 0f),
            "spacearound" => (free / trackCount, free / trackCount / 2f),
            "spaceevenly" => (free / (trackCount + 1), free / (trackCount + 1)),
            _ => (0f, 0f),
        };
    }

    private static float TrackGroupSize(List<GridTrack> tracks, float gap)
    {
        // Collapsed auto-fit tracks contribute neither size nor gap.
        float size = gap * Math.Max(0, tracks.Count(t => !t.Collapsed) - 1);
        foreach (var t in tracks) size += t.BaseSize;
        return size;
    }

    /// <summary>
    /// A cell spans track sizes only; the gap lives between tracks, so a
    /// k-track span includes k-1 gaps, not k.
    /// </summary>
    private static float GetTrackSpanSize(List<GridTrack> tracks, int start, int end, float gap)
    {
        float size = 0;
        for (int i = start; i < end; i++) size += tracks[i].BaseSize;
        if (end > start + 1) size += (end - start - 1) * gap;
        return size;
    }

    private Dom.LayoutBox LayoutItem(GridItem item, float fillW, float pctInline, LayoutBox containerBox)
    {
        // The item's border box fills the margin-adjusted area (fillW), but its
        // percentage padding resolves against the full grid area (pctInline).
        var childSpace = _space.InheritBuilder(fillW, float.PositiveInfinity)
            .SetIsFixedInlineSize(true)
            .SetIsNewFormattingContext(true)
            .SetPercentageResolution(pctInline, float.NaN)
            .ToConstraintSpace();
        var itemResult = new BlockLayoutAlgorithm(item.Element, childSpace).Layout();
        return AuroraFragmentConverter.ToLayoutBox(itemResult.Fragment, item.Element, containerBox);
    }

    private void PositionItems(List<GridItem> items, List<GridTrack> columns, List<GridTrack> rows, LayoutBox containerBox, float columnGap, float rowGap)
    {
        var containerStyle0 = _containerStyle!;

        // §12.6 auto track stretching first, so `align-content: stretch` (the
        // default) grows auto tracks instead of leaving them content-sized.
        StretchAutoTracks(columns, containerBox.ContentBox.Width, columnGap, 0f);
        StretchAutoTracks(rows, containerBox.ContentBox.Height, rowGap, 0f);

        float colFree = containerBox.ContentBox.Width - TrackGroupSize(columns, columnGap);
        float rowFree = containerBox.ContentBox.Height - TrackGroupSize(rows, rowGap);
        var (colExtraGap, colLeading) = ContentAlignment(containerStyle0.JustifyContent.ToString(), colFree, columns.Count);
        var (rowExtraGap, rowLeading) = ContentAlignment(containerStyle0.AlignContent, rowFree, rows.Count);

        // Compute column offsets
        var colOffsets = ComputeTrackOffsets(columns, containerBox.ContentBox.Left, columnGap, colExtraGap, colLeading);

        // Compute row offsets
        var rowOffsets = ComputeTrackOffsets(rows, containerBox.ContentBox.Top, rowGap, rowExtraGap, rowLeading);

        var containerStyle = _containerStyle!;
        var justifyItems = ParseJustifyItems(containerStyle.JustifyItems);
        var alignItems = ParseAlignItems(containerStyle.AlignItems.ToString());

        // Position each item
        // CSS Writing Modes 3 §4.1 / CSS Display 3 §3: with 'direction: rtl' the
        // columns run right-to-left. The track SIZING algorithm is unaffected — only
        // the placement is, so everything below stays in logical (inline-start-based)
        // coordinates and the whole track group is flipped once, when the item's
        // physical x is written. 'justify-content' therefore needs no special case:
        // packing tracks against the logical start edge *is* the right edge in RTL.
        bool rtl = _space.Direction == Acrux.Core.Layout.Geometry.TextDirection.Rtl;
        float contentLeft = containerBox.ContentBox.Left;
        float contentRight = containerBox.ContentBox.Right;
        foreach (var item in items)
        {
            int col = item.ColumnStart - 1;
            int row = item.RowStart - 1;
            int colEnd = Math.Min(item.ColumnEnd - 1, columns.Count);
            int rowEnd = Math.Min(item.RowEnd - 1, rows.Count);

            float logicalCellX = colOffsets[col];
            float cellY = rowOffsets[row];
            float cellW = GetTrackSpanSize(columns, col, colEnd, columnGap);
            float cellH = GetTrackSpanSize(rows, row, rowEnd, rowGap);
            float cellX = !rtl
                ? logicalCellX
                : contentRight - (logicalCellX - contentLeft) - cellW;

            // A grid item's margin-box occupies the grid area; the border box is the
            // area minus the margins and starts at the start margin. Percentage
            // margins resolve against the area (track-span) size (CSS Grid §6.5).
            var style = item.Element.ComputedStyle!;
            var itemMargins = LengthUtils.ComputeMargins(_space.WithPercentageResolution(cellW, cellH), style);
            float marginInline = itemMargins.Left + itemMargins.Right;
            float marginBlock = itemMargins.Top + itemMargins.Bottom;
            float availW = Math.Max(0, cellW - marginInline);
            float availH = Math.Max(0, cellH - marginBlock);

            // Reuse the box measured during row track sizing (same margin-adjusted
            // width, same child space), falling back to a fresh layout only if the
            // measure pass skipped this item. The box carries the real fragment
            // data (text runs, line boxes, nested children), so a grid item's
            // nested formatting contexts (flex/grid/table/replaced) are covered.
            var childBox = item.MeasuredBox ?? LayoutItem(item, availW, cellW, containerBox);

            // Apply alignment
            var justifySelf = ParseJustifySelf(style.JustifySelf ?? "auto", justifyItems);
            var alignSelf = ParseAlignSelfEnum(style.AlignSelf, alignItems);

            // An auto-sized item stretches to fill its cell (CSS grid default);
            // an item with a definite size keeps it and is aligned in the cell.
            bool stretchInline = justifySelf == JustifyItemsType.Stretch && (style.Width is AutoLength or null);
            bool stretchBlock = alignSelf == AlignItemsType.Stretch && (style.Height is AutoLength or null);

            // A non-stretching auto-width item sizes to its content (the measure
            // pass gave it the full cell width; shrink back to the natural inline
            // size so start/center/end alignment is visible).
            if (!stretchInline && style.Width is AutoLength or null)
                ShrinkBoxToNaturalInline(childBox);

            // The alignment below positions the item's BORDER box (TranslateBox sets
            // BorderBox.Left/Top = finalX/finalY), so the extent it is aligned by has
            // to be the border-box size too; using the content size under-counts an
            // item's own border+padding and pushes it past the track edge.
            float itemW = childBox.BorderBox.Width;
            float itemH = childBox.BorderBox.Height;

            float alignW = stretchInline ? availW : itemW;
            float alignH = stretchBlock ? availH : itemH;

            // 'margin-inline-start' is the RIGHT margin in RTL, and the alignment
            // offsets above are already logical (start = 0), so the item's distance
            // from its area's inline-start edge is marginStart + alignOffset and the
            // physical x is that distance measured from the mirrored edge.
            float marginStart = rtl ? itemMargins.Right : itemMargins.Left;
            float offsetFromStart = marginStart + GetAlignmentOffset(availW, alignW, justifySelf);
            float finalX = !rtl
                ? cellX + offsetFromStart
                : cellX + cellW - offsetFromStart - itemW;
            float finalY = cellY + itemMargins.Top + GetAlignmentOffset(availH, alignH, alignSelf);

            // Translate the child box and its subtree (lines, runs, children)
            // to the aligned position within the grid content box.
            TranslateBox(childBox, finalX - childBox.BorderBox.Left, finalY - childBox.BorderBox.Top);

            if (stretchInline || stretchBlock)
                ExpandBoxToCell(childBox,
                    stretchInline ? availW : childBox.BorderBox.Width,
                    stretchBlock ? availH : childBox.BorderBox.Height);

            childBox.Float = FloatType.None;

            // Add to container
            containerBox.Children.Add(childBox);
        }

        // Reflect the laid-out content height (row tracks + gaps) on the
        // container box so auto-height grids report a real content box instead
        // of the placeholder seeded for track unit resolution.
        float contentEnd = rows.Count > 0
            ? rowOffsets[rows.Count - 1] + rows[rows.Count - 1].BaseSize
            : containerBox.ContentBox.Top;
        containerBox.ContentBox = new SKRect(
            containerBox.ContentBox.Left,
            containerBox.ContentBox.Top,
            containerBox.ContentBox.Right,
            _containerHeightAuto ? Math.Max(containerBox.ContentBox.Top, contentEnd)
                                 : Math.Max(containerBox.ContentBox.Bottom, contentEnd));
    }

    /// <summary>
    /// Shift a converted layout box subtree by (dx, dy) without disturbing the
    /// offsets between lines/runs/children (they all move together).
    /// </summary>
    /// <summary>
    /// Shrink a measured item box from the cell width back to its natural
    /// (max line) inline extent plus its own border/padding, so non-stretch
    /// justify alignment positions a content-sized box.
    /// </summary>
    private static void ShrinkBoxToNaturalInline(Dom.LayoutBox box)
    {
        if (box.Lines == null || box.Lines.Count == 0) return;
        float natural = 0;
        foreach (var line in box.Lines)
            natural = Math.Max(natural, line.X + line.Width - box.ContentBox.Left);
        foreach (var child in box.Children)
            natural = Math.Max(natural, child.BorderBox.Right - box.ContentBox.Left);
        if (natural <= 0) return;

        float bpExtra = box.BorderBox.Width - box.ContentBox.Width;
        float target = Math.Min(box.BorderBox.Width, natural + bpExtra);
        float dw = box.BorderBox.Width - target;
        if (dw <= 0.5f) return;

        box.ContentBox = new SKRect(box.ContentBox.Left, box.ContentBox.Top, box.ContentBox.Right - dw, box.ContentBox.Bottom);
        box.PaddingBox = new SKRect(box.PaddingBox.Left, box.PaddingBox.Top, box.PaddingBox.Right - dw, box.PaddingBox.Bottom);
        box.BorderBox = new SKRect(box.BorderBox.Left, box.BorderBox.Top, box.BorderBox.Right - dw, box.BorderBox.Bottom);
        box.MarginBox = new SKRect(box.MarginBox.Left, box.MarginBox.Top, box.MarginBox.Right - dw, box.MarginBox.Bottom);
    }

    private static void TranslateBox(Dom.LayoutBox box, float dx, float dy)
    {
        if (dx == 0 && dy == 0) return;

        box.MarginBox = Offset(box.MarginBox, dx, dy);
        box.BorderBox = Offset(box.BorderBox, dx, dy);
        box.PaddingBox = Offset(box.PaddingBox, dx, dy);
        box.ContentBox = Offset(box.ContentBox, dx, dy);

        if (box.Lines != null)
        {
            foreach (var line in box.Lines)
            {
                line.X += dx;
                line.Y += dy;
                line.Baseline += dy;
                foreach (var run in line.Runs)
                {
                    run.X += dx;
                    // A run baseline of 0 is the "sit on the line box's baseline"
                    // sentinel; shifting it would turn it into an absolute offset.
                    if (run.Baseline != 0) run.Baseline += dy;
                }
            }
        }
        if (box.LineRuns != null)
        {
            foreach (var run in box.LineRuns)
            {
                run.X += dx;
                if (run.Baseline != 0) run.Baseline += dy;
            }
        }

        foreach (var child in box.Children)
            TranslateBox(child, dx, dy);
    }

    private static SKRect Offset(SKRect r, float dx, float dy) =>
        new(r.Left + dx, r.Top + dy, r.Right + dx, r.Bottom + dy);

    /// <summary>
    /// Grow a stretched item's box to fill its cell. Only expands (never shrinks
    /// below the laid-out content), so an item that overflows its row tracks
    /// keeps its content size.
    /// </summary>
    private static void ExpandBoxToCell(Dom.LayoutBox box, float borderWidth, float borderHeight)
    {
        float dw = borderWidth - box.BorderBox.Width;
        float dh = borderHeight - box.BorderBox.Height;
        if (dw > 0)
        {
            box.ContentBox = new SKRect(box.ContentBox.Left, box.ContentBox.Top, box.ContentBox.Right + dw, box.ContentBox.Bottom);
            box.PaddingBox = new SKRect(box.PaddingBox.Left, box.PaddingBox.Top, box.PaddingBox.Right + dw, box.PaddingBox.Bottom);
            box.BorderBox = new SKRect(box.BorderBox.Left, box.BorderBox.Top, box.BorderBox.Right + dw, box.BorderBox.Bottom);
            box.MarginBox = new SKRect(box.MarginBox.Left, box.MarginBox.Top, box.MarginBox.Right + dw, box.MarginBox.Bottom);
        }
        if (dh > 0)
        {
            box.ContentBox = new SKRect(box.ContentBox.Left, box.ContentBox.Top, box.ContentBox.Right, box.ContentBox.Bottom + dh);
            box.PaddingBox = new SKRect(box.PaddingBox.Left, box.PaddingBox.Top, box.PaddingBox.Right, box.PaddingBox.Bottom + dh);
            box.BorderBox = new SKRect(box.BorderBox.Left, box.BorderBox.Top, box.BorderBox.Right, box.BorderBox.Bottom + dh);
            box.MarginBox = new SKRect(box.MarginBox.Left, box.MarginBox.Top, box.MarginBox.Right, box.MarginBox.Bottom + dh);
        }
    }

    // The container style carries the DOM enum (FlexStart/FlexEnd); accept both
    // that spelling and the logical keywords.
    private static JustifyItemsType ParseJustifyItems(string value) => value.ToLowerInvariant() switch
    {
        "start" or "flexstart" or "flex-start" or "left" => JustifyItemsType.Start,
        "end" or "flexend" or "flex-end" or "right" => JustifyItemsType.End,
        "center" => JustifyItemsType.Center,
        "stretch" => JustifyItemsType.Stretch,
        _ => JustifyItemsType.Stretch
    };

    private static AlignItemsType ParseAlignItems(string value) => value.ToLowerInvariant() switch
    {
        "start" or "flexstart" or "flex-start" => AlignItemsType.Start,
        "end" or "flexend" or "flex-end" => AlignItemsType.End,
        "center" => AlignItemsType.Center,
        "stretch" => AlignItemsType.Stretch,
        "baseline" => AlignItemsType.Baseline,
        _ => AlignItemsType.Stretch
    };

    private static JustifyItemsType ParseJustifySelf(string value, JustifyItemsType parent) => value.ToLowerInvariant() switch
    {
        "auto" => parent,
        "start" or "flexstart" or "flex-start" => JustifyItemsType.Start,
        "end" or "flexend" or "flex-end" => JustifyItemsType.End,
        "center" => JustifyItemsType.Center,
        "stretch" => JustifyItemsType.Stretch,
        _ => parent
    };

    private static AlignItemsType ParseAlignSelfEnum(Dom.AlignSelfType value, AlignItemsType parent) => value switch
    {
        Dom.AlignSelfType.Auto => parent,
        Dom.AlignSelfType.FlexStart => AlignItemsType.Start,
        Dom.AlignSelfType.FlexEnd => AlignItemsType.End,
        Dom.AlignSelfType.Center => AlignItemsType.Center,
        Dom.AlignSelfType.Stretch => AlignItemsType.Stretch,
        Dom.AlignSelfType.Baseline => AlignItemsType.Baseline,
        _ => parent
    };

    private static float GetAlignmentOffset(float cellSize, float itemSize, JustifyItemsType alignment) => alignment switch
    {
        JustifyItemsType.Start => 0,
        JustifyItemsType.End => cellSize - itemSize,
        JustifyItemsType.Center => (cellSize - itemSize) / 2,
        JustifyItemsType.Stretch => 0, // stretch: item fills the cell
        _ => 0
    };

    private static float GetAlignmentOffset(float cellSize, float itemSize, AlignItemsType alignment) => alignment switch
    {
        AlignItemsType.Start => 0,
        AlignItemsType.End => cellSize - itemSize,
        AlignItemsType.Center => (cellSize - itemSize) / 2,
        AlignItemsType.Stretch => 0,
        AlignItemsType.Baseline => 0,
        _ => 0
    };

    private enum JustifyItemsType { Start, End, Center, Stretch }
    private enum AlignItemsType { Start, End, Center, Stretch, Baseline }
}

public class GridTrack
{
    public TrackSizeType SizeType { get; set; } = TrackSizeType.Auto;
    public float FixedSize { get; set; }
    public float Percentage { get; set; }
    public float Fraction { get; set; }
    public float BaseSize { get; set; }
    public float GrowLimit { get; set; } = float.MaxValue;
    public GridTrack? MinSize { get; set; }
    public GridTrack? MaxSize { get; set; }
    /// <summary>True for tracks produced by a repeat(auto-fit, …): when no item
    /// spans them they collapse to zero (CSS Grid §7.2.3), unlike auto-fill.</summary>
    public bool AutoFit { get; set; }
    /// <summary>Set during column sizing when an auto-fit track holds no item.</summary>
    public bool Collapsed { get; set; }

    public float ResolveSize(float containerSize, float fontSize, float viewportWidth, float viewportHeight) => SizeType switch
    {
        TrackSizeType.Fixed => FixedSize,
        TrackSizeType.Percentage => Percentage * containerSize,
        TrackSizeType.Fraction => 0,
        TrackSizeType.Auto => 0,
        TrackSizeType.MinContent => 0,
        TrackSizeType.MaxContent => 0,
        TrackSizeType.MinMax => ResolveMinMax(containerSize, fontSize, viewportWidth, viewportHeight),
        _ => 0
    };

    private float ResolveMinMax(float containerSize, float fontSize, float viewportWidth, float viewportHeight)
    {
        float min = MinSize?.ResolveSize(containerSize, fontSize, viewportWidth, viewportHeight) ?? 0;
        float max = MaxSize?.ResolveSize(containerSize, fontSize, viewportWidth, viewportHeight) ?? float.MaxValue;
        if (max == 0) max = float.MaxValue;
        return Math.Clamp(BaseSize, min, max);
    }

    public void Initialize(float containerSize, float fontSize, float viewportWidth, float viewportHeight)
    {
        if (SizeType == TrackSizeType.MinMax)
        {
            float min = MinSize?.ResolveSize(containerSize, fontSize, viewportWidth, viewportHeight) ?? 0;
            float max = MaxSize?.ResolveSize(containerSize, fontSize, viewportWidth, viewportHeight) ?? float.MaxValue;
            if (max == 0) max = float.MaxValue;
            BaseSize = min;
            GrowLimit = max;
        }
        else if (SizeType == TrackSizeType.Fraction)
        {
            BaseSize = 0;
            GrowLimit = float.MaxValue;
        }
        else
        {
            BaseSize = ResolveSize(containerSize, fontSize, viewportWidth, viewportHeight);
            GrowLimit = BaseSize;
        }
    }

    public GridTrack Clone() => new()
    {
        SizeType = SizeType, FixedSize = FixedSize, Percentage = Percentage,
        Fraction = Fraction, BaseSize = BaseSize, GrowLimit = GrowLimit,
        MinSize = MinSize, MaxSize = MaxSize, AutoFit = AutoFit
    };
}

public enum TrackSizeType { Fixed, Percentage, Fraction, Auto, MinContent, MaxContent, MinMax }

public class GridItem
{
    public Element Element { get; set; } = null!;
    public int ColumnStart { get; set; } = 1;
    public int ColumnEnd { get; set; } = 2;
    public int RowStart { get; set; } = 1;
    public int RowEnd { get; set; } = 2;
    public int ColumnSpan { get; set; } = 1;
    public int RowSpan { get; set; } = 1;
    public bool IsPlaced { get; set; }

    /// <summary>Box produced by the measure pass (layout at the resolved cell
    /// width); consumed by row track sizing and by PositionItems.</summary>
    public Dom.LayoutBox? MeasuredBox { get; set; }
}