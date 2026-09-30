using SkiaSharp;
using UpBrowser.PageContract;
using UpBrowser.Platform;

namespace UpBrowser.Rendering.DevTools;

/// <summary>
/// Elements panel over the <see cref="IDevToolsChannel"/> DTO stream. It never touches a
/// live DOM object: nodes arrive as preorder chunks and are flattened into exactly the
/// row list the old direct-object walk produced (same root "#tag" marker, same element
/// rows with ▼ markers/#id/.class, same "text child only when the parent has element
/// children" rule, same 18/16 px line heights), so the drawn panel is identical in
/// in-process and out-of-process modes. Large trees stream in lazily — one outstanding
/// chunk request at a time — and a moved mutation version voids the cache and restarts
/// the pull.
/// </summary>
public class DevToolsElements
{
    private enum RowKind { RootMarker, Element, Text, Status }

    private struct Row
    {
        public RowKind Kind;
        public int Depth;          // indent levels; RootMarker/Status draw at 0
        public string Marker;      // element: "▼ " or "  "
        public string Tag;         // element: lower-case tag; text: quoted preview; status: full line
        public string IdText;      // element: "#id" or ""
        public string ClassText;   // element: ".a.b" or ""
        public int Height;         // 18 for element/root/status rows, 16 for text rows
    }

    private IDevToolsChannel? _channel;
    /// <summary>Raised when an async batch changed the rows — the panel repaints off it.</summary>
    public Action? OnRowsChanged;
    private int _generation;               // invalidates callbacks after a channel swap
    private long _knownVersion = -1;       // version our cached rows were minted under
    private readonly List<Row> _rows = new();
    private readonly List<DtNode> _nodes = new();   // index == node id (preorder is dense)
    private int _rootId = -1;              // the "#tag" root element, once seen
    private int _nextId;                   // continuation: preorder index to request next
    private bool _fetching;
    private bool _streamDone;
    private bool _emptyDoc;

    private int _selectedId = -1;          // no selection UI exists today (old-panel parity)
    private DtStyleBatch? _selectedStyles;

    private float _scrollOffset;
    private float _contentHeight;
    private float _viewHeight;
    private float _renderX, _renderY, _renderW, _renderH;

    private SKPaint _font;
    private SKFont _skFont;

