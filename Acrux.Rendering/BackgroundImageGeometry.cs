using SkiaSharp;
using Acrux.Core.Dom;

namespace Acrux.Rendering;

/// <summary>
/// Transliteration of BackgroundImageGeometry: computes the destination rect,
/// tile size, phase and repeat spacing for one background-image layer.
/// </summary>
public sealed class BackgroundImageGeometry
{
    public SKRect UnsnappedDestRect;
    public SKRect SnappedDestRect;
    public SKSize TileSize;
    public SKPoint Phase;
    public SKSize SpaceSize;
    /// <summary>Resolved repeat per axis; no-repeat is not periodic, so its phase is used verbatim.</summary>
    public FillRepeat RepeatX = FillRepeat.Repeat;
    public FillRepeat RepeatY = FillRepeat.Repeat;

    public static BackgroundImageGeometry Empty => new();

    public bool HasClippedToPaintRect { get; private set; }

    /// <summary>Return the amount of space to leave between image tiles for repeat: space.</summary>
    private static float GetSpaceBetweenImageTiles(float areaSize, float tileSize)
    {
        int numberOfTiles = (int)(areaSize / tileSize);
        float space = -1;
        if (numberOfTiles > 1)
        {
            space = (areaSize - numberOfTiles * tileSize) / (numberOfTiles - 1);
        }
        return space;
    }

    private static float ComputeRoundedTileSize(float areaSize, float tileSize)
    {
        int nrTiles = Math.Max(1, (int)Math.Round(areaSize / tileSize));
        return areaSize / nrTiles;
    }

    private static float ComputeTilePhase(float position, float tileExtent)
    {
        if (tileExtent == 0)
            return 0;
        return tileExtent - IntMod(position, tileExtent);
    }

    private static float ResolveWidthForRatio(float height, SKSize naturalRatio)
    {
        float resolvedWidth = naturalRatio.Height != 0 ? height * naturalRatio.Width / naturalRatio.Height : 0;
        if (naturalRatio.Width >= 1 && resolvedWidth < 1)
            return 1;
        return resolvedWidth;
    }

    private static float ResolveHeightForRatio(float width, SKSize naturalRatio)
    {
        float resolvedHeight = naturalRatio.Width != 0 ? width * naturalRatio.Height / naturalRatio.Width : 0;
        if (naturalRatio.Height >= 1 && resolvedHeight < 1)
            return 1;
        return resolvedHeight;
    }

    private static float ResolveXPosition(FillLayer fillLayer, float availableWidth, float offset)
    {
        float edgeRelativePosition = MinimumValueForLength(fillLayer.PositionX, availableWidth);
        float absolutePosition = fillLayer.XOrigin == BackgroundEdgeOrigin.Right
            ? availableWidth - edgeRelativePosition
            : edgeRelativePosition;
        return absolutePosition - offset;
    }

    private static float ResolveYPosition(FillLayer fillLayer, float availableHeight, float offset)
    {
        float edgeRelativePosition = MinimumValueForLength(fillLayer.PositionY, availableHeight);
        float absolutePosition = fillLayer.YOrigin == BackgroundEdgeOrigin.Bottom
            ? availableHeight - edgeRelativePosition
            : edgeRelativePosition;
        return absolutePosition - offset;
    }

    private static float MinimumValueForLength(Length? length, float available)
    {
        if (length == null || length is AutoLength)
            return 0;
        if (length is PixelLength px)
            return px.Value;
        if (length is PercentLength pct)
            return pct.Value * available;
        float value = length.ToPixels(available, 16, 0, 0);
        if (float.IsNaN(value))
            return 0;
        return Math.Max(0, value);
    }

    private static float IntMod(float a, float b)
    {
        if (b == 0)
            return 0;
        float r = a % b;
        if (r < 0)
            r += b;
        return r;
    }

    private void SetPhaseX(float x) => Phase = new SKPoint(x, Phase.Y);
    private void SetPhaseY(float y) => Phase = new SKPoint(Phase.X, y);
    private void SetSpaceSize(SKSize s) => SpaceSize = s;

    private void SetNoRepeatX(FillLayer fillLayer, float xOffset, float snappedXOffset)
    {
        if (xOffset > 0)
        {
            // Move the dest rect if the offset is positive. The image "stays"
            // where it is over the dest rect, so this effectively modifies the
            // phase.
            UnsnappedDestRect = new SKRect(UnsnappedDestRect.Left + xOffset, UnsnappedDestRect.Top, UnsnappedDestRect.Right + xOffset, UnsnappedDestRect.Bottom);
            SnappedDestRect = new SKRect((float)Math.Round(UnsnappedDestRect.Left), SnappedDestRect.Top, SnappedDestRect.Right, SnappedDestRect.Bottom);

            // Make the dest as wide as a tile, which will reduce the dest rect
            // if the tile is too small to fill the paint rect.
            UnsnappedDestRect = new SKRect(UnsnappedDestRect.Left, UnsnappedDestRect.Top, UnsnappedDestRect.Left + TileSize.Width, UnsnappedDestRect.Bottom);
            SnappedDestRect = new SKRect(SnappedDestRect.Left, SnappedDestRect.Top, SnappedDestRect.Left + TileSize.Width, SnappedDestRect.Bottom);

            SetPhaseX(0);
        }
        else
        {
            // Negative offset: the image starts outside the destination rect, so
            // keep the raw (negative) shift — the tiler slides the tile left and
            // crops what falls outside.
            SetPhaseX(xOffset);

            // Reduce the width of the dest rect to draw only the portion of the
            // tile that remains visible after offsetting the image.
            UnsnappedDestRect = new SKRect(UnsnappedDestRect.Left, UnsnappedDestRect.Top, UnsnappedDestRect.Left + TileSize.Width + xOffset, UnsnappedDestRect.Bottom);
            SnappedDestRect = new SKRect(SnappedDestRect.Left, SnappedDestRect.Top, SnappedDestRect.Left + TileSize.Width + snappedXOffset, SnappedDestRect.Bottom);
        }

        // Force the horizontal space to zero, retaining vertical.
        SetSpaceSize(new SKSize(0, SpaceSize.Height));
    }

    private void SetNoRepeatY(FillLayer fillLayer, float yOffset, float snappedYOffset)
    {
        if (yOffset > 0)
        {
            UnsnappedDestRect = new SKRect(UnsnappedDestRect.Left, UnsnappedDestRect.Top + yOffset, UnsnappedDestRect.Right, UnsnappedDestRect.Bottom + yOffset);
            SnappedDestRect = new SKRect(SnappedDestRect.Left, (float)Math.Round(UnsnappedDestRect.Top), SnappedDestRect.Right, SnappedDestRect.Bottom);

            UnsnappedDestRect = new SKRect(UnsnappedDestRect.Left, UnsnappedDestRect.Top, UnsnappedDestRect.Right, UnsnappedDestRect.Top + TileSize.Height);
            SnappedDestRect = new SKRect(SnappedDestRect.Left, SnappedDestRect.Top, SnappedDestRect.Right, SnappedDestRect.Top + TileSize.Height);

            SetPhaseY(0);
        }
        else
        {
            SetPhaseY(yOffset);
            UnsnappedDestRect = new SKRect(UnsnappedDestRect.Left, UnsnappedDestRect.Top, UnsnappedDestRect.Right, UnsnappedDestRect.Top + TileSize.Height + yOffset);
            SnappedDestRect = new SKRect(SnappedDestRect.Left, SnappedDestRect.Top, SnappedDestRect.Right, SnappedDestRect.Top + TileSize.Height + snappedYOffset);
        }

        SetSpaceSize(new SKSize(SpaceSize.Width, 0));
    }

    private void SetRepeatX(float xOffset)
    {
        // All values are unsnapped to accurately set phase in the presence of
        // zoom and large values.
        SetPhaseX(ComputeTilePhase(xOffset, TileSize.Width));
        SetSpaceSize(new SKSize(0, SpaceSize.Height));
    }

    private void SetRepeatY(float yOffset)
    {
        SetPhaseY(ComputeTilePhase(yOffset, TileSize.Height));
        SetSpaceSize(new SKSize(SpaceSize.Width, 0));
    }

    private void SetSpaceX(float space, float extraOffset)
    {
        SetSpaceSize(new SKSize(space, SpaceSize.Height));
        // Modify the phase to start a full tile at the edge of the paint area.
        SetPhaseX(ComputeTilePhase(extraOffset, TileSize.Width + space));
    }

    private void SetSpaceY(float space, float extraOffset)
    {
        SetSpaceSize(new SKSize(SpaceSize.Width, space));
        SetPhaseY(ComputeTilePhase(extraOffset, TileSize.Height + space));
    }

    /// <summary>
    /// Main entry point: compute dest rect, tile size, phase and spacing for a
    /// background-image layer. <paramref name="intrinsicSize"/> is the natural
    /// (intrinsic) size of the image; when null the image is treated as a
    /// generated image without intrinsic dimensions.
    /// </summary>
    public void Calculate(FillLayer fillLayer, BoxBackgroundPaintContext paintContext, SKRect paintRect, ComputedStyle style, SKSize? intrinsicSize = null, bool isMask = false)
    {
        SKRect unsnappedPositioningArea;
        SKRect snappedPositioningArea;
        SKPoint unsnappedBoxOffset;
        SKPoint snappedBoxOffset;

        if (paintContext.ShouldUseFixedAttachment(fillLayer))
        {
            unsnappedPositioningArea = paintContext.FixedAttachmentPositioningArea();
            UnsnappedDestRect = SnappedDestRect = snappedPositioningArea = unsnappedPositioningArea;
            unsnappedBoxOffset = new SKPoint(0, 0);
            snappedBoxOffset = new SKPoint(0, 0);
        }
        else
        {
            unsnappedPositioningArea = paintContext.NormalPositioningArea(paintRect);
            UnsnappedDestRect = paintRect;
            snappedPositioningArea = unsnappedPositioningArea;
            unsnappedBoxOffset = new SKPoint(0, 0);
            snappedBoxOffset = new SKPoint(0, 0);
            AdjustPositioningArea(fillLayer, paintContext, unsnappedPositioningArea, ref snappedPositioningArea, out unsnappedBoxOffset, out snappedBoxOffset);
        }

        // Sets the tile size.
        CalculateFillTileSize(fillLayer, style, new SKSize(unsnappedPositioningArea.Width, unsnappedPositioningArea.Height),
            new SKSize(snappedPositioningArea.Width, snappedPositioningArea.Height), intrinsicSize);

        // Applies *-repeat and *-position.
        SKPoint offsetInBackground = paintContext.OffsetInBackground(fillLayer);
        CalculateRepeatAndPosition(fillLayer, offsetInBackground,
            new SKSize(unsnappedPositioningArea.Width, unsnappedPositioningArea.Height),
            new SKSize(snappedPositioningArea.Width, snappedPositioningArea.Height),
            unsnappedBoxOffset, snappedBoxOffset);

        if (paintContext.ShouldUseFixedAttachment(fillLayer))
        {
            SKPoint fixedAdjustment = new(paintRect.Left - UnsnappedDestRect.Left, paintRect.Top - UnsnappedDestRect.Top);
            fixedAdjustment.X = Math.Max(0, fixedAdjustment.X);
            fixedAdjustment.Y = Math.Max(0, fixedAdjustment.Y);
            Phase = new SKPoint(Phase.X + fixedAdjustment.X, Phase.Y + fixedAdjustment.Y);
        }

        // The actual painting area can be bigger than the provided background
        // geometry for mask-clip: no-clip, so avoid clipping.
        if (fillLayer.Clip != FillBox.NoClip)
        {
            UnsnappedDestRect = Intersect(UnsnappedDestRect, paintRect);
            SnappedDestRect = Intersect(SnappedDestRect, paintRect);
            HasClippedToPaintRect = true;
        }
        // Re-snap the dest rect as we may have adjusted it with unsnapped values.
        SnappedDestRect = new SKRect(
            (float)Math.Round(SnappedDestRect.Left), (float)Math.Round(SnappedDestRect.Top),
            (float)Math.Round(SnappedDestRect.Right), (float)Math.Round(SnappedDestRect.Bottom));
    }

    private static SKRect Intersect(SKRect a, SKRect b)
    {
        return new SKRect(
            Math.Max(a.Left, b.Left), Math.Max(a.Top, b.Top),
            Math.Min(a.Right, b.Right), Math.Min(a.Bottom, b.Bottom));
    }

    private void AdjustPositioningArea(FillLayer fillLayer, BoxBackgroundPaintContext paintContext, SKRect unsnappedPositioningArea,
        ref SKRect snappedPositioningArea, out SKPoint unsnappedBoxOffset, out SKPoint snappedBoxOffset)
    {
        bool disallowBorderDerivedAdjustment = paintContext.DisallowBorderDerivedAdjustment;

        var destAdjust = ComputeDestRectAdjustments(fillLayer, paintContext, unsnappedPositioningArea, disallowBorderDerivedAdjustment);
        var boxOutset = ComputePositioningAreaAdjustments(fillLayer, paintContext, disallowBorderDerivedAdjustment);

        // Offset of the positioning area from the corner of positioning_box_.
        unsnappedBoxOffset = boxOutset.Unsnapped.Offset() - destAdjust.Unsnapped.Offset();
        snappedBoxOffset = boxOutset.Snapped.Offset() - destAdjust.Snapped.Offset();

        // Apply the adjustments.
        SnappedDestRect = Contract(UnsnappedDestRect, destAdjust.Snapped);
        SnappedDestRect = new SKRect(
            (float)Math.Round(SnappedDestRect.Left), (float)Math.Round(SnappedDestRect.Top),
            (float)Math.Round(SnappedDestRect.Right), (float)Math.Round(SnappedDestRect.Bottom));
        ClampNegativeToZero(ref SnappedDestRect);
        UnsnappedDestRect = Contract(UnsnappedDestRect, destAdjust.Unsnapped);
        ClampNegativeToZero(ref UnsnappedDestRect);
        snappedPositioningArea = Contract(unsnappedPositioningArea, boxOutset.Snapped);
        snappedPositioningArea = new SKRect(
            (float)Math.Round(snappedPositioningArea.Left), (float)Math.Round(snappedPositioningArea.Top),
            (float)Math.Round(snappedPositioningArea.Right), (float)Math.Round(snappedPositioningArea.Bottom));
        ClampNegativeToZero(ref snappedPositioningArea);
        unsnappedPositioningArea = Contract(unsnappedPositioningArea, boxOutset.Unsnapped);
        ClampNegativeToZero(ref unsnappedPositioningArea);
    }

    private static SKRect Contract(SKRect r, PhysicalBoxStrut o)
    {
        return new SKRect(r.Left + o.Left, r.Top + o.Top, r.Right - o.Right, r.Bottom - o.Bottom);
    }

    private static void ClampNegativeToZero(ref SKRect r)
    {
        if (r.Width < 0)
            r = new SKRect(r.Left, r.Top, r.Left, r.Bottom);
        if (r.Height < 0)
            r = new SKRect(r.Left, r.Top, r.Right, r.Top);
    }

    private SnappedAndUnsnappedOutsets ComputeDestRectAdjustments(FillLayer fillLayer, BoxBackgroundPaintContext paintContext,
        SKRect unsnappedPositioningArea, bool disallowBorderDerivedAdjustment)
    {
        SnappedAndUnsnappedOutsets destAdjust;
        switch (paintContext.EffectiveClip(fillLayer))
        {
            case FillBox.NoClip:
                destAdjust = SnappedAndUnsnappedOutsets.From(VisualOverflowOutsets());
                break;
            case FillBox.FillBox:
            case FillBox.Content:
                destAdjust = SnappedAndUnsnappedOutsets.From(paintContext.PaddingOutsets);
                if (!destAdjust.Unsnapped.IsZero)
                {
                    destAdjust.Unsnapped += paintContext.BorderOutsets;
                    destAdjust.Snapped = destAdjust.Unsnapped;
                    break;
                }
                goto case FillBox.Padding;
            case FillBox.Padding:
                destAdjust.Unsnapped = paintContext.BorderOutsets;
                if (disallowBorderDerivedAdjustment)
                {
                    destAdjust.Snapped = destAdjust.Unsnapped;
                }
                else
                {
                    // Force the snapped dest rect to match the inner border to
                    // avoid gaps between the background and border.
                    destAdjust.Snapped = paintContext.InnerBorderOutsets(UnsnappedDestRect, unsnappedPositioningArea);
                }
                break;
            case FillBox.StrokeBox:
            case FillBox.ViewBox:
            case FillBox.Border:
                if (disallowBorderDerivedAdjustment)
                {
                    // All adjustments remain 0.
                    destAdjust = default;
                    break;
                }
                destAdjust = paintContext.ObscuredBorderOutsets(UnsnappedDestRect, unsnappedPositioningArea);
                break;
            case FillBox.Text:
                destAdjust = default;
                break;
            default:
                destAdjust = default;
                break;
        }
        return destAdjust;
    }

    private SnappedAndUnsnappedOutsets ComputePositioningAreaAdjustments(FillLayer fillLayer, BoxBackgroundPaintContext paintContext,
        bool disallowBorderDerivedAdjustment)
    {
        SnappedAndUnsnappedOutsets boxOutset;
        // The painters hand in the padding box, which already *is* the default
        // positioning area (CSS Backgrounds 3 4.7), so 'padding' needs no adjustment.
        // Contracting it by the border again shrank the area by twice the border width
        // and made 'background-repeat: space' drop its last tile.
        switch (fillLayer.Origin)
        {
            case FillBoxOrigin.Padding:
                boxOutset = default;
                break;
            case FillBoxOrigin.Border:
            {
                var border = paintContext.BorderOutsets;
                boxOutset = SnappedAndUnsnappedOutsets.From(
                    new PhysicalBoxStrut(-border.Top, -border.Right, -border.Bottom, -border.Left));
                break;
            }
            case FillBoxOrigin.Content:
                // The positioning area is the content box: inset by the padding.
                boxOutset = SnappedAndUnsnappedOutsets.From(paintContext.PaddingOutsets);
                break;
            default:
                boxOutset = default;
                break;
        }
        return boxOutset;
    }

    private PhysicalBoxStrut VisualOverflowOutsets()
    {
        // No overflow tracking; return zero outsets.
        return new PhysicalBoxStrut();
    }

    private void CalculateFillTileSize(FillLayer fillLayer, ComputedStyle style, SKSize unsnappedPositioningAreaSize, SKSize snappedPositioningAreaSize, SKSize? intrinsicSize)
    {
        var type = fillLayer.SizeType;

        // Intrinsic sizing info. Images without intrinsic dimensions (generated
        // content like gradients) are sized against the snapped positioning
        // area; images with intrinsic dimensions use the unsnapped area.
        SKSize imageAspectRatio = SKSize.Empty;
        bool hasIntrinsicSize = false;
        if (intrinsicSize.HasValue && intrinsicSize.Value.Width > 0 && intrinsicSize.Value.Height > 0)
        {
            imageAspectRatio = intrinsicSize.Value;
            hasIntrinsicSize = true;
        }
        SKSize positioningAreaSize = !hasIntrinsicSize ? snappedPositioningAreaSize : unsnappedPositioningAreaSize;

        switch (type)
        {
            case FillSizeType.Length:
            {
                TileSize = positioningAreaSize;

                float layerWidth = ValueForLength(fillLayer.SizeWidth, positioningAreaSize.Width);
                float layerHeight = ValueForLength(fillLayer.SizeHeight, positioningAreaSize.Height);

                if (!IsAuto(fillLayer.SizeWidth))
                    TileSize = new SKSize(layerWidth, TileSize.Height);
                if (!IsAuto(fillLayer.SizeHeight))
                    TileSize = new SKSize(TileSize.Width, layerHeight);

                // An auto value for one dimension is resolved by using the
                // image's natural aspect ratio and the size of the other
                // dimension, or failing that the natural size, or failing that
                // the positioning area size.
                if (IsAuto(fillLayer.SizeWidth) && !IsAuto(fillLayer.SizeHeight))
                {
                    if (!imageAspectRatio.IsEmpty)
                        TileSize = new SKSize(ResolveWidthForRatio(TileSize.Height, imageAspectRatio), TileSize.Height);
                    else if (hasIntrinsicSize)
                        TileSize = new SKSize(intrinsicSize!.Value.Width, TileSize.Height);
                    else
                        TileSize = new SKSize(positioningAreaSize.Width, TileSize.Height);
                }
                else if (!IsAuto(fillLayer.SizeWidth) && IsAuto(fillLayer.SizeHeight))
                {
                    if (!imageAspectRatio.IsEmpty)
                        TileSize = new SKSize(TileSize.Width, ResolveHeightForRatio(TileSize.Width, imageAspectRatio));
                    else if (hasIntrinsicSize)
                        TileSize = new SKSize(TileSize.Width, intrinsicSize!.Value.Height);
                    else
                        TileSize = new SKSize(TileSize.Width, positioningAreaSize.Height);
                }
                else if (IsAuto(fillLayer.SizeWidth) && IsAuto(fillLayer.SizeHeight))
                {
                    TileSize = hasIntrinsicSize ? intrinsicSize!.Value : positioningAreaSize;
                }
                ClampNegativeToZero(TileSize);
                return;
            }
            case FillSizeType.Contain:
            case FillSizeType.Cover:
            {
                if (imageAspectRatio.IsEmpty)
                {
                    TileSize = snappedPositioningAreaSize;
                    return;
                }
                // Always use the snapped positioning area size for this
                // computation, so the image resizes to completely fill the
                // actual painted area.
                TileSize = FitToAspectRatio(snappedPositioningAreaSize, imageAspectRatio, type == FillSizeType.Cover);
                // Snap the dependent dimension to avoid bleeding/blending
                // artifacts at the edge of the image.
                if (type == FillSizeType.Contain)
                {
                    if (TileSize.Width != snappedPositioningAreaSize.Width)
                        TileSize = new SKSize(Math.Max(1, (float)Math.Round(TileSize.Width)), TileSize.Height);
                    if (TileSize.Height != snappedPositioningAreaSize.Height)
                        TileSize = new SKSize(TileSize.Width, Math.Max(1, (float)Math.Round(TileSize.Height)));
                }
                else
                {
                    if (TileSize.Width != snappedPositioningAreaSize.Width)
                        TileSize = new SKSize(Math.Max(1, TileSize.Width), TileSize.Height);
                    if (TileSize.Height != snappedPositioningAreaSize.Height)
                        TileSize = new SKSize(TileSize.Width, Math.Max(1, TileSize.Height));
                }
                return;
            }
            case FillSizeType.Auto:
            {
                TileSize = hasIntrinsicSize ? intrinsicSize!.Value : positioningAreaSize;
                ClampNegativeToZero(TileSize);
                return;
            }
            case FillSizeType.SizeNone:
                TileSize = positioningAreaSize;
                return;
        }
    }

    /// <summary>Fit <paramref name="area"/> into <paramref name="ratio"/>, growing (cover) or shrinking (contain).</summary>
    private static SKSize FitToAspectRatio(SKSize area, SKSize ratio, bool grow)
    {
        if (ratio.Width <= 0 || ratio.Height <= 0)
            return area;
        // area is "wider" than ratio when area.w/area.h > ratio.w/ratio.h.
        bool areaWider = area.Width * ratio.Height > area.Height * ratio.Width;
        if (grow)
        {
            // Cover: the constrained dimension must be the same as the wider
            // dimension so the scaled size covers the whole area.
            return areaWider
                ? new SKSize(area.Width, area.Width * ratio.Height / ratio.Width)
                : new SKSize(area.Height * ratio.Width / ratio.Height, area.Height);
        }
        // Contain: fit inside — constrain by the opposite dimension.
        return areaWider
            ? new SKSize(area.Height * ratio.Width / ratio.Height, area.Height)
            : new SKSize(area.Width, area.Width * ratio.Height / ratio.Width);
    }

    private static void ClampNegativeToZero(SKSize s)
    {
    }

    private static bool IsAuto(Length? length) => length == null || length is AutoLength;

    private static float ValueForLength(Length? length, float available)
    {
        if (length == null || length is AutoLength)
            return available;
        if (length is PixelLength px)
            return px.Value;
        if (length is PercentLength pct)
            return pct.Value * available;
        float value = length.ToPixels(available, 16, 0, 0);
        if (float.IsNaN(value))
            return available;
        return value;
    }

    private void CalculateRepeatAndPosition(FillLayer fillLayer, SKPoint offsetInBackground,
        SKSize unsnappedPositioningAreaSize, SKSize snappedPositioningAreaSize, SKPoint unsnappedBoxOffset, SKPoint snappedBoxOffset)
    {
        var backgroundRepeatX = fillLayer.RepeatX;
        var backgroundRepeatY = fillLayer.RepeatY;

        // Maintain both snapped and unsnapped available widths and heights.
        float unsnappedAvailableWidth = unsnappedPositioningAreaSize.Width - TileSize.Width;
        float unsnappedAvailableHeight = unsnappedPositioningAreaSize.Height - TileSize.Height;
        float snappedAvailableWidth = snappedPositioningAreaSize.Width - TileSize.Width;
        float snappedAvailableHeight = snappedPositioningAreaSize.Height - TileSize.Height;

        if (backgroundRepeatX == FillRepeat.Round && snappedPositioningAreaSize.Width > 0 && TileSize.Width > 0)
        {
            float roundedWidth = ComputeRoundedTileSize(snappedPositioningAreaSize.Width, TileSize.Width);
            if (IsAuto(fillLayer.SizeHeight) && backgroundRepeatY != FillRepeat.Round)
            {
                TileSize = new SKSize(TileSize.Width, ResolveHeightForRatio(roundedWidth, TileSize));
            }
            TileSize = new SKSize(roundedWidth, TileSize.Height);

            // Force the first tile to line up with the edge of the positioning area.
            float xOffset = ResolveXPosition(fillLayer, snappedAvailableWidth, offsetInBackground.X);
            SetPhaseX(ComputeTilePhase(xOffset + unsnappedBoxOffset.X, TileSize.Width));
            SetSpaceSize(new SKSize(0, 0));
        }

        if (backgroundRepeatY == FillRepeat.Round && snappedPositioningAreaSize.Height > 0 && TileSize.Height > 0)
        {
            float roundedHeight = ComputeRoundedTileSize(snappedPositioningAreaSize.Height, TileSize.Height);
            if (IsAuto(fillLayer.SizeWidth) && backgroundRepeatX != FillRepeat.Round)
            {
                TileSize = new SKSize(ResolveWidthForRatio(roundedHeight, TileSize), TileSize.Height);
            }
            TileSize = new SKSize(TileSize.Width, roundedHeight);

            float yOffset = ResolveYPosition(fillLayer, snappedAvailableHeight, offsetInBackground.Y);
            SetPhaseY(ComputeTilePhase(yOffset + unsnappedBoxOffset.Y, TileSize.Height));
            SetSpaceSize(new SKSize(0, 0));
        }

        if (backgroundRepeatX == FillRepeat.Repeat)
        {
            float xOffset = ResolveXPosition(fillLayer, unsnappedAvailableWidth, offsetInBackground.X);
            SetRepeatX(unsnappedBoxOffset.X + xOffset);
        }
        else if (backgroundRepeatX == FillRepeat.Space && TileSize.Width > 0)
        {
            float space = GetSpaceBetweenImageTiles(snappedPositioningAreaSize.Width, TileSize.Width);
            if (space >= 0)
                SetSpaceX(space, snappedBoxOffset.X);
            else
                backgroundRepeatX = FillRepeat.NoRepeat;
        }
        if (backgroundRepeatX == FillRepeat.NoRepeat)
        {
            float xOffset = ResolveXPosition(fillLayer, unsnappedAvailableWidth, offsetInBackground.X);
            float snappedXOffset = ResolveXPosition(fillLayer, snappedAvailableWidth, offsetInBackground.X);
            SetNoRepeatX(fillLayer, unsnappedBoxOffset.X + xOffset, snappedBoxOffset.X + snappedXOffset);
        }

        if (backgroundRepeatY == FillRepeat.Repeat)
        {
            float yOffset = ResolveYPosition(fillLayer, unsnappedAvailableHeight, offsetInBackground.Y);
            SetRepeatY(unsnappedBoxOffset.Y + yOffset);
        }
        else if (backgroundRepeatY == FillRepeat.Space && TileSize.Height > 0)
        {
            float space = GetSpaceBetweenImageTiles(snappedPositioningAreaSize.Height, TileSize.Height);
            if (space >= 0)
                SetSpaceY(space, snappedBoxOffset.Y);
            else
                backgroundRepeatY = FillRepeat.NoRepeat;
        }
        if (backgroundRepeatY == FillRepeat.NoRepeat)
        {
            float yOffset = ResolveYPosition(fillLayer, unsnappedAvailableHeight, offsetInBackground.Y);
            float snappedYOffset = ResolveYPosition(fillLayer, snappedAvailableHeight, offsetInBackground.Y);
            SetNoRepeatY(fillLayer, unsnappedBoxOffset.Y + yOffset, snappedBoxOffset.Y + snappedYOffset);
        }

        RepeatX = backgroundRepeatX;
        RepeatY = backgroundRepeatY;
    }

    /// <summary>
    /// Compute the phase to paint, no more than one size + space in magnitude.
    /// A no-repeat axis is not periodic, so its raw (possibly negative) shift is
    /// kept to slide the first tile out under the destination rect.
    /// </summary>
    public SKPoint ComputePhase()
    {
        float stepX = TileSize.Width + SpaceSize.Width;
        float stepY = TileSize.Height + SpaceSize.Height;
        return new SKPoint(PhaseForAxis(Phase.X, stepX, RepeatX), PhaseForAxis(Phase.Y, stepY, RepeatY));
    }

    private static float PhaseForAxis(float phase, float step, FillRepeat repeat)
    {
        if (repeat == FillRepeat.NoRepeat)
            return Math.Min(phase, 0f);
        return step > 0 ? IntMod(-phase, step) : 0;
    }

    /// <summary>Build a FillLayer chain from a ComputedStyle.</summary>
public static FillLayer? FromStyle(ComputedStyle style, bool isMask = false)
    {
        var images = style.BackgroundImage;
        bool hasImage = images != null && images.Count > 0 && images.Any(s => s != "none");
        bool hasColor = style.BackgroundColor.HasValue && style.BackgroundColor.Value.Alpha > 0;
        if (!hasImage && !hasColor)
            return null;

        FillLayer? head = null;
        FillLayer? tail = null;

        if (hasImage)
        {
            // Build a FillLayer chain, one per image.  CSS paints layers
            // front-to-back in source order, so the last image in the list is
            // the bottom-most layer (painted first).
            for (int i = images!.Count - 1; i >= 0; i--)
            {
                var img = images[i];
                if (string.IsNullOrEmpty(img) || img == "none")
                    continue;
                var layer = new FillLayer
                {
                    Image = img,
                    Color = hasColor && i == images.Count - 1 ? style.BackgroundColor : null,
                    Attachment = style.BackgroundAttachment switch
                    {
                        BackgroundAttachment.Fixed => FillAttachment.Fixed,
                        BackgroundAttachment.Local => FillAttachment.Local,
                        _ => FillAttachment.Scroll
                    },
                    Clip = style.BackgroundClip switch
                    {
                        "content-box" => FillBox.Content,
                        "border-box" => FillBox.Border,
                        "text" => FillBox.Text,
                        _ => FillBox.Padding
                    },
                    Origin = style.BackgroundOrigin switch
                    {
                        "content-box" => FillBoxOrigin.Content,
                        "border-box" => FillBoxOrigin.Border,
                        _ => FillBoxOrigin.Padding
                    },
                    PositionX = style.BackgroundPositionX,
                    PositionY = style.BackgroundPositionY,
                    SizeType = style.BackgroundSize switch
                    {
                        BackgroundSizeType.Cover => FillSizeType.Cover,
                        BackgroundSizeType.Contain => FillSizeType.Contain,
                        BackgroundSizeType.Length => FillSizeType.Length,
                        _ => FillSizeType.Auto
                    },
                    SizeWidth = style.BackgroundSizeWidth,
                    SizeHeight = style.BackgroundSizeHeight,
                    RepeatX = style.BackgroundRepeat switch
                    {
                        BackgroundRepeat.NoRepeat or BackgroundRepeat.RepeatY => FillRepeat.NoRepeat,
                        BackgroundRepeat.Round => FillRepeat.Round,
                        BackgroundRepeat.Space => FillRepeat.Space,
                        _ => FillRepeat.Repeat,
                    },
                    RepeatY = style.BackgroundRepeat switch
                    {
                        BackgroundRepeat.NoRepeat or BackgroundRepeat.RepeatX => FillRepeat.NoRepeat,
                        BackgroundRepeat.Round => FillRepeat.Round,
                        BackgroundRepeat.Space => FillRepeat.Space,
                        _ => FillRepeat.Repeat,
                    },
                };
                if (head == null)
                    head = tail = layer;
                else
                {
                    tail!.Next = layer;
                    tail = layer;
                }
            }
        }

        // If only a color (no images), build a single layer with the color.
        if (head == null && hasColor)
        {
            head = new FillLayer { Color = style.BackgroundColor };
        }

return head;
    }
}
