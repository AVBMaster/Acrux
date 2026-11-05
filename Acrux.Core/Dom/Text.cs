namespace Acrux.Core.Dom;

public class TextNode : CharacterData
{
    public TextNode(string text) : base(text, "#text")
    {
    }

    public override NodeType NodeType => NodeType.Text;
    public override string NodeName => "#text";

    /// <summary>Collapsible whitespace only (CSS Text 3 §4.1.1): the HTML space characters
    /// tab, LF, form feed, CR and space. A node holding just those never opens a line box,
    /// which is what lets the whitespace between block tags disappear. U+00A0 and the other
    /// space separators are non-collapsible, so <c>&amp;nbsp;</c> on its own still makes a
    /// line — <see cref="string.IsNullOrWhiteSpace"/> counts it as whitespace and swallowed
    /// the whole line box.</summary>
    public bool IsWhitespaceOnly => IsCollapsibleWhitespace(Data);

    public static bool IsCollapsibleWhitespace(string? data)
    {
        if (string.IsNullOrEmpty(data)) return true;
        foreach (char c in data)
        {
            if (c is not (' ' or '\t' or '\n' or '\r' or '\f')) return false;
        }
        return true;
    }

    /// <summary>DOM §3.3.11: the whole text of a node is its own data and that of the Text nodes that
    /// <em>follow</em> it — the walk stops at the first sibling that is not text and never runs back
    /// towards the start, so the second of two adjacent nodes reads only its own part. A CDATA section
    /// anywhere in the walk answers null: the two spellings of the same characters are not mixed.</summary>
    public string? WholeText
    {
        get
        {
            var sb = new System.Text.StringBuilder();
            for (Node? node = this; node is TextNode text; node = node.NextSibling)
            {
                if (text.NodeType == NodeType.CDataSection) return null;
                sb.Append(text.Data);
            }
            return sb.ToString();
        }
    }

    /// <summary>DOM §3.3.10: the tail of the split is a new node of the <em>same interface</em> in the
    /// same document — a CDATA section splits into two CDATA sections — and it is only inserted where
    /// there is a parent to insert it into: a detached node still keeps its head and hands back its
    /// tail.</summary>
    public TextNode SplitText(int offset)
    {
        if (offset < 0 || offset > Data.Length)
            throw new DOMException("Index out of bounds", "IndexSizeError");

        var tail = CreateTail(Data[offset..]);
        Data = Data[..offset];
        ParentNode?.InsertBefore(tail, NextSibling);
        return tail;
    }

    private TextNode CreateTail(string data) => this switch
    {
        CDataSection => new CDataSection(data) { OwnerDocument = OwnerDocument },
        _ => new TextNode(data) { OwnerDocument = OwnerDocument },
    };

    public Element? AssignedSlot => null;

    protected override Node CloneNodeInternal(bool deep) => new TextNode(Data);
}