    public DevToolsElements()
    {
        try
        {
            Console.WriteLine("[DevToolsElements] Creating SKPaint...");
            _font = new SKPaint { IsAntialias = true };
            Console.WriteLine("[DevToolsElements] SKPaint OK");
            Console.WriteLine("[DevToolsElements] Creating SKFont...");
            _skFont = FontHelper.CreateDevToolsFont(12);
            Console.WriteLine("[DevToolsElements] SKFont OK");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DevToolsElements] SKPaint/SKFont FAILED: {ex.GetType().Name}: {ex.Message}");
            _font = null;
            _skFont = null;
        }
    }

    private bool _warnedUnavailable;
    private bool _thumbDragging;
    private float _thumbDragStartY;
    private float _thumbDragStartOffset;

    /// <summary>Swap the inspected page. Everything cached is dropped; the pull restarts
    /// lazily from the next render pass while the panel is visible.</summary>
    public void SetChannel(IDevToolsChannel? channel)
    {
        _channel = channel;
        _generation++;
        _rows.Clear();
        _nodes.Clear();
        _knownVersion = -1;
        _fetching = false;
        _streamDone = false;
        _emptyDoc = false;
        _rootId = -1;
        _nextId = 0;
        _selectedId = -1;
        _selectedStyles = null;
        _scrollOffset = 0;
    }

    public bool HandleWheel(double delta)
    {
        float maxScroll = Math.Max(0, _contentHeight - _viewHeight);
        _scrollOffset -= (float)delta * 3;
        _scrollOffset = Math.Max(0, Math.Min(_scrollOffset, maxScroll));
        return true;
    }

    public void SetScrollOffset(float offset)
    {
        float maxScroll = Math.Max(0, _contentHeight - _viewHeight);
        _scrollOffset = Math.Max(0, Math.Min(offset, maxScroll));
    }

    public bool HandleThumbDragStart(float y)
    {
        if (_contentHeight <= _viewHeight) return false;
        float sh = _viewHeight * _viewHeight / Math.Max(1, _contentHeight);
        float maxScroll = Math.Max(0, _contentHeight - _viewHeight);
        if (maxScroll <= 0) return false;
        float sy = _renderY + (maxScroll > 0 ? (_scrollOffset / maxScroll) * (_viewHeight - sh) : 0);
        if (y >= sy && y <= sy + sh)
        {
            _thumbDragging = true;
            _thumbDragStartY = y;
            _thumbDragStartOffset = _scrollOffset;
            return true;
        }
        return false;
    }

    public bool HandleThumbDrag(float y)
    {
        if (!_thumbDragging) return false;
        float sh = _viewHeight * _viewHeight / Math.Max(1, _contentHeight);
        float maxScroll = Math.Max(0, _contentHeight - _viewHeight);
        if (maxScroll <= 0) return false;
        float delta = (y - _thumbDragStartY) / Math.Max(1, _viewHeight - sh) * maxScroll;
        _scrollOffset = Math.Max(0, Math.Min(maxScroll, _thumbDragStartOffset + delta));
        return true;
    }

    public void HandleThumbDragEnd() { _thumbDragging = false; }

    // ==================== streaming ====================

    /// <summary>Kick the chunk pull while the tree is incomplete. Called from Render, so
    /// an idle or closed DevTools never puts a byte on the channel.</summary>
    private void PumpFetch()
    {
        var ch = _channel;
        if (ch == null || !ch.Supported || _fetching || _streamDone) return;
        _fetching = true;
        int gen = _generation;
        int startId = _nextId;
        ch.RequestChildren(startId, _knownVersion, 0, DevToolsWire.MaxNodesPerBatch, batch =>
        {
            if (gen != _generation || !ReferenceEquals(ch, _channel)) return; // stale swap
            OnBatch(batch);
        });
    }

    private void OnBatch(DtNodeBatch batch)
    {
        _fetching = false;
        if (batch.VersionMoved)
        {
            // The DOM version moved under our ids: the cache is void. Restart from the
            // document root at the live version.
            _rows.Clear();
            _nodes.Clear();
            _rootId = -1;
            _emptyDoc = false;
            _nextId = 0;
            _knownVersion = batch.MutationVersion;
            _streamDone = false;
            OnRowsChanged?.Invoke();
            PumpFetch();
            return;
        }
        _knownVersion = batch.MutationVersion;
        int before = _rows.Count;
        foreach (var node in batch.Nodes)
        {
            while (_nodes.Count <= node.Id) _nodes.Add(null!);   // dense preorder: pad only on gaps
            _nodes[node.Id] = node;
            Ingest(node);
        }
        if (batch.Nodes.Count > 0) _nextId = batch.Nodes[^1].Id + 1;
        _streamDone = !batch.Truncated;
        if (_rows.Count != before) OnRowsChanged?.Invoke();
        if (!_streamDone) PumpFetch();
    }

    private void Ingest(DtNode node)
    {
        if (node.Depth == 0)
        {
            // The document node itself is not a row — it decides the root like the old
            // walk did: "(empty document)" when it has no children at all.
            if (node.ChildCount == 0)
            {
                _emptyDoc = true;
                _rows.Add(new Row { Kind = RowKind.Status, Tag = "(empty document)", Height = 18 });
            }
            return;
        }
        if (_emptyDoc) return;

        // The first element child of the document is the root (old: DocumentElement ??
        // Body — for any parsed HTML document these are the same <html> node).
        if (_rootId < 0 && node.Depth == 1 && node.NodeType == 1 && node.ParentId == 0)
        {
            _rootId = node.Id;
            _rows.Add(new Row { Kind = RowKind.RootMarker, Tag = "#" + node.LocalName, Height = 18 });
        }

        switch (node.NodeType)
        {
            case 1: // element — the old RenderTree line: marker, tag, #id, .class
            {
                var (idText, classText) = IdClassFromAttributes(node);
                _rows.Add(new Row
                {
                    Kind = RowKind.Element,
                    Depth = node.Depth,
                    Marker = node.HasElementChildren ? "▼ " : "  ",
                    Tag = node.LocalName,
                    IdText = idText,
                    ClassText = classText,
                    Height = 18,
                });
                break;
            }
            case 3: // text — the old walk drew it only inside the parent's hasKids branch
            {
                if (node.TextPreview.Length == 0) break;   // whitespace-only or empty
                if (node.ParentId < 0 || node.ParentId >= _nodes.Count) break;
                var parent = _nodes[node.ParentId];
                if (parent == null || !parent.HasElementChildren) break;
                string t = node.TextPreview;
                if (t.Length > 80) t = t[..80] + "...";
                _rows.Add(new Row { Kind = RowKind.Text, Depth = node.Depth, Tag = "\"" + t + "\"", Height = 16 });
                break;
            }
            default:
                // comments / doctype / everything else: no rows, matching the old panel
                break;
        }
    }

    private static (string id, string cls) IdClassFromAttributes(DtNode node)
    {
        string id = "", cls = "";
        for (int i = 0; i + 1 < node.Attributes.Count; i += 2)
        {
            var name = node.Attributes[i];
            var value = node.Attributes[i + 1];
            if (name.Equals("id", StringComparison.OrdinalIgnoreCase)) id = value;
            else if (name.Equals("class", StringComparison.OrdinalIgnoreCase)) cls = value;
        }
        return (id.Length > 0 ? "#" + id : "", cls.Length > 0 ? "." + cls.Replace(' ', '.') : "");
    }

    // ==================== render ====================

    public void Render(SKCanvas canvas, float x, float y, float width, float height, DevToolsTheme theme)
    {
        if (_font == null || _skFont == null)
        {
            if (!_warnedUnavailable)
            {
                _warnedUnavailable = true;
                Console.WriteLine("[DevToolsElements.Render] SKPaint/SKFont unavailable on this OS, skipping render");
            }
            return;
        }

        _renderX = x; _renderY = y; _renderW = width; _renderH = height;
        _viewHeight = height;

        PumpFetch();

        using var bg = new SKPaint { Color = theme.PanelBg, Style = SKPaintStyle.Fill };
        canvas.DrawRect(x, y, width, height, bg);

        float dy = y + 16;

        canvas.Save();
        canvas.ClipRect(new SKRect(x, y, x + width, y + height));

        if (_channel == null || !_channel.Supported)
        {
            _font.Color = theme.TextSecondary;
            canvas.DrawText("(no document loaded)", x + 4, dy - _scrollOffset, SKTextAlign.Left, _skFont, _font);
            dy += 18;
        }

        foreach (var row in _rows)
        {
            float lineY = dy - _scrollOffset;
            if (lineY + row.Height >= _renderY && lineY <= _renderY + _renderH)
            {
                switch (row.Kind)
                {
                    case RowKind.RootMarker:
                        _font.Color = theme.AccentBlue;
                        canvas.DrawText(row.Tag, x + 4, lineY, SKTextAlign.Left, _skFont, _font);
                        break;

                    case RowKind.Element:
                    {
                        float lx = x + 4 + row.Depth * 16;
                        _font.Color = theme.TextSecondary;
                        canvas.DrawText(row.Marker, lx, lineY, SKTextAlign.Left, _skFont, _font);
                        float mw = _skFont.MeasureText(row.Marker);

                        _font.Color = theme.AccentBlue;
                        canvas.DrawText(row.Tag, lx + mw, lineY, SKTextAlign.Left, _skFont, _font);
                        float tw = _skFont.MeasureText(row.Tag);

                        float ax = lx + mw + tw;
                        if (row.IdText.Length > 0)
                        {
                            _font.Color = theme.AccentOrange;
                            canvas.DrawText(row.IdText, ax, lineY, SKTextAlign.Left, _skFont, _font);
                            ax += _skFont.MeasureText(row.IdText);
                        }
                        if (row.ClassText.Length > 0)
                        {
                            _font.Color = theme.AccentYellow;
                            canvas.DrawText(row.ClassText, ax, lineY, SKTextAlign.Left, _skFont, _font);
                        }
                        break;
                    }

                    case RowKind.Text:
                        _font.Color = theme.TextPrimary;
                        canvas.DrawText(row.Tag, x + 4 + row.Depth * 16, lineY, SKTextAlign.Left, _skFont, _font);
                        break;

                    case RowKind.Status:
                        _font.Color = theme.TextSecondary;
                        canvas.DrawText(row.Tag, x + 4, lineY, SKTextAlign.Left, _skFont, _font);
                        break;
                }
            }
            dy += row.Height;
        }

        _contentHeight = dy - y;
        canvas.Restore();

        float maxScroll = Math.Max(0, _contentHeight - height);
        _scrollOffset = Math.Max(0, Math.Min(_scrollOffset, maxScroll));

        if (_contentHeight > height)
        {
            float sh = height * height / Math.Max(1, _contentHeight);
            float sy = y + (maxScroll > 0 ? (_scrollOffset / maxScroll) * (height - sh) : 0);
            using var sp = new SKPaint { Color = theme.ScrollbarThumb, Style = SKPaintStyle.Fill };
            canvas.DrawRoundRect(x + width - 6, sy, 4, sh, 2, 2, sp);
        }

        if (_selectedId >= 0 && _selectedStyles != null)
        {
            float iy = y + height - 60;
            using var ibg = new SKPaint { Color = theme.InfoBg, Style = SKPaintStyle.Fill };
            canvas.DrawRect(x, iy, width, 60, ibg);

            using var iSep = new SKPaint { Color = theme.Separator, Style = SKPaintStyle.Stroke, StrokeWidth = 1 };
            canvas.DrawLine(x, iy, x + width, iy, iSep);

            using var ifont = FontHelper.CreateMonoPaint(11);
            using var ifontFont = FontHelper.CreateMonoFont(11);

            string selectedTag = SelectedTagName();
            ifont.Color = theme.AccentBlue;
            canvas.DrawText($"<{selectedTag}>", x + 8, iy + 16, SKTextAlign.Left, ifontFont, ifont);

            string fontSize = "", color = "", display = "", position = "";
            foreach (var d in _selectedStyles.ComputedStyle)
            {
                switch (d.Name)
                {
                    case "font-size": fontSize = d.Value; break;
                    case "color": color = d.Value; break;
                    case "display": display = d.Value; break;
                    case "position": position = d.Value; break;
                }
            }
            ifont.Color = theme.TextPrimary;
            canvas.DrawText($"font-size: {fontSize}  color: {color}", x + 8, iy + 32, SKTextAlign.Left, ifontFont, ifont);
            canvas.DrawText($"display: {display}  position: {position}", x + 8, iy + 48, SKTextAlign.Left, ifontFont, ifont);
        }
    }

    /// <summary>Ask the channel for the style batch of a node and show the info bar.
    /// Nothing selects a row today (the pre-IPC panel never set its selection either),
    /// so this is the DTO consumer waiting for a selection source.</summary>
    public void RequestStylesFor(int nodeId, string tagName)
    {
        var ch = _channel;
        if (ch == null || !ch.Supported) return;
        _selectedId = nodeId;
        _selectedTag = tagName;
        _selectedStyles = null;
        int gen = _generation;
        ch.RequestStyles(nodeId, _knownVersion, batch =>
        {
            if (gen != _generation || !ReferenceEquals(ch, _channel)) return;
            _selectedStyles = batch;
        });
    }

    private string _selectedTag = "";
    private string SelectedTagName() => _selectedTag;

    /// <summary>Serialized view of the rows the panel draws (cross-mode parity probe).</summary>
    internal string RowsSnapshotForTests()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var r in _rows)
            sb.Append(r.Kind).Append('|').Append(r.Depth).Append('|').Append(r.Marker).Append('|')
              .Append(r.Tag).Append('|').Append(r.IdText).Append('|').Append(r.ClassText).Append('\n');
        return sb.ToString();
    }
}
