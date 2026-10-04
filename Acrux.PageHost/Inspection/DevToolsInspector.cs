using Acrux.Core.Css;
using Acrux.Core.Css.Rules;
using Acrux.Core.Dom;
using Acrux.Core.Layout;
using Acrux.PageContract;

namespace Acrux.PageHost.Inspection;

/// <summary>
/// Preorder node cache over a live DOM tree — the single id space DevTools uses: a node
/// id is the node's preorder index under the <see cref="Version"/> it was minted at.
/// The cache is rebuilt only when <see cref="DomMutationTracker.Version"/> moves (or the
/// document is replaced) and only when a request actually needs it, so an untouched page
/// pays nothing and nobody polls. Chunk reads are then O(chunk): ids are contiguous in
/// preorder, so a continuation is simply the next index.
/// </summary>
public sealed class DevToolsTree
{
    private readonly List<Node> _nodes = new();
    private readonly List<int> _depth = new();
    private readonly List<int> _parentId = new();
    private readonly List<int> _subtreeEnd = new();

    public long Version { get; private set; } = long.MinValue;
    public Node? Root { get; private set; }
    public int Count => _nodes.Count;

    public bool IsCurrent(Node root, long currentVersion) =>
        Root == root && Version == currentVersion;

    public void Rebuild(Node root, long currentVersion)
    {
        Root = root;
        Version = currentVersion;
        _nodes.Clear();
        _depth.Clear();
        _parentId.Clear();
        _subtreeEnd.Clear();

        // Explicit preorder stack walk (deep documents must not blow the stack),
        // carrying each node's parent index so the reverse pass below is O(n).
        var stack = new Stack<(Node Node, int Depth, int Parent)>();
        stack.Push((root, 0, -1));
        while (stack.Count > 0)
        {
            var (node, depth, parent) = stack.Pop();
            int id = _nodes.Count;
            _nodes.Add(node);
            _depth.Add(depth);
            _parentId.Add(parent);
            _subtreeEnd.Add(id + 1);
            var kids = node.Children;
            for (int i = kids.Count - 1; i >= 0; i--)
                stack.Push((kids[i], depth + 1, id));
        }

        // Reverse pass: fold every node's range into its parent's. Children always
        // follow their parent in preorder, so this single pass yields exact ends.
        for (int i = _nodes.Count - 1; i > 0; i--)
        {
            int p = _parentId[i];
            if (_subtreeEnd[i] > _subtreeEnd[p]) _subtreeEnd[p] = _subtreeEnd[i];
        }
    }

    public Node? NodeAt(int id) => id >= 0 && id < _nodes.Count ? _nodes[id] : null;
    public int DepthAt(int id) => _depth[id];
    public int ParentIdAt(int id) => _parentId[id];
    public int SubtreeEndAt(int id) => _subtreeEnd[id];
}

/// <summary>
/// Turns live DOM state into the DTOs both DevTools transports serve. One builder in,
/// so an in-process page and a page-host child answer byte-identically — that identity
/// is what makes the out-of-process panel render exactly what the in-process one did.
/// </summary>
public static class DevToolsInspector
{
    private const int TextPreviewCap = 512;

    /// <summary>Current DOM mutation version — ids are valid only while this holds.</summary>
    public static long CurrentVersion => DomMutationTracker.Version;

