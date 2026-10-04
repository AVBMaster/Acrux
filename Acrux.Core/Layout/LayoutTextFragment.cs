using Acrux.Core.Dom;
using Acrux.Core.Layout.Geometry;

namespace Acrux.Core.Layout;

public class LayoutTextFragment : LayoutText
{
    private uint _start;
    private uint _fragmentLength;
    private bool _isRemainingTextLayoutObject;
    private string _contentString = string.Empty;
    private FirstLetterPseudoElement? _firstLetterPseudoElement;

    public LayoutTextFragment(Node? node, string text, int startOffset, int length)
        : base(node, text)
    {
        _start = (uint)startOffset;
        _fragmentLength = (uint)length;
    }

    public static LayoutTextFragment? Create(Node? node, string text, int startOffset, int length) =>
        new(node, text, startOffset, length);

    public static LayoutTextFragment? CreateAnonymous(Document document, string text) =>
        new(null, text, 0, text.Length);

    public static LayoutTextFragment? CreateAnonymous(Document document, string text, uint start, uint length) =>
        new(null, text, (int)start, (int)length);

    public Position? PositionForCaretOffset(uint offset) => null;

    public uint? CaretOffsetForPosition(Position? position) => null;

    public uint Start => _start;
    public uint FragmentLength => _fragmentLength;

    public override uint TextStartOffset => Start;

    public void SetContentString(string s) => _contentString = s;
    public string ContentString => _contentString;

    public string CompleteText() => Text;

    public override string OriginalText() => Text;

    public void SetTextFragment(string text, uint start, uint length)
    {
        SetText(text);
        _start = start;
        _fragmentLength = length;
    }

    public override void TransformAndSecureOriginalText()
    {
    }

    public override string GetName() => "LayoutTextFragment";

    public void SetFirstLetterPseudoElement(FirstLetterPseudoElement? element) =>
        _firstLetterPseudoElement = element;

    public FirstLetterPseudoElement? GetFirstLetterPseudoElement() => _firstLetterPseudoElement;

    public void SetIsRemainingTextLayoutObject(bool isRemaining) =>
        _isRemainingTextLayoutObject = isRemaining;

    public bool IsRemainingTextLayoutObject => _isRemainingTextLayoutObject;

    public TextNode? AssociatedTextNode() => Node as TextNode;

    public override LayoutText? GetFirstLetterPart() => _firstLetterPseudoElement?.LayoutObject as LayoutText;

    public override string PlainText() => Text;

    public override void WillBeDestroyed()
    {
    }

    public override void InsertedIntoTree()
    {
        ValidNgItems = false;
        base.InsertedIntoTree();
    }

    private LayoutBlock? BlockForAccompanyingFirstLetter() => null;

    protected override char PreviousCharacter() => '\0';

    public override void TextDidChange()
    {
    }

    public override void UpdateHitTestResult(HitTestResult? result, PhysicalOffset offset)
    {
    }

    public override uint OwnerNodeId => 0;
}