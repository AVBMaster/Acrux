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

    public string WholeText
    {
        get
        {
            var sb = new System.Text.StringBuilder();
            var current = this;
            while (current.PreviousSibling is TextNode prev)
                current = prev;
            while (current != null && current is TextNode tn)
            {
                sb.Append(tn.Data);
                current = current.NextSibling as TextNode;
            }
            return sb.ToString();
        }
    }

    public TextNode SplitText(int offset)
    {
        if (offset < 0 || offset > Data.Length)
            throw new DOMException("Index out of bounds", "IndexSizeError");

        var newData = Data[offset..];
        Data = Data[..offset];

        var newNode = new TextNode(newData);
        if (ParentNode != null)
            ParentNode.InsertBefore(newNode, NextSibling);
        return newNode;
    }

    public Element? AssignedSlot => null;

    protected override Node CloneNodeInternal(bool deep) => new TextNode(Data);
}
