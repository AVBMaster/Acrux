using UpBrowser.Core.Dom;
using UpBrowser.Core.Dom.Html;
using UpBrowser.Core.Layout.Geometry;

namespace UpBrowser.Core.Layout;

/// <summary>
/// Layout algorithm for replaced elements (img, video, canvas, etc.).
/// Computes the intrinsic size and sets up the fragment accordingly.
/// Mirrors ReplacedLayoutAlgorithm in replaced_layout_algorithm.cc.
/// </summary>
public class ReplacedLayoutAlgorithm : LayoutAlgorithm
{
    public ReplacedLayoutAlgorithm(Element node, in ConstraintSpace space) : base(node, space) { }

    public override LayoutResult Layout()
    {
        var border = LengthUtils.ComputeBorders(Style);
        var padding = LengthUtils.ComputePadding(Space, Style);
        var bp = new BoxStrut(border.Top + padding.Top, border.Right + padding.Right,
            border.Bottom + padding.Bottom, border.Left + padding.Left);

        Builder.BorderLeft = border.Left; Builder.BorderTop = border.Top;
        Builder.BorderRight = border.Right; Builder.BorderBottom = border.Bottom;
        Builder.PaddingLeft = padding.Left; Builder.PaddingTop = padding.Top;
        Builder.PaddingRight = padding.Right; Builder.PaddingBottom = padding.Bottom;
        Builder.Element = Node;

        // Compute intrinsic size using the element's intrinsic info.
        var intrinsic = GetIntrinsicSizingInfo(Node);
        float availInline = ChildAvailableInlineSize;
        float availBlock = ChildAvailableBlockSize;

        var concrete = LengthUtils.ComputeReplacedSize(intrinsic, Space, Style, bp, availInline, availBlock);

        Builder.InlineSize = concrete.Width + bp.HorizontalSum;
        Builder.BlockSize = concrete.Height + bp.VerticalSum;
        Builder.IntrinsicBlockSize = concrete.Height;

        // A replaced box has a resolved, non-zero block size, so it is never
        // self-collapsing and margins never collapse through it: where it sits in
        // the parent's block flow is fully decided at layout time by the
        // block-offset the parent estimated for it, plus the margin strut that is
        // committed to reach its border edge (the same value a block child derives
        // in FinishLayout()). Report it here, as the reference engine's
        // LayoutReplacedChild() does through builder->SetBfcBlockOffset().
        // Without a resolved offset the parent can only read the result as
        // "unpositioned": it never resolves its own BFC block-offset while laying
        // out children, so every replaced sibling is placed at block-offset 0 (they
        // overlap) and the container sizes to a single child's height.
        float bfcBlockOffset = Space.ForcedBfcBlockOffset
            ?? (Space.GetBfcOffset().BlockOffset + Space.MarginStrut.Sum);
        if (Space.HasClearanceOffset && bfcBlockOffset < Space.ClearanceOffset)
            bfcBlockOffset = Space.ClearanceOffset;

        Builder.BfcLineOffset = Space.GetBfcOffset().LineOffset;
        Builder.BfcBlockOffset = bfcBlockOffset;

        var frag = Builder.ToBoxFragment();
        var result = LayoutResult.FromFragment(frag);
        result.BfcLineOffset = Builder.BfcLineOffset;
        result.BfcBlockOffset = bfcBlockOffset;
        // Non-null: the child's position in its parent's block flow is resolved.
        result.BfcBlockOffsetValue = bfcBlockOffset;
        return result;
    }

    /// <summary>
    /// Get the intrinsic sizing info for a replaced element.
    /// For now, returns a best-effort estimate based on the element's natural
    /// dimensions or aspect ratio.
    /// </summary>
    private static IntrinsicSizingInfo GetIntrinsicSizingInfo(Element element)
    {
        var tag = element.TagName;

        if (tag == "IMG")
        {
            // Synthetic generated images (content: url()) are plain elements, so the
            // attribute/decoder lookup must not depend on the concrete DOM class.
            var size = element is HTMLImageElement img
                ? new PhysicalSize(img.NaturalWidth, img.NaturalHeight)
                : PhysicalSize.Zero;
            if (size.Width <= 0 || size.Height <= 0)
                size = ReplacedIntrinsicSizes.Lookup(element.GetAttribute("src")) ?? size;
            if (size.Width > 0 && size.Height > 0)
                return new IntrinsicSizingInfo(size, size, true, true);
            return new IntrinsicSizingInfo(PhysicalSize.Zero, size, false, false);
        }

        if (tag == "CANVAS")
        {
            float w = 300, h = 150;
            if (element.ComputedStyle?.Width is PixelLength pw) w = pw.Value;
            if (element.ComputedStyle?.Height is PixelLength ph) h = ph.Value;
            return new IntrinsicSizingInfo(new PhysicalSize(w, h), new PhysicalSize(w, h), true, true);
        }

        if (tag == "VIDEO")
        {
            return new IntrinsicSizingInfo(new PhysicalSize(300, 150), new PhysicalSize(300, 150), true, true);
        }

        if (tag == "BUTTON")
        {
            return new IntrinsicSizingInfo(new PhysicalSize(80, 22), new PhysicalSize(80, 22), false, false);
        }

        if (tag == "SELECT")
            return new IntrinsicSizingInfo(new PhysicalSize(120, 22), new PhysicalSize(120, 22), false, false);

        if (tag == "TEXTAREA")
        {
            int cols = 20, rows = 2;
            var colsStr = element.GetAttribute("cols");
            var rowsStr = element.GetAttribute("rows");
            if (int.TryParse(colsStr, out int c) && c > 0) cols = c;
            if (int.TryParse(rowsStr, out int r) && r > 0) rows = r;
            float fontSize = element.ComputedStyle?.FontSize ?? 16;
            return new IntrinsicSizingInfo(new PhysicalSize(cols * fontSize * 0.5f, rows * fontSize), new PhysicalSize(cols * fontSize * 0.5f, rows * fontSize), false, false);
        }

        if (tag == "INPUT" && element is HTMLInputElement input)
        {
            string t = input.Type?.ToLowerInvariant() ?? "text";
            if (t is "text" or "password" or "email" or "tel" or "url" or "search" or "number")
                return new IntrinsicSizingInfo(new PhysicalSize(150, 22), new PhysicalSize(150, 22), false, false);
            if (t is "checkbox" or "radio")
                return new IntrinsicSizingInfo(new PhysicalSize(13, 13), new PhysicalSize(13, 13), true, true);
            if (t == "range")
                return new IntrinsicSizingInfo(new PhysicalSize(150, 22), new PhysicalSize(150, 22), false, false);
            if (t == "color")
                return new IntrinsicSizingInfo(new PhysicalSize(40, 22), new PhysicalSize(40, 22), true, true);
            if (t is "date" or "time" or "datetime-local" or "month" or "week")
                return new IntrinsicSizingInfo(new PhysicalSize(160, 22), new PhysicalSize(160, 22), false, false);
            if (t == "file")
                return new IntrinsicSizingInfo(new PhysicalSize(240, 22), new PhysicalSize(240, 22), false, false);
        }

        return IntrinsicSizingInfo.None;
    }
}