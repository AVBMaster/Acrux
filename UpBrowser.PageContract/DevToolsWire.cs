using System.Text;

namespace UpBrowser.PageContract;

/// <summary>
/// Hand-rolled binary codec for the DevTools / find RPC payloads, shared verbatim by
/// the page host (writer) and the shell (reader) so the two ends can never drift.
/// Framing conventions match <c>TabFraming</c>: little-endian, strings as UTF-8 with an
/// int32 length prefix. RPC payloads carry their correlation id first; this codec does
/// not write or read that id — callers own it.
/// </summary>
public static class DevToolsWire
{
    /// <summary>Node-count cap per message (protocol budget).</summary>
    public const int MaxNodesPerBatch = 2000;

    /// <summary>Serialized-payload cap per nodes batch. Kept below the 256 KB wire
    /// budget so the codec's own framing and the correlation id always fit.</summary>
    public const int MaxBatchBytes = 240 * 1024;

    // ---- nodes ----

    public static void Write(BinaryWriter w, in DtNode node)
    {
        w.Write(node.Id);
        w.Write(node.ParentId);
        w.Write(node.Depth);
        w.Write((byte)node.NodeType);
        w.Write(node.HasElementChildren);
        w.Write(node.ChildCount);
        TabString(w, node.NodeName);
        TabString(w, node.LocalName);
        TabString(w, node.TextPreview);
        w.Write(node.Attributes.Count);
        foreach (var a in node.Attributes) TabString(w, a);
    }

    public static DtNode ReadNode(BinaryReader r)
    {
        var id = r.ReadInt32();
        var parentId = r.ReadInt32();
        var depth = r.ReadInt32();
        var type = r.ReadByte();
        var hasElementChildren = r.ReadBoolean();
        var childCount = r.ReadInt32();
        var nodeName = TabString(r);
        var localName = TabString(r);
        var preview = TabString(r);
        int n = r.ReadInt32();
        var attrs = new string[n];
        for (int i = 0; i < n; i++) attrs[i] = TabString(r);
        return new DtNode
        {
            Id = id,
            ParentId = parentId,
            Depth = depth,
            NodeType = type,
            HasElementChildren = hasElementChildren,
            ChildCount = childCount,
            NodeName = nodeName,
            LocalName = localName,
            TextPreview = preview,
            Attributes = attrs,
        };
    }

    public static void Write(BinaryWriter w, DtNodeBatch batch)
    {
        w.Write(batch.MutationVersion);
        w.Write(batch.Nodes.Count);
        foreach (var n in batch.Nodes) Write(w, n);
        w.Write(batch.Truncated);
        w.Write(batch.VersionMoved);
    }

    public static DtNodeBatch ReadNodeBatch(BinaryReader r)
    {
        long version = r.ReadInt64();
        int count = r.ReadInt32();
        var nodes = new DtNode[count];
        for (int i = 0; i < count; i++) nodes[i] = ReadNode(r);
        var truncated = r.ReadBoolean();
        var moved = r.ReadBoolean();
        return new DtNodeBatch(version, nodes, truncated, moved);
    }

    // ---- styles ----

    public static void Write(BinaryWriter w, DtStyleBatch? styles)
    {
        if (styles == null)
        {
            w.Write((byte)0);
            return;
        }
        w.Write((byte)1);
        w.Write(styles.NodeId);
        w.Write(styles.MutationVersion);
        w.Write(styles.VersionMoved);
        w.Write(styles.MatchedRules.Count);
        foreach (var rule in styles.MatchedRules)
        {
            TabString(w, rule.SelectorText);
            TabString(w, rule.SourceSheet);
            w.Write(rule.SourceLine);
            WriteDecls(w, rule.Declarations);
        }
        WriteDecls(w, styles.InlineStyle);
        WriteDecls(w, styles.ComputedStyle);
    }

    public static DtStyleBatch? ReadStyles(BinaryReader r)
    {
        if (r.ReadByte() == 0) return null;
        var nodeId = r.ReadInt32();
        var version = r.ReadInt64();
        var moved = r.ReadBoolean();
        int ruleCount = r.ReadInt32();
        var rules = new DtStyleRule[ruleCount];
        for (int i = 0; i < ruleCount; i++)
        {
            var selector = TabString(r);
            var sheet = TabString(r);
            var line = r.ReadInt32();
            var decls = ReadDecls(r);
            rules[i] = new DtStyleRule(selector, sheet, line, decls);
        }
        var inline = ReadDecls(r);
        var computed = ReadDecls(r);
        return new DtStyleBatch(nodeId, version, rules, inline, computed, moved);
    }

    private static void WriteDecls(BinaryWriter w, IReadOnlyList<DtDeclaration> decls)
    {
        w.Write(decls.Count);
        foreach (var d in decls)
        {
            TabString(w, d.Name);
            TabString(w, d.Value);
            w.Write(d.Important);
            w.Write(d.Overridden);
        }
    }

    private static IReadOnlyList<DtDeclaration> ReadDecls(BinaryReader r)
    {
        int n = r.ReadInt32();
        var decls = new DtDeclaration[n];
        for (int i = 0; i < n; i++)
        {
            var name = TabString(r);
            var value = TabString(r);
            var important = r.ReadBoolean();
            var overridden = r.ReadBoolean();
            decls[i] = new DtDeclaration(name, value, important, overridden);
        }
        return decls;
    }

    // ---- eval ----

    public static void Write(BinaryWriter w, EvalResult result)
    {
        w.Write(result.IsError);
        TabString(w, result.Text);
    }

    public static EvalResult ReadEval(BinaryReader r) => new(r.ReadBoolean(), TabString(r));

    // ---- find ----

    public static void Write(BinaryWriter w, FindResult result)
    {
        w.Write(result.TotalCount);
        w.Write(result.ActiveIndex);
        w.Write(result.Rects.Count);
        foreach (var rc in result.Rects)
        {
            w.Write(rc.X); w.Write(rc.Y); w.Write(rc.Width); w.Write(rc.Height);
        }
    }

    public static FindResult ReadFind(BinaryReader r)
    {
        int total = r.ReadInt32();
        int active = r.ReadInt32();
        int count = r.ReadInt32();
        var rects = new PageRect[count];
        for (int i = 0; i < count; i++)
            rects[i] = new PageRect(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
        return new FindResult { TotalCount = total, ActiveIndex = active, Rects = rects };
    }

    // ---- request payloads (shared so both ends agree on the field order) ----

    public static void WriteNodesRequest(BinaryWriter w, int rpcId, long knownVersion, int nodeId, int maxDepth, int limit)
    {
        w.Write(rpcId);
        w.Write(knownVersion);
        w.Write(nodeId);
        w.Write(maxDepth);
        w.Write(limit);
    }

    public static (int RpcId, long KnownVersion, int NodeId, int MaxDepth, int Limit) ReadNodesRequest(BinaryReader r)
    {
        int rpcId = r.ReadInt32();
        long knownVersion = r.ReadInt64();
        int nodeId = r.ReadInt32();
        int maxDepth = r.ReadInt32();
        int limit = r.ReadInt32();
        return (rpcId, knownVersion, nodeId, maxDepth, limit);
    }

    public static void WriteStylesRequest(BinaryWriter w, int rpcId, long knownVersion, int nodeId)
    {
        w.Write(rpcId);
        w.Write(knownVersion);
        w.Write(nodeId);
    }

    public static (int RpcId, long KnownVersion, int NodeId) ReadStylesRequest(BinaryReader r)
    {
        int rpcId = r.ReadInt32();
        long knownVersion = r.ReadInt64();
        int nodeId = r.ReadInt32();
        return (rpcId, knownVersion, nodeId);
    }

    public static void WriteEvalRequest(BinaryWriter w, int rpcId, string script)
    {
        w.Write(rpcId);
        TabString(w, script);
    }

    public static (int RpcId, string Script) ReadEvalRequest(BinaryReader r)
    {
        int rpcId = r.ReadInt32();
        return (rpcId, TabString(r));
    }

    public static void WriteFindRequest(BinaryWriter w, int rpcId, FindOptions options, int activateIndex)
    {
        w.Write(rpcId);
        TabString(w, options.Query);
        w.Write(options.CaseSensitive);
        w.Write(options.Forward);
        w.Write(activateIndex);
    }

    public static (int RpcId, FindOptions Options, int ActivateIndex) ReadFindRequest(BinaryReader r)
    {
        int rpcId = r.ReadInt32();
        var query = TabString(r);
        var cs = r.ReadBoolean();
        var forward = r.ReadBoolean();
        int activate = r.ReadInt32();
        return (rpcId, new FindOptions(query) { CaseSensitive = cs, Forward = forward }, activate);
    }

    // ---- string helpers (same encoding as the tab framing) ----

    private static void TabString(BinaryWriter w, string s)
    {
        var bytes = Encoding.UTF8.GetBytes(s ?? "");
        w.Write(bytes.Length);
        w.Write(bytes);
    }

    private static string TabString(BinaryReader r)
    {
        int len = r.ReadInt32();
        if (len <= 0) return "";
        return Encoding.UTF8.GetString(r.ReadBytes(len));
    }
}