    /// <summary>Serve the chunk of preorder nodes starting at <paramref name="startId"/>
    /// within the subtree of that node: at most <paramref name="limit"/> nodes (capped at
    /// <see cref="DevToolsWire.MaxNodesPerBatch"/>) and at most
    /// <see cref="DevToolsWire.MaxBatchBytes"/> estimated serialized bytes, restricted to
    /// <paramref name="maxDepth"/> levels below the start node (0/negative = unbounded).
    /// The tree must already be current (see <see cref="DevToolsTree.IsCurrent"/>).</summary>
    public static DtNodeBatch CollectChunk(DevToolsTree tree, long currentVersion,
        int startId, int maxDepth, int limit)
    {
        if (startId < 0 || startId >= tree.Count)
            return new DtNodeBatch(currentVersion, Array.Empty<DtNode>(), false, false);

        int end = tree.SubtreeEndAt(startId);
        int maxNodeDepth = maxDepth > 0 ? tree.DepthAt(startId) + maxDepth : int.MaxValue;
        int nodeLimit = Math.Clamp(limit <= 0 ? DevToolsWire.MaxNodesPerBatch : limit, 1, DevToolsWire.MaxNodesPerBatch);

        var nodes = new List<DtNode>(Math.Min(nodeLimit, 256));
        int bytes = 64; // version + count + flags + headroom for the id/framing
        int id = startId;
        while (id < end)
        {
            if (tree.DepthAt(id) > maxNodeDepth)
            {
                // Preorder: a deeper node's whole subtree is too — skip it wholesale.
                id = tree.SubtreeEndAt(id);
                continue;
            }
            var node = BuildNode(tree, id);
            int size = SerializedSize(node);
            if ((nodes.Count >= nodeLimit || bytes + size > DevToolsWire.MaxBatchBytes) && nodes.Count > 0)
                return new DtNodeBatch(currentVersion, nodes, true, false);
            bytes += size;
            nodes.Add(node);
            id++;
        }
        return new DtNodeBatch(currentVersion, nodes, false, false);
    }

    /// <summary>Approximates the codec's per-node byte cost — fixed fields plus UTF-8
    /// string bodies (this build's text is near-ASCII; the bound is an estimate used
    /// only to stop the chunk early, the writer itself is exact).</summary>
    private static int SerializedSize(DtNode node)
    {
        int size = 4 * 5 + 2; // id, parentId, depth, childCount + nodeType/flags bytes
        size += 4 + node.NodeName.Length + 4 + node.LocalName.Length + 4 + node.TextPreview.Length;
        size += 4;
        foreach (var a in node.Attributes) size += 4 + a.Length;
        return size;
    }

    public static DtNode BuildNode(DevToolsTree tree, int id)
    {
        var n = tree.NodeAt(id)!;

        string localName;
        string textPreview = "";
        IReadOnlyList<string> attrs = Array.Empty<string>();
        bool hasElementChildren = false;
        foreach (var c in n.Children)
            if (c is Element) { hasElementChildren = true; break; }

        switch (n)
        {
            case Element el:
                localName = string.IsNullOrEmpty(el.LocalName) ? el.TagName.ToLowerInvariant() : el.LocalName;
                if (el.Attributes.Count > 0)
                {
                    var list = new List<string>(el.Attributes.Count * 2);
                    foreach (var kv in el.Attributes)
                    {
                        list.Add(kv.Key);
                        list.Add(kv.Value ?? "");
                    }
                    attrs = list;
                }
                break;
            case TextNode tn:
                localName = "#text";
                textPreview = Trimmed(tn.TextContent);
                break;
            case CommentNode cn:
                localName = "#comment";
                textPreview = Trimmed(cn.TextContent);
                break;
            default:
                localName = n.NodeName;
                break;
        }

        return new DtNode
        {
            Id = id,
            ParentId = tree.ParentIdAt(id),
            Depth = tree.DepthAt(id),
            NodeType = (int)n.NodeType,
            NodeName = n.NodeName,
            LocalName = localName,
            Attributes = attrs,
            TextPreview = textPreview,
            ChildCount = n.Children.Count,
            HasElementChildren = hasElementChildren,
        };
    }

    private static string Trimmed(string? text)
    {
        var t = (text ?? "").Trim();
        return t.Length > TextPreviewCap ? t[..TextPreviewCap] : t;
    }

    // ==================== styles ====================

    /// <summary>Matched rules (UA sheet first, then author sheets in cascade order),
    /// the inline style attribute, and the computed subset the Elements panel renders.
    /// Returns null when <paramref name="nodeId"/> is not a known id.</summary>
    public static DtStyleBatch? BuildStyles(DevToolsTree tree, long currentVersion, int nodeId,
        StyleComputer? styles)
    {
        var node = tree.NodeAt(nodeId);
        if (node == null) return null;

        var matched = new List<DtStyleRule>();
        var inline = new List<DtDeclaration>();
        var computed = new List<DtDeclaration>();

        if (node is Element el)
        {
            var styleAttr = el.GetAttribute("style");
            if (!string.IsNullOrWhiteSpace(styleAttr))
            {
                foreach (var part in styleAttr.Split(';', StringSplitOptions.RemoveEmptyEntries))
                {
                    int colon = part.IndexOf(':');
                    if (colon <= 0) continue;
                    var name = part[..colon].Trim();
                    var value = part[(colon + 1)..].Trim();
                    bool important = value.EndsWith("!important", StringComparison.OrdinalIgnoreCase);
                    if (important) value = value[..^10].Trim();
                    inline.Add(new DtDeclaration(name, value, important, false));
                }
            }

            if (styles != null)
                CollectMatchedRules(el, styles, inline, matched);

            // The exact subset the Elements panel info bar renders — stringified the way
            // the old direct-object code did, so the drawn text cannot drift.
            var cs = el.ComputedStyle;
            if (cs != null)
            {
                computed.Add(new DtDeclaration("font-size", $"{cs.FontSize}", false, false));
                computed.Add(new DtDeclaration("color", $"#{cs.Color.Red:X2}{cs.Color.Green:X2}{cs.Color.Blue:X2}", false, false));
                computed.Add(new DtDeclaration("display", $"{cs.Display}", false, false));
                computed.Add(new DtDeclaration("position", $"{cs.Position}", false, false));
            }
        }

        return new DtStyleBatch(nodeId, currentVersion, matched, inline, computed, false);
    }

    private static void CollectMatchedRules(Element el, StyleComputer styles,
        IReadOnlyList<DtDeclaration> inline, List<DtStyleRule> into)
    {
        // (rule index, declaration index) winners per property name, then flag the rest
        // overridden. Within one rule the same property twice: last one wins. A matching
        // inline declaration overrides a non-important rule winner (author sheets lose to
        // the style attribute); an !important rule declaration keeps standing — except
        // against an !important inline, which wins like any later same-origin declaration.
        var entries = new List<(string Selector, string Sheet, List<(string Name, string Value, bool Important)> Decls)>();
        var winners = new Dictionary<string, (int Entry, int Decl)>(StringComparer.OrdinalIgnoreCase);

        void AddSheet(StyleSheetContents? sheet, string label)
        {
            if (sheet == null) return;
            foreach (var rule in sheet.GetStyleRules())
            {
                var selector = rule.OriginalSelectorText.Length > 0 ? rule.OriginalSelectorText : rule.SelectorText;
                if (string.IsNullOrWhiteSpace(selector)) continue;
                bool hit;
                try { hit = CssSelectorMatcher.Matches(selector, el); }
                catch { continue; }
                if (!hit) continue;

                var decls = new List<(string, string, bool)>();
                int entry = entries.Count;
                foreach (var p in rule.Properties.Properties)
                {
                    string name = p.Name.ToString().ToLowerInvariant();
                    string value = p.Value.CssText();
                    int d = decls.Count;
                    decls.Add((name, value, p.IsImportant));
                    if (!winners.TryGetValue(name, out var cur))
                        winners[name] = (entry, d);
                    else
                    {
                        var curDecl = entries[cur.Entry].Decls[cur.Decl];
                        if (p.IsImportant || !curDecl.Item3)
                            winners[name] = (entry, d);
                    }
                }
                entries.Add((selector, label, decls));
            }
        }

        AddSheet(styles.UaSheet, "user-agent stylesheet");
        foreach (var sheet in styles.AuthorSheets)
            AddSheet(sheet, sheet.OriginalUrl ?? "(document)");

        var inlineByName = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in inline) inlineByName[d.Name.ToLowerInvariant()] = d.Important;
        var beatenByInline = new HashSet<(int, int)>();
        foreach (var (name, win) in winners)
        {
            if (!inlineByName.TryGetValue(name, out bool inlineImportant)) continue;
            var declImportant = entries[win.Entry].Decls[win.Decl].Important;
            if (inlineImportant || !declImportant)
                beatenByInline.Add((win.Entry, win.Decl));
        }

        for (int entry = 0; entry < entries.Count; entry++)
        {
            var (selector, sheet, decls) = entries[entry];
            var finals = new List<DtDeclaration>(decls.Count);
            for (int d = 0; d < decls.Count; d++)
            {
                var (name, value, important) = decls[d];
                var win = winners[name];
                bool overridden = !(win.Entry == entry && win.Decl == d) || beatenByInline.Contains((entry, d));
                finals.Add(new DtDeclaration(name, value, important, overridden));
            }
            into.Add(new DtStyleRule(selector, sheet, 0, finals));
        }
    }

    // ==================== eval formatting ====================

    /// <summary>The console's result rendering, kept identical to the pre-IPC panel so
    /// both hosts echo the same text.</summary>
    public static string FormatEvalValue(object? result) => result switch
    {
        null => "undefined",
        string s => $"\"{s}\"",
        _ => result.ToString() ?? "undefined",
    };

    // ==================== find ====================

    /// <summary>All occurrences of <paramref name="query"/> in document text nodes,
    /// in DOM order, as (node, start, length) spans.</summary>
    public static List<(TextNode Node, int Start, int Length)> FindSpans(Document? document, string query, bool caseSensitive)
    {
        var spans = new List<(TextNode, int, int)>();
        if (document == null || string.IsNullOrEmpty(query)) return spans;
        var cmp = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var stack = new Stack<Node>();
        stack.Push(document);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            if (n is TextNode tn)
            {
                var text = tn.TextContent ?? "";
                int idx = 0;
                while ((idx = text.IndexOf(query, idx, cmp)) >= 0)
                {
                    spans.Add((tn, idx, query.Length));
                    idx += query.Length;
                }
            }
            var kids = n.Children;
            for (int i = kids.Count - 1; i >= 0; i--) stack.Push(kids[i]);
        }
        return spans;
    }

    /// <summary>Document-space rect (CSS px) of one (node, start, length) span, derived
    /// from the same line/run geometry the selection hit test walks, with per-character
    /// widths measured the way layout measured them — find coordinates and selection
    /// coordinates therefore cannot disagree. Unlaid-out text falls back to the parent
    /// element's box; hidden text yields a zero rect.</summary>
    public static PageRect SpanRect(TextNode node, int start, int length)
    {
        var text = node.TextContent ?? "";
        int end = Math.Min(start + length, text.Length);
        float left = float.MaxValue, right = float.MinValue, top = 0, bottom = 0;
        bool found = CollectSpanRect(node.ParentElement, node, start, end, ref left, ref right, ref top, ref bottom);
        if (found)
            return new PageRect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));

        var box = node.ParentElement?.LayoutBox;
        if (box == null) return new PageRect(0, 0, 0, 0);
        var r = box.ContentBox;
        return new PageRect(r.Left, r.Top, r.Width, r.Height);
    }

    private static bool CollectSpanRect(Element? el, TextNode node, int start, int end,
        ref float left, ref float right, ref float top, ref float bottom)
    {
        for (; el != null; el = el.ParentElement)
        {
            var box = el.LayoutBox;
            if (box == null) continue;
            if (CollectFromBox(box, node, start, end, ref left, ref right, ref top, ref bottom))
                return true;
        }
        return false;
    }

    private static bool CollectFromBox(LayoutBox box, TextNode node, int start, int end,
        ref float left, ref float right, ref float top, ref float bottom)
    {
        bool any = false;
        if (box.Lines is { Count: > 0 })
        {
            float boxLeft = box.ContentBox.Left;
            foreach (var line in box.Lines)
            {
                float runX = boxLeft + line.TextAlignOffsetX;
                TextNode? lastNode = null;
                int runCharOffset = 0;
                foreach (var run in line.Runs)
                {
                    if (!run.IsText || run.Node is not TextNode tn)
                    {
                        lastNode = null;
                        runCharOffset = 0;
                        runX += run.Width;
                        continue;
                    }
                    if (tn != lastNode)
                    {
                        runCharOffset = 0;
                        lastNode = tn;
                    }
                    int len = (run.Text ?? "").Length;
                    if (tn == node && runCharOffset < end && runCharOffset + len > start)
                    {
                        float x0 = RunX(runX, run, start - runCharOffset);
                        float x1 = RunX(runX, run, Math.Min(end - runCharOffset, len));
                        Accumulate(ref left, ref right, ref top, ref bottom, any,
                            Math.Min(x0, x1), Math.Max(x0, x1), line.Y, line.Y + line.Height);
                        any = true;
                    }
                    runCharOffset += len;
                    runX += run.Width;
                }
            }
        }
        if (!any && box.LineRuns is { Count: > 0 })
        {
            float runX = box.ContentBox.Left;
            float lineHeight = 0;
            foreach (var run in box.LineRuns) lineHeight = Math.Max(lineHeight, run.Height);
            if (lineHeight <= 0) lineHeight = box.ContentBox.Height;
            float lineTop = box.ContentBox.Top;
            TextNode? lastNode = null;
            int runCharOffset = 0;
            foreach (var run in box.LineRuns)
            {
                if (!run.IsText || run.Node is not TextNode tn)
                {
                    lastNode = null;
                    runCharOffset = 0;
                    runX += run.Width;
                    continue;
                }
                if (tn != lastNode)
                {
                    runCharOffset = 0;
                    lastNode = tn;
                }
                int len = (run.Text ?? "").Length;
                if (tn == node && runCharOffset < end && runCharOffset + len > start)
                {
                    float x0 = RunX(runX, run, start - runCharOffset);
                    float x1 = RunX(runX, run, Math.Min(end - runCharOffset, len));
                    Accumulate(ref left, ref right, ref top, ref bottom, any,
                        Math.Min(x0, x1), Math.Max(x0, x1), lineTop, lineTop + lineHeight);
                    any = true;
                }
                runCharOffset += len;
                runX += run.Width;
            }
        }
        return any;
    }

    private static void Accumulate(ref float left, ref float right, ref float top, ref float bottom,
        bool any, float l, float r, float t, float b)
    {
        if (!any)
        {
            left = l; right = r; top = t; bottom = b;
            return;
        }
        left = Math.Min(left, l);
        right = Math.Max(right, r);
        top = Math.Min(top, t);
        bottom = Math.Max(bottom, b);
    }

    /// <summary>x at character <paramref name="charOffset"/> inside a run, using the
    /// run's own font metrics — the same per-character measurement the hit test's
    /// <c>GetCharOffsetAtX</c> inverts.</summary>
    private static float RunX(float runStartX, InlineRun run, int charOffset)
    {
        string text = run.Text ?? "";
        if (charOffset <= 0 || string.IsNullOrEmpty(text)) return runStartX;
        charOffset = Math.Min(charOffset, text.Length);
        if (charOffset >= text.Length) return runStartX + run.Width;

        var measurer = TextMeasurer.Instance;
        if (measurer == null) return runStartX + run.Width * charOffset / Math.Max(1, text.Length);

        float fontSize = run.FontSize ?? 16f;
        string family = run.FontFamily ?? "Arial";
        var weight = run.FontWeight;
        float width = 0;
        for (int i = 0; i < charOffset; i++)
            width += measurer.MeasureText(text[i].ToString(), family, fontSize, weight);
        return runStartX + width;
    }
}
