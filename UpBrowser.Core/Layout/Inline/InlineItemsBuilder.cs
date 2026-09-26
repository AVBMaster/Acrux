using UpBrowser.Core.Dom;
using System.Text;

namespace UpBrowser.Core.Layout.Inline;

/// <summary>
/// Collects InlineItems from a DOM subtree and produces the text content
/// string that item offsets index into. Mirrors inline_items_builder.cc
/// (whitespace collapsing, break opportunities, control items).
/// </summary>
public class InlineItemsBuilder
{
    private readonly Element _block;
    private readonly ComputedStyle _style;
    private readonly InlineItemsData _data;
    private readonly StringBuilder _text = new();
    private int _itemCountLimit = int.MaxValue;
    private int _bidiLevel;
    private bool _pendingCollapsibleSpace;
    private bool _hasContent;
    private bool _isAtEndOfParagraph;
    private int _startIndexText = -1; // start of the currently accumulated text run
    // Style of the text run currently being accumulated, and of a pending
    // collapsible space. Without these, FlushTextRun falls back to the block's
    // own style and every inline run (e.g. a <span> with its own font-size)
    // would be shaped at the wrong size.
    private ComputedStyle? _pendingRunStyle;
    private ComputedStyle? _pendingSpaceStyle;

    public InlineItemsBuilder(Element block, ComputedStyle style, InlineItemsData data)
    {
        _block = block;
        _style = style;
        _data = data;
    }

    public InlineItemsBuilder(InlineNode node, InlineItemsData data)
    {
        _block = node.DOMNode;
        _style = node.Style;
        _data = data;
    }

    public int Size() => _data.Items.Count;

    public void CollectInlines(int maxItems)
    {
        _itemCountLimit = maxItems;
        _text.Clear();
        _data.Items.Clear();
        bool atStartOfParagraph = true;
        CollectChildren(_block, _style, atStartOfParagraph);
        ExitBlock();
        _data.TextContent = _text.ToString();
        _data.HasText = _data.Items.Any(i => i.IsText || i.Type == InlineItem.InlineItemType.Control);
    }

    private void CollectChildren(Element parent, ComputedStyle inheritedStyle, bool atStartOfParagraph)
    {
        bool first = atStartOfParagraph;
        foreach (var child in parent.Children)
        {
            if (child is TextNode tn)
            {
                var style = parent.ComputedStyle ?? inheritedStyle;
                AppendText(tn.Data ?? "", style, parent,
                    new LayoutText(tn, tn.Data ?? ""), first);
            }
            else if (child is Element el)
            {
                first = CollectElement(el, inheritedStyle, first);
            }
        }
        _isAtEndOfParagraph = true;
    }

    /// <summary>Dispatch a child element to the appropriate item factory.</summary>
    private bool CollectElement(Element el, ComputedStyle inheritedStyle, bool atStartOfParagraph)
    {
        var style = el.ComputedStyle ?? inheritedStyle;
        if (style.Display == DisplayType.None)
            return atStartOfParagraph;

        if (el.TagName == "BR")
        {
            AppendControlItem('\n', TextItemType.kForcedLineBreak, el, isGenerated: true);
            return false;
        }
        if (el.TagName == "WBR")
        {
            AppendControlItem('\u200B', TextItemType.kFlowControl, el, isGenerated: true);
            return false;
        }

        if (style.Float != FloatType.None)
        {
            AppendFloating(el, style);
            return atStartOfParagraph;
        }
        if (style.Position is PositionType.Absolute or PositionType.Fixed)
        {
            AppendOutOfFlowPositioned(el, style);
            return atStartOfParagraph;
        }

        if (IsAtomicInlineElement(el, style))
        {
            AppendAtomicInline(el, style);
            return false;
        }

        bool isInline = style.Display is DisplayType.Inline or DisplayType.InlineBlock or DisplayType.Contents;
        if (isInline && el.Children.Count == 0)
        {
            // Empty inline: emit open/close tags so styles are tracked.
            AppendOpenTag(el, style);
            AppendCloseTag(el, style);
            return atStartOfParagraph;
        }
        if (isInline)
        {
            AppendOpenTag(el, style);
            bool first = atStartOfParagraph;
            foreach (var child in el.Children)
            {
                if (child is TextNode inner)
                    AppendText(inner.Data ?? "", el.ComputedStyle ?? style, el, new LayoutText(inner, inner.Data ?? ""), first);
                else if (child is Element childEl)
                    first = CollectElement(childEl, el.ComputedStyle ?? style, first);
            }
            // The element's text must precede its CloseTag item; a still-pending
            // run (element ending without trailing whitespace) would otherwise be
            // flushed after the tag and painted outside the popped box state.
            FlushTextRun();
            AppendCloseTag(el, style);
            return !atStartOfParagraph || HasContent();
        }

        // Block-level element in an inline context.
        FlushTextRun();
        AppendBlockInInline(el, style);
        return false;
    }

    public bool HasContent() => _hasContent;

    private static bool IsAtomicInlineElement(Element el, ComputedStyle style)
    {
        if (style.Display is DisplayType.InlineBlock or DisplayType.InlineFlex or DisplayType.InlineGrid)
            return true;
        return el.TagName is "IMG" or "CANVAS" or "INPUT" or "SELECT" or "TEXTAREA" or "IFRAME" or "EMBED" or "OBJECT" or "VIDEO" or "AUDIO";
    }

    // ------------------------------------------------------------------
    // Item factories.
    // ------------------------------------------------------------------

    private void AppendOpenTag(Element element, ComputedStyle style)
    {
        if (_data.Items.Count >= _itemCountLimit) return;
        // Text that came before this element must be styled by the previous box,
        // not by the element being opened: flush any pending run first so the
        // tag lands at the current end position.
        FlushTextRun();
        int offset = CurrentTextOffset();
        _data.Items.Add(new InlineItem(InlineItem.InlineItemType.OpenTag, offset, offset, element) { BidiLevel = _bidiLevel });
    }

    private void AppendCloseTag(Element element, ComputedStyle style)
    {
        if (_data.Items.Count >= _itemCountLimit) return;
        int offset = CurrentTextOffset();
        _data.Items.Add(new InlineItem(InlineItem.InlineItemType.CloseTag, offset, offset, element) { BidiLevel = _bidiLevel });
    }

    /// <summary>
    /// The next text position a tag or placeholder belongs at. If a text run is
    /// still pending, the tag must sit at the START of that run (the position
    /// right after the previously flushed run), not past the run's end — using
    /// _text.Length here would leave a gap the line breaker cannot walk across.
    /// </summary>
    private int CurrentTextOffset() => _startIndexText >= 0 ? _startIndexText : _text.Length;

    private void AppendFloating(Element element, ComputedStyle style)
    {
        FlushTextRun();
        int offset = _text.Length;
        _text.Append('\uFFFC');
        _data.Items.Add(new InlineItem(InlineItem.InlineItemType.Floating, offset, offset + 1, element)
        { BidiLevel = _bidiLevel, IsOpaqueToCollapsing = true });
        _hasContent = true;
    }

    private void AppendOutOfFlowPositioned(Element element, ComputedStyle style)
    {
        FlushTextRun();
        int offset = _text.Length;
        _text.Append('\uFFFC');
        _data.Items.Add(new InlineItem(InlineItem.InlineItemType.OutOfFlowPositioned, offset, offset + 1, element)
        { BidiLevel = _bidiLevel, IsOpaqueToCollapsing = true });
        _hasContent = true;
    }

    private void AppendBlockInInline(Element element, ComputedStyle style)
    {
        FlushTextRun();
        int offset = _text.Length;
        _data.Items.Add(new InlineItem(InlineItem.InlineItemType.BlockInInline, offset, offset, element)
        { BidiLevel = _bidiLevel, IsOpaqueToCollapsing = true });
        _hasContent = true;
    }

    private void AppendAtomicInline(Element element, ComputedStyle style)
    {
        FlushTextRun();
        int offset = _text.Length;
        _text.Append('\uFFFC');
        _data.Items.Add(new InlineItem(InlineItem.InlineItemType.AtomicInline, offset, offset + 1, element)
        { BidiLevel = _bidiLevel, IsOpaqueToCollapsing = true, StyleOverride = style });
        _hasContent = true;
    }

    private void AppendControlItem(char character, TextItemType textType, Element? element, bool isGenerated)
    {
        FlushTextRun();
        int offset = _text.Length;
        _text.Append(character);
        _data.Items.Add(new InlineItem(InlineItem.InlineItemType.Control, offset, offset + 1, element)
        {
            TextType = textType,
            BidiLevel = _bidiLevel,
            IsGeneratedForLineBreak = isGenerated,
            IsOpaqueToCollapsing = true,
        });
        _hasContent = true;
    }

    private void AppendBidiControlItem(char character, Element? element)
    {
        FlushTextRun();
        int offset = _text.Length;
        _text.Append(character);
        if (character == '\u2069' || character == '\u202C')
            _bidiLevel = Math.Max(0, _bidiLevel - 1);
        else
            _bidiLevel++;
        _data.Items.Add(new InlineItem(InlineItem.InlineItemType.BidiControl, offset, offset + 1, element)
        { BidiLevel = _bidiLevel, IsOpaqueToCollapsing = true });
    }

    // ------------------------------------------------------------------
    // Text processing: mirrors AppendText() / ProcessTextItem().
    // ------------------------------------------------------------------

    private static string ApplyTextTransform(string text, string? transform)
    {
        if (string.IsNullOrEmpty(transform) || transform == "none") return text;
        return transform.ToLowerInvariant() switch
        {
            "uppercase" => text.ToUpperInvariant(),
            "lowercase" => text.ToLowerInvariant(),
            "capitalize" => System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(text),
            _ => text,
        };
    }

    private void AppendText(string text, ComputedStyle style, Element? element, LayoutText layoutText, bool isFirstLine)
    {
        // text-transform must apply before shaping so measurement, breaking and
        // painting all see the transformed glyphs (matching browser behavior).
        if (!string.IsNullOrEmpty(text))
            text = ApplyTextTransform(text, style.TextTransform);

        if (string.IsNullOrEmpty(text))
        {
            if (_data.Items.Count < _itemCountLimit)
                _data.Items.Add(MakeTextItem("", 0, 0, style, layoutText));
            return;
        }

        int length = text.Length;
        if (length > _itemCountLimit - _data.Items.Count - 1)
            length = Math.Max(0, _itemCountLimit - _data.Items.Count - 1);
        if (length == 0)
        {
            _data.Items.Add(MakeTextItem("", 0, 0, style, layoutText));
            return;
        }

        bool shouldCollapse = WhiteSpaceStyle.ShouldCollapseWhiteSpaces(style);
        bool shouldPreserveNewline = WhiteSpaceStyle.ShouldPreserveNewline(style);
        bool preserve = !shouldCollapse;

        for (int i = 0; i < length; i++)
        {
            char c = text[i];
            switch (c)
            {
                case '\n':
                    if (shouldPreserveNewline)
                    {
                        AppendControlItem('\n', TextItemType.kForcedLineBreak, element, isGenerated: false);
                    }
                    else
                    {
                        HandleCollapsibleSegmentBreak(style, element, layoutText);
                    }
                    break;
                case '\r':
                case '\f':
                    // Carriage return and form feed are ignored (except CRLF handled here).
                    if (c == '\r' && i + 1 < length && text[i + 1] == '\n')
                        continue;
                    break;
                case '\t':
                    AppendControlItem('\t', TextItemType.kFlowControl, element, isGenerated: false);
                    break;
                case '\u200B':
                    AppendControlItem('\u200B', TextItemType.kFlowControl, element, isGenerated: false);
                    break;
                case '\u202A': case '\u202B': case '\u202D': case '\u202E':
                case '\u2066': case '\u2067': case '\u2068':
                case '\u2069': case '\u202C':
                    AppendBidiControlItem(c, element);
                    break;
                case ' ':
                case '\u00A0':
                    if (shouldCollapse && !(c == '\u00A0'))
                    {
                        if (_pendingCollapsibleSpace)
                            break;
                        FlushTextRun();
                        _pendingCollapsibleSpace = true;
                        _pendingSpaceStyle = style;
                    }
                    else
                    {
                        // Preserved space or NBSP: part of the text run. Commit any
                        // pending collapsible space first so it is not lost.
                        CommitPendingSpace();
                        AppendToRun(c, layoutText, style);
                    }
                    break;
                default:
                    // A collapsible space that preceded this content must be
                    // emitted, not discarded, or adjacent words / inline elements
                    // run together (e.g. "10 14 20" painting as "101420").
                    CommitPendingSpace();
                    AppendCharacterToRun(c, layoutText, style);
                    break;
            }
        }
    }

    private void AppendToRun(char c, LayoutText? layoutText, ComputedStyle? style = null)
    {
        if (_startIndexText < 0)
        {
            _startIndexText = _text.Length;
            // Capture the style at the moment this run begins; it determines how
            // the run is shaped when flushed.
            _pendingRunStyle = style;
            if (layoutText != null)
                _lastLayoutText = layoutText;
        }
        else if (layoutText != null && !ReferenceEquals(layoutText, _lastLayoutText))
        {
            // A new text node's characters merged into the pending run; keep the
            // newest node's layout object so the flushed run maps to the correct
            // TextNode when painted.
            _lastLayoutText = layoutText;
        }
        _text.Append(c);
        _hasContent = true;
    }

    private void AppendCharacterToRun(char c, LayoutText? layoutText, ComputedStyle? style = null) => AppendToRun(c, layoutText, style);

    /// <summary>A segment break (newline in collapsible mode) is itself a collapsible space.</summary>
    private void HandleCollapsibleSegmentBreak(ComputedStyle style, Element? element, LayoutText layoutText)
    {
        // Segment breaks collapse to a space (or nothing if adjacent to other
        // whitespace); model as a collapsible space.
        if (_pendingCollapsibleSpace)
            return;
        FlushTextRun();
        _pendingCollapsibleSpace = true;
        _pendingSpaceStyle = style;
    }

    /// <summary>
    /// Emit a pending collapsible space as a single space run before subsequent
    /// non-space content. Collapsing keeps exactly one space (CSS white-space
    /// processing); dropping it entirely would run adjacent words or inline
    /// elements together.
    /// </summary>
    private void CommitPendingSpace()
    {
        if (!_pendingCollapsibleSpace)
            return;
        int offset = _text.Length;
        _text.Append(' ');
        _data.Items.Add(MakeTextItem(" ", 1, offset, _pendingSpaceStyle ?? _style, _lastLayoutText ?? new LayoutText(null, " ")));
        _pendingCollapsibleSpace = false;
        _pendingSpaceStyle = null;
        _hasContent = true;
    }

