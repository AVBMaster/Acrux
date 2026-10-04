using Acrux.Core.Dom;
using Acrux.Core.Layout.Geometry;
using Acrux.Core.Layout.Inline;

namespace Acrux.Core.Layout;

public class LayoutTextCombine : LayoutText
{
    private float? _scaleX;
    private Font? _compressedFont;
    private bool _hasCompressedFont;

    public LayoutTextCombine() : base(null, "")
    {
    }

    public float DesiredWidth() => 0;

    public string GetTextContent() => Text;

    public Font? CompressedFont() => _hasCompressedFont ? _compressedFont : null;

    public void SetCompressedFont(Font font)
    {
        _compressedFont = font;
        _hasCompressedFont = true;
    }

    public PhysicalOffset AdjustOffsetForHitTest(PhysicalOffset offsetInContainer) => offsetInContainer;

    public PhysicalOffset AdjustOffsetForLocalCaretRect(PhysicalOffset offsetInContainer) => offsetInContainer;

    public PhysicalRect AdjustRectForBoundingBox(PhysicalRect rect) => rect;

    public PhysicalRect ComputeTextBoundsRectForHitTest(FragmentItem? textItem, PhysicalOffset inlineRootOffset) => default;

    public PhysicalRect RecalcContentsInkOverflow(InlineCursor? cursor) => default;

    public void ResetLayout()
    {
        _scaleX = null;
        _hasCompressedFont = false;
        _compressedFont = null;
    }

    public void SetScaleX(float newScaleX) => _scaleX = newScaleX;

    public bool UsesScaleX() => _scaleX.HasValue;

    public float AdjustTextLeftForPaint(float textLeft) => textLeft;

    public float AdjustTextTopForPaint(float textTop) => textTop;

    public AffineTransform ComputeAffineTransformForPaint(PhysicalOffset paintOffset) => new();

    public bool NeedsAffineTransformInPaint() => false;

    public LineRelativeRect ComputeTextFrameRect(PhysicalOffset paintOffset) => default;

    public PhysicalRect VisualRectForPaint(PhysicalOffset paintOffset) => default;

    public static void AssertStyleIsValid(ComputedStyle? style)
    {
    }

    public static LayoutTextCombine? CreateAnonymous(LayoutText? textChild) => null;

    public static bool ShouldBeParentOf(LayoutObject layoutObject) => false;

    public override bool IsLayoutTextCombine => true;

    public override string GetName() => "LayoutTextCombine";

    private PhysicalOffset ApplyScaleX(PhysicalOffset offset) => offset;
    private PhysicalRect ApplyScaleX(PhysicalRect rect) => rect;
    private PhysicalSize ApplyScaleX(PhysicalSize offset) => offset;
    private PhysicalOffset UnapplyScaleX(PhysicalOffset offset) => offset;

    private float ComputeInlineSpacing() => 0;
    private bool UsingSyntheticOblique() => false;
}