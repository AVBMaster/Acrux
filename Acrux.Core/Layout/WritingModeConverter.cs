using Acrux.Core.Layout.Geometry;
using Geom = Acrux.Core.Layout.Geometry;

namespace Acrux.Core.Layout;

/// <summary>
/// Converts between logical and physical coordinate systems, accounting for
/// writing mode and text direction. Mirrors WritingModeConverter in
/// writing_mode_converter.h.
/// </summary>
public class WritingModeConverter
{
    private readonly WritingDirectionMode _writingDirection;
    private readonly PhysicalSize _outerSize;

    public WritingModeConverter(WritingDirectionMode writingDirection, PhysicalSize outerSize)
    {
        _writingDirection = writingDirection;
        _outerSize = outerSize;
    }

    public WritingModeConverter(WritingDirectionMode writingDirection, LogicalSize outerSize)
        : this(writingDirection, ToPhysicalSize(outerSize, writingDirection.WritingMode))
    {
    }

    public WritingModeConverter(WritingDirectionMode writingDirection)
        : this(writingDirection, PhysicalSize.Zero)
    {
    }

    public WritingDirectionMode WritingDirection => _writingDirection;
    public Geom.WritingMode WritingMode => _writingDirection.WritingMode;
    public bool IsLtr => _writingDirection.Direction == TextDirection.Ltr;

    public LogicalOffset ToLogical(PhysicalOffset offset, PhysicalSize innerSize)
    {
        if (IsLtr && WritingMode == Geom.WritingMode.HorizontalTb)
            return new LogicalOffset(offset.Left, offset.Top);

        switch (WritingMode)
        {
            case Geom.WritingMode.HorizontalTb:
                return new LogicalOffset(_outerSize.Width - offset.Left - innerSize.Width, offset.Top);
            case Geom.WritingMode.VerticalRl:
                if (IsLtr)
                    return new LogicalOffset(offset.Top, _outerSize.Width - offset.Left - innerSize.Width);
                return new LogicalOffset(_outerSize.Height - offset.Top - innerSize.Height, _outerSize.Width - offset.Left - innerSize.Width);
            case Geom.WritingMode.VerticalLr:
                if (IsLtr)
                    return new LogicalOffset(offset.Top, offset.Left);
                return new LogicalOffset(_outerSize.Height - offset.Top - innerSize.Height, offset.Left);
            default:
                return new LogicalOffset(offset.Left, offset.Top);
        }
    }

    public PhysicalOffset ToPhysical(LogicalOffset offset, PhysicalSize innerSize)
    {
        if (IsLtr && WritingMode == Geom.WritingMode.HorizontalTb)
            return new PhysicalOffset(offset.InlineOffset, offset.BlockOffset);

        switch (WritingMode)
        {
            case Geom.WritingMode.HorizontalTb:
                return new PhysicalOffset(_outerSize.Width - offset.InlineOffset - innerSize.Width, offset.BlockOffset);
            case Geom.WritingMode.VerticalRl:
                if (IsLtr)
                    return new PhysicalOffset(_outerSize.Width - offset.BlockOffset - innerSize.Width, offset.InlineOffset);
                return new PhysicalOffset(_outerSize.Width - offset.BlockOffset - innerSize.Width, _outerSize.Height - offset.InlineOffset - innerSize.Height);
            case Geom.WritingMode.VerticalLr:
                if (IsLtr)
                    return new PhysicalOffset(offset.BlockOffset, offset.InlineOffset);
                return new PhysicalOffset(offset.BlockOffset, _outerSize.Height - offset.InlineOffset - innerSize.Height);
            default:
                return new PhysicalOffset(offset.InlineOffset, offset.BlockOffset);
        }
    }

    public LogicalRect ToLogical(PhysicalRect rect)
    {
        var size = ToLogical(rect.Size);
        return new LogicalRect(ToLogical(rect.Offset, rect.Size), size);
    }

    public PhysicalRect ToPhysical(LogicalRect rect)
    {
        var size = ToPhysical(rect.Size);
        return new PhysicalRect(ToPhysical(rect.Offset, size), size);
    }

    public LogicalSize ToLogical(PhysicalSize size)
    {
        if (WritingMode == Geom.WritingMode.HorizontalTb)
            return new LogicalSize(size.Width, size.Height);
        return new LogicalSize(size.Height, size.Width);
    }

    public PhysicalSize ToPhysical(LogicalSize size)
    {
        if (WritingMode == Geom.WritingMode.HorizontalTb)
            return new PhysicalSize(size.InlineSize, size.BlockSize);
        return new PhysicalSize(size.BlockSize, size.InlineSize);
    }

    private static PhysicalSize ToPhysicalSize(LogicalSize logical, Geom.WritingMode mode)
    {
        if (mode == Geom.WritingMode.HorizontalTb)
            return new PhysicalSize(logical.InlineSize, logical.BlockSize);
        return new PhysicalSize(logical.BlockSize, logical.InlineSize);
    }
}