    private void FlushTextRun()
    {
        if (_pendingCollapsibleSpace)
        {
            // Emit the pending collapsible space as its own trailing space run,
            // shaped with the style in effect where the space occurred.
            int offset = _text.Length;
            _text.Append(' ');
            _data.Items.Add(MakeTextItem(" ", 1, offset, _pendingSpaceStyle ?? _style, _lastLayoutText ?? new LayoutText(null, " ")));
            _pendingCollapsibleSpace = false;
            _pendingSpaceStyle = null;
            _hasContent = true;
        }
        if (_startIndexText >= 0)
        {
            int end = _text.Length;
            string slice = _text.ToString(_startIndexText, end - _startIndexText);
            // Use the style captured when this run started, not the block's style,
            // so an inline run keeps its own font.
            AddTextRunSlicingFirstLetter(slice, _startIndexText, _pendingRunStyle ?? _style,
                _lastLayoutText ?? new LayoutText(null, slice));
            _startIndexText = -1;
            _pendingRunStyle = null;
            _hasContent = true;
        }
    }

    private bool _firstLetterApplied;

    /// <summary>
    /// Emit a text run, splitting off the block's first letter so that
    /// ::first-letter participates in measurement (CSS Pseudo-Elements 4 §3):
    /// leading whitespace is skipped and the first remaining character carries the
    /// merged pseudo-element style.
    /// </summary>
    private void AddTextRunSlicingFirstLetter(string slice, int startOffset, ComputedStyle style, LayoutText layoutText)
    {
        if (slice.Length == 0 || _firstLetterApplied || _block.HasGeneratedFirstLetter
            || _block.FirstLetterStyles is not { Count: > 0 })
        {
            _data.Items.Add(MakeTextItem(slice, slice.Length, startOffset, style, layoutText));
            return;
        }

        int letterIndex = 0;
        while (letterIndex < slice.Length && char.IsWhiteSpace(slice[letterIndex]))
            letterIndex++;
        if (letterIndex >= slice.Length)
        {
            _data.Items.Add(MakeTextItem(slice, slice.Length, startOffset, style, layoutText));
            return;
        }

        _firstLetterApplied = true;
        var letterStyle = UpBrowser.Core.Css.PseudoStyleMerger.Merge(style, _block.FirstLetterStyles) ?? style;
        var node = layoutText.Node;

        if (letterIndex > 0)
            AddPlainRun(slice[..letterIndex], startOffset, style, node);
        AddPlainRun(slice[letterIndex..(letterIndex + 1)], startOffset + letterIndex, letterStyle, node);
        if (letterIndex + 1 < slice.Length)
            AddPlainRun(slice[(letterIndex + 1)..], startOffset + letterIndex + 1, style, node);
    }

    private void AddPlainRun(string text, int offset, ComputedStyle style, Node? node)
    {
        _data.Items.Add(MakeTextItem(text, text.Length, offset, style, new LayoutText(node, text)));
    }

    private ComputedStyle? _lastStyle;
    private LayoutText? _lastLayoutText;

    private InlineItem MakeTextItem(string slice, int length, int startOffset, ComputedStyle style, LayoutText layoutText)
    {
        _lastStyle = style;
        _lastLayoutText = layoutText;
        var textType = style.Display == DisplayType.ListItem && startOffset == 0 ? TextItemType.kSymbolMarker : TextItemType.kNormal;
        return new InlineItem(InlineItem.InlineItemType.Text, startOffset, startOffset + length,
            layoutText.Node as Element, slice, layoutText)
        {
            TextType = textType,
            BidiLevel = _bidiLevel,
            // A text item's DOM node is a TextNode, not an Element, so
            // Element?.ComputedStyle is null and Style() would fall back to a
            // default 16px ComputedStyle. That desyncs shaping (measures at 16px)
            // from painting (uses the inherited size), making glyphs overlap.
            // Carry the resolved inherited style explicitly.
            StyleOverride = style,
        };
    }

    /// <summary>Collapse trailing collapsible spaces at the end of the block.</summary>
    private void ExitBlock()
    {
        FlushTextRun();
    }

    public string GetText() => _text.ToString();

    public String GetTextUntrimmed() => _text.ToString();

    public bool IsAtEndOfParagraph() => _isAtEndOfParagraph;

    public IEnumerable<InlineItem> Items() => _data.Items;
}