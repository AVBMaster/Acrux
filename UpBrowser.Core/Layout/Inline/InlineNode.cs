using UpBrowser.Core.Dom;
using UpBrowser.Core.Layout.Geometry;

namespace UpBrowser.Core.Layout.Inline;

/// <summary>
/// Represents an inline node for layout. Mirrors inline_node.h.
/// </summary>
public class InlineNode
{
    public Element DOMNode { get; }
    public ComputedStyle Style { get; }
    public InlineItemsData ItemsData { get; } = new();

    public InlineNode(Element node, ComputedStyle style)
    {
        DOMNode = node;
        Style = style;
    }

    /// <summary>Back the node with externally-collected items (compat/test path).</summary>
    internal InlineNode(ComputedStyle style, InlineItemsData data)
    {
        DOMNode = new InlineSyntheticElement();
        Style = style;
        ItemsData = data;
    }

    private sealed class InlineSyntheticElement : Element
    {
        public InlineSyntheticElement() : base("span") { }
    }

    /// <summary>Collect the inline items from the DOM subtree.</summary>
    public void CollectInlineItems()
    {
        ItemsData.Items.Clear();
        ItemsData.TextContent = "";
        ItemsData.HasText = false;
        var builder = new InlineItemsBuilder(DOMNode, Style, ItemsData);
        builder.CollectInlines(int.MaxValue);
    }

    public void CollectInlineItems(int maxItems)
    {
        ItemsData.Items.Clear();
        var builder = new InlineItemsBuilder(DOMNode, Style, ItemsData);
        builder.CollectInlines(maxItems);
    }

    public bool IsBidiEnabled() => ItemsData.IsBidiEnabled;

    public bool IsScoreLineBreakDisabled() => true;

    public TextDirection BaseDirection() => ItemsData.BaseDirection;

    public bool CanContainFirstFormattedLine() => true;

    public bool UseFirstLineStyle() => false;

    public bool IsStickyImagesQuirkForContentSize() => false;

    public bool IsInitialLetterBox() => false;

    public bool IsSvgText() => false;

    public bool IsTextCombine() => false;

    public bool HasRuby() => ItemsData.Items.Any(i => i.Type is InlineItem.InlineItemType.OpenRubyColumn or InlineItem.InlineItemType.RubyLinePlaceholder);

    public bool ShouldWrapLineGreedy() => true;

    public bool IsListItem() => DOMNode.ComputedStyle?.Display == DisplayType.ListItem;

    public Document? GetDocument() => DOMNode.OwnerDocument;

    public LayoutBox? GetLayoutBox() => DOMNode.LayoutBox;

    public bool HasLineIfEmpty() => false;

    public void PrepareLayoutIfNeeded() { }

    public object? SvgCharacterDataList() => null;
    public object? SvgTextPathRangeList() => null;
    public object? SvgTextLengthRangeList() => null;

    public bool IsInlineFormattingContextRoot() => true;

    /// <summary>Compute base direction and bidi flag (CSS 2.1 §8.2, UAX#9 P2/P3).</summary>
    public void ComputeBidiFlags()
    {
        bool hasStrongRtl = false;
        foreach (char c in ItemsData.TextContent)
        {
            if (BidiParagraph.IsStrongRtl(c)) { hasStrongRtl = true; break; }
        }
        // The paragraph base direction comes from the computed 'direction',
        // not from a content heuristic.
        ItemsData.BaseDirection = string.Equals(Style.Direction, "rtl", StringComparison.OrdinalIgnoreCase)
            ? TextDirection.Rtl
            : TextDirection.Ltr;
        // Reordering matters when content fights the base (strong RTL inside
        // an LTR paragraph) and in every RTL paragraph, where even pure LTR
        // runs sit at level 2 and RTL text needs the whole-line reversal.
        ItemsData.IsBidiEnabled = hasStrongRtl || ItemsData.BaseDirection == TextDirection.Rtl;
    }
}