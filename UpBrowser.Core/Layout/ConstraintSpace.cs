using UpBrowser.Core.Layout.Geometry;

namespace UpBrowser.Core.Layout;

/// <summary>
/// ConstraintSpace - encodes the dimensional constraint environment for layout passes.
/// Mirrors the modern layout pipeline's ConstraintSpace architecture.
/// </summary>
public struct ConstraintSpace
{
    private readonly AvailableSizeType _availableInlineSize;
    private readonly AvailableSizeType _availableBlockSize;
    private readonly float _inlineSizeValue;
    private readonly float _blockSizeValue;
    private readonly PercentageStorage _percentageResolutionStorage;
    private readonly float _percentageResolutionInline;
    private readonly float _percentageResolutionBlock;
    private readonly bool _isFixedInlineSize;
    private readonly bool _isFixedBlockSize;
    private readonly AutoBehavior _inlineAutoBehavior;
    private readonly AutoBehavior _blockAutoBehavior;
    private readonly bool _isShrinkToFit;
    private readonly WritingMode _writingMode;
    private readonly bool _isNewFormattingContext;
    private readonly bool _isHiddenForPaint;
    private readonly bool _isTableCell;
    private readonly FragmentationType _fragmentationType;
    private readonly float _bfcBlockOffset;
    private readonly float _blockOffset;
    private readonly BaselineAlgorithmType _baselineAlgorithmType;

    // Block formatting context state (mirrors constraint_space.h).
    private readonly float _bfcLineOffset;
    private readonly TextDirection _direction;
    private readonly MarginStrut _marginStrut;
    private readonly ExclusionSpace? _exclusionSpace;
    private readonly AdjoiningObjectTypes _adjoiningObjectTypes;
    private readonly float? _forcedBfcBlockOffset;
    private readonly float? _optimisticBfcBlockOffset;
    private readonly float _expectedBfcBlockOffset;
    private readonly float _clearanceOffset;
    private readonly bool _isPushedByFloats;
    private readonly bool _ancestorHasClearancePastAdjoiningFloats;

    // Unit-resolution context: rem resolves against the root element's
    // computed font-size; vw/vh/vmin/vmax resolve against the initial containing
    // block (= viewport). Every algorithm used to hardcode these (rootFontSize=16,
    // viewport=available-size-or-0), which silently broke pages that rely on them.
    private readonly float _rootFontSize;
    private readonly float _viewportWidth;
    private readonly float _viewportHeight;

    // Classic-scrollbar space reservation: inline size eaten by a vertical
    // scrollbar of THIS box (ConstraintSpace::scrollbar_space). When
    // set, child available inline size shrinks by it so content no longer
    // flows underneath the bar.
    private readonly float _scrollbarInline;
    private readonly bool _scrollbarSpaceReserved;

    // A table cell in a border-collapse: collapse table owns only half of every
    // shared grid line (CSS 2.1 §17.6.2), and that half - not the cell's own
    // border-width - is what its box is built from. The table algorithm knows
    // the resolved edges, so it hands the strut down for this box only.
    private readonly BoxStrut? _collapsedCellBorders;

    /// <summary>
    /// Device pixels per CSS pixel. Layout stays in CSS pixels; this only feeds
    /// the half-device-pixel tolerance used when testing whether inline content
    /// fits a line, so wrap points match what the snapped paint will show.
    /// </summary>
    private readonly float _dpiScale = 1f;

    public const float DefaultRootFontSize = 16f;

    public ConstraintSpace(
        float availableInlineSize = float.NaN,
        float availableBlockSize = float.NaN,
        bool isFixedInlineSize = false,
        bool isFixedBlockSize = false,
        AutoBehavior inlineAutoBehavior = AutoBehavior.StretchImplicit,
        AutoBehavior blockAutoBehavior = AutoBehavior.StretchImplicit,
        bool isShrinkToFit = false,
        WritingMode writingMode = WritingMode.HorizontalTb,
        float percentageResolutionInline = 0,
        float percentageResolutionBlock = 0,
        bool isNewFormattingContext = false,
        bool isHiddenForPaint = false,
        bool isTableCell = false,
        FragmentationType fragmentationType = FragmentationType.None,
        float bfcBlockOffset = 0,
        float blockOffset = 0,
        BaselineAlgorithmType baselineAlgorithmType = BaselineAlgorithmType.Default,
        float bfcLineOffset = 0,
        TextDirection direction = TextDirection.Ltr,
        MarginStrut marginStrut = default,
        ExclusionSpace? exclusionSpace = null,
        AdjoiningObjectTypes adjoiningObjectTypes = AdjoiningObjectTypes.None,
        float? forcedBfcBlockOffset = null,
        float? optimisticBfcBlockOffset = null,
        float expectedBfcBlockOffset = 0,
        float clearanceOffset = float.MinValue,
        bool isPushedByFloats = false,
        bool ancestorHasClearancePastAdjoiningFloats = false,
        float rootFontSize = DefaultRootFontSize,
        float viewportWidth = 0,
        float viewportHeight = 0,
        float scrollbarInline = 0,
        bool scrollbarSpaceReserved = false,
        BoxStrut? collapsedCellBorders = null,
        float dpiScale = 1f)
    {
        _availableInlineSize = float.IsNaN(availableInlineSize) ? AvailableSizeType.Auto : AvailableSizeType.Definite;
        _availableBlockSize = float.IsNaN(availableBlockSize) ? AvailableSizeType.Auto : AvailableSizeType.Definite;
        _inlineSizeValue = availableInlineSize;
        _blockSizeValue = availableBlockSize;
        _isFixedInlineSize = isFixedInlineSize;
        _isFixedBlockSize = isFixedBlockSize;
        _inlineAutoBehavior = inlineAutoBehavior;
        _blockAutoBehavior = blockAutoBehavior;
        _isShrinkToFit = isShrinkToFit;
        _writingMode = writingMode;
        _isNewFormattingContext = isNewFormattingContext;
        _isHiddenForPaint = isHiddenForPaint;
        _isTableCell = isTableCell;
        _fragmentationType = fragmentationType;
        _bfcBlockOffset = bfcBlockOffset;
        _blockOffset = blockOffset;
        _baselineAlgorithmType = baselineAlgorithmType;
        _bfcLineOffset = bfcLineOffset;
        _direction = direction;
        _marginStrut = marginStrut;
        _exclusionSpace = exclusionSpace;
        _adjoiningObjectTypes = adjoiningObjectTypes;
        _forcedBfcBlockOffset = forcedBfcBlockOffset;
        _optimisticBfcBlockOffset = optimisticBfcBlockOffset;
        _expectedBfcBlockOffset = expectedBfcBlockOffset;
        _clearanceOffset = clearanceOffset;
        _isPushedByFloats = isPushedByFloats;
        _ancestorHasClearancePastAdjoiningFloats = ancestorHasClearancePastAdjoiningFloats;
        _rootFontSize = rootFontSize > 0 ? rootFontSize : DefaultRootFontSize;
        _viewportWidth = viewportWidth;
        _viewportHeight = viewportHeight;
        _scrollbarInline = scrollbarInline;
        _scrollbarSpaceReserved = scrollbarSpaceReserved;
        _collapsedCellBorders = collapsedCellBorders;
        _dpiScale = dpiScale > 0 ? dpiScale : 1f;

        // A caller can ask for an explicitly indefinite percentage base by passing a
        // negative size: a percentage that resolves against nothing behaves as auto
        // (CSS 2.1 §10.5), which is not the same as inheriting the available size.
        _percentageResolutionStorage = (percentageResolutionInline != 0 || percentageResolutionBlock != 0)
            ? PercentageStorage.Defined
            : PercentageStorage.SameAsAvailable;
        _percentageResolutionInline = MapPercentageBase(percentageResolutionInline, availableInlineSize);
        _percentageResolutionBlock = MapPercentageBase(percentageResolutionBlock, availableBlockSize);

        static float MapPercentageBase(float requested, float available) => requested switch
        {
            0 => available,
            < 0 => float.NaN,
            _ => requested,
        };
    }

    public float AvailableInlineSize => _availableInlineSize == AvailableSizeType.Definite ? _inlineSizeValue : float.NaN;
    public float AvailableBlockSize => _availableBlockSize == AvailableSizeType.Definite ? _blockSizeValue : float.NaN;
    public bool HasDefiniteInlineSize => _availableInlineSize == AvailableSizeType.Definite;
    public bool HasDefiniteBlockSize => _availableBlockSize == AvailableSizeType.Definite;
    public bool IsFixedInlineSize => _isFixedInlineSize;
    public bool IsFixedBlockSize => _isFixedBlockSize;
    public AutoBehavior InlineAutoBehavior => _inlineAutoBehavior;
    public AutoBehavior BlockAutoBehavior => _blockAutoBehavior;
    public bool IsShrinkToFit => _isShrinkToFit;
    public WritingMode WritingMode => _writingMode;
    public bool IsNewFormattingContext => _isNewFormattingContext;
    public bool IsHiddenForPaint => _isHiddenForPaint;
    public bool IsTableCell => _isTableCell;
    public FragmentationType FragmentationType => _fragmentationType;
    public bool HasBlockFragmentation => _fragmentationType != FragmentationType.None;
    public float BfcBlockOffset => _bfcBlockOffset;
    public float BlockOffset => _blockOffset;
    public BaselineAlgorithmType BaselineAlgorithmType => _baselineAlgorithmType;

    public float PercentageResolutionInlineSize =>
        _percentageResolutionStorage == PercentageStorage.SameAsAvailable ? AvailableInlineSize : _percentageResolutionInline;

    public float GetBfcBlockOffset => _bfcBlockOffset;

    public float PercentageResolutionBlockSize =>
        _percentageResolutionStorage == PercentageStorage.SameAsAvailable ? AvailableBlockSize : _percentageResolutionBlock;

    public float BfcLineOffset => _bfcLineOffset;
    public TextDirection Direction => _direction;
    public MarginStrut MarginStrut => _marginStrut;
    public ExclusionSpace? ExclusionSpace => _exclusionSpace;
    public AdjoiningObjectTypes AdjoiningObjectTypes => _adjoiningObjectTypes;
    public float? ForcedBfcBlockOffset => _forcedBfcBlockOffset;
    public float? OptimisticBfcBlockOffset => _optimisticBfcBlockOffset;
    public float ExpectedBfcBlockOffset => _expectedBfcBlockOffset;
    public float ClearanceOffset => _clearanceOffset;
    public bool HasClearanceOffset => _clearanceOffset > float.MinValue;
    public bool IsPushedByFloats => _isPushedByFloats;
    public bool AncestorHasClearancePastAdjoiningFloats => _ancestorHasClearancePastAdjoiningFloats;

    /// <summary>Root element computed font-size; the rem basis.</summary>
    public float RootFontSize => _rootFontSize;
    /// <summary>Device pixels per CSS pixel (line-fit tolerance basis).</summary>
    public float DpiScale => _dpiScale;
    /// <summary>Initial containing block width; the vw/vmin/vmax basis.</summary>
    public float ViewportWidth => _viewportWidth;
    /// <summary>Initial containing block height; the vh/vmin/vmax basis.</summary>
    public float ViewportHeight => _viewportHeight;

    /// <summary>Inline size reserved for this box's own vertical scrollbar.</summary>
    public float ScrollbarInline => _scrollbarInline;
    /// <summary>True once a layout pass already reserved scrollbar space (guards relayout recursion).</summary>
    public bool HasScrollbarSpaceReserved => _scrollbarSpaceReserved;

    /// <summary>Border strut a table cell must use instead of its own computed
    /// borders, set only by the collapsing table algorithm (CSS 2.1 §17.6.2).</summary>
    public BoxStrut? CollapsedCellBorders => _collapsedCellBorders;

    public BfcOffset GetBfcOffset() => new(BfcLineOffset, BfcBlockOffset);
    public WritingDirectionMode GetWritingDirection() => new(WritingMode, Direction);
    public TextDirection GetTextDirection() => Direction;

    private ConstraintSpace Rebuild(
        float inlineSizeValue, float blockSizeValue, bool isFixedInlineSize, bool isFixedBlockSize,
        AutoBehavior inlineAutoBehavior, AutoBehavior blockAutoBehavior, bool isShrinkToFit,
        float pctInline, float pctBlock, bool isNewFormattingContext, bool isHiddenForPaint, bool isTableCell,
        FragmentationType fragmentationType, float bfcBlockOffset, float blockOffset, BaselineAlgorithmType baselineAlgorithmType,
        float? bfcLineOffset = null, TextDirection? direction = null, MarginStrut? marginStrut = null, ExclusionSpace? exclusionSpace = null,
        AdjoiningObjectTypes? adjoiningObjectTypes = null, float? forcedBfcBlockOffset = null, float? optimisticBfcBlockOffset = null,
        float? expectedBfcBlockOffset = null, float? clearanceOffset = null, bool? isPushedByFloats = null,
        bool? ancestorHasClearancePastAdjoiningFloats = null,
        float? scrollbarInline = null, bool? scrollbarSpaceReserved = null) => new(
        inlineSizeValue, blockSizeValue, isFixedInlineSize, isFixedBlockSize, inlineAutoBehavior, blockAutoBehavior, isShrinkToFit,
        _writingMode, pctInline, pctBlock, isNewFormattingContext, isHiddenForPaint, isTableCell, fragmentationType,
        bfcBlockOffset, blockOffset, baselineAlgorithmType,
        bfcLineOffset ?? _bfcLineOffset, direction ?? _direction, marginStrut ?? _marginStrut, exclusionSpace ?? _exclusionSpace,
        adjoiningObjectTypes ?? _adjoiningObjectTypes, forcedBfcBlockOffset ?? _forcedBfcBlockOffset, optimisticBfcBlockOffset ?? _optimisticBfcBlockOffset,
        expectedBfcBlockOffset ?? _expectedBfcBlockOffset, clearanceOffset ?? _clearanceOffset,
        isPushedByFloats ?? _isPushedByFloats, ancestorHasClearancePastAdjoiningFloats ?? _ancestorHasClearancePastAdjoiningFloats,
        _rootFontSize, _viewportWidth, _viewportHeight,
        scrollbarInline ?? _scrollbarInline, scrollbarSpaceReserved ?? _scrollbarSpaceReserved,
        _collapsedCellBorders, _dpiScale);

    /// <summary>
    /// Reserve inline space for this box's own vertical scrollbar and mark the
    /// reservation done (a later overflow check must not reserve again).
    /// </summary>
    public ConstraintSpace WithScrollbarInline(float thickness) => Rebuild(
        _inlineSizeValue, _blockSizeValue, _isFixedInlineSize, _isFixedBlockSize, _inlineAutoBehavior, _blockAutoBehavior, _isShrinkToFit,
        _percentageResolutionInline, _percentageResolutionBlock, _isNewFormattingContext, _isHiddenForPaint, _isTableCell,
        _fragmentationType, _bfcBlockOffset, _blockOffset, _baselineAlgorithmType,
        scrollbarInline: thickness, scrollbarSpaceReserved: true);

    public ConstraintSpace WithInlineSize(float size) => Rebuild(
        size, _blockSizeValue, true, _isFixedBlockSize, _inlineAutoBehavior, _blockAutoBehavior, _isShrinkToFit,
        _percentageResolutionInline, _percentageResolutionBlock, _isNewFormattingContext, _isHiddenForPaint, _isTableCell,
        _fragmentationType, _bfcBlockOffset, _blockOffset, _baselineAlgorithmType);

    public ConstraintSpace WithBlockSize(float size) => Rebuild(
        _inlineSizeValue, size, _isFixedInlineSize, true, _inlineAutoBehavior, _blockAutoBehavior, _isShrinkToFit,
        _percentageResolutionInline, _percentageResolutionBlock, _isNewFormattingContext, _isHiddenForPaint, _isTableCell,
        _fragmentationType, _bfcBlockOffset, _blockOffset, _baselineAlgorithmType);

    public ConstraintSpace WithNewFormattingContext(bool value) => Rebuild(
        _inlineSizeValue, _blockSizeValue, _isFixedInlineSize, _isFixedBlockSize, _inlineAutoBehavior, _blockAutoBehavior, _isShrinkToFit,
        _percentageResolutionInline, _percentageResolutionBlock, value, _isHiddenForPaint, _isTableCell,
        _fragmentationType, _bfcBlockOffset, _blockOffset, _baselineAlgorithmType);

    /// <summary>Copy with the percentage-resolution base replaced. A child's
    /// percentage padding/margin resolves against the formatting context's own
    /// content box, which differs from the space the context itself received.</summary>
    public ConstraintSpace WithPercentageResolution(float inlineSize, float blockSize) => Rebuild(
        _inlineSizeValue, _blockSizeValue, _isFixedInlineSize, _isFixedBlockSize, _inlineAutoBehavior, _blockAutoBehavior, _isShrinkToFit,
        inlineSize, blockSize, _isNewFormattingContext, _isHiddenForPaint, _isTableCell,
        _fragmentationType, _bfcBlockOffset, _blockOffset, _baselineAlgorithmType);
    public ConstraintSpace WithFragmentationType(FragmentationType type) => Rebuild(
        _inlineSizeValue, _blockSizeValue, _isFixedInlineSize, _isFixedBlockSize, _inlineAutoBehavior, _blockAutoBehavior, _isShrinkToFit,
        _percentageResolutionInline, _percentageResolutionBlock, _isNewFormattingContext, _isHiddenForPaint, _isTableCell,
        type, _bfcBlockOffset, _blockOffset, _baselineAlgorithmType);

    /// <summary>Copy with the inline/base text direction replaced (CSS writing-mode §3,
    /// the element's own 'direction' wins over the space inherited from its parent).</summary>
    public ConstraintSpace WithDirection(TextDirection direction) => Rebuild(
        _inlineSizeValue, _blockSizeValue, _isFixedInlineSize, _isFixedBlockSize, _inlineAutoBehavior, _blockAutoBehavior, _isShrinkToFit,
        _percentageResolutionInline, _percentageResolutionBlock, _isNewFormattingContext, _isHiddenForPaint, _isTableCell,
        _fragmentationType, _bfcBlockOffset, _blockOffset, _baselineAlgorithmType,
        direction: direction);

    public ConstraintSpace WithBfcBlockOffset(float offset) => Rebuild(
        _inlineSizeValue, _blockSizeValue, _isFixedInlineSize, _isFixedBlockSize, _inlineAutoBehavior, _blockAutoBehavior, _isShrinkToFit,
        _percentageResolutionInline, _percentageResolutionBlock, _isNewFormattingContext, _isHiddenForPaint, _isTableCell,
        _fragmentationType, offset, _blockOffset, _baselineAlgorithmType);

    public ConstraintSpace WithBfcLineOffset(float offset) => Rebuild(
        _inlineSizeValue, _blockSizeValue, _isFixedInlineSize, _isFixedBlockSize, _inlineAutoBehavior, _blockAutoBehavior, _isShrinkToFit,
        _percentageResolutionInline, _percentageResolutionBlock, _isNewFormattingContext, _isHiddenForPaint, _isTableCell,
        _fragmentationType, _bfcBlockOffset, _blockOffset, _baselineAlgorithmType, bfcLineOffset: offset);

    public static ConstraintSpace Infinite() => new(bfcBlockOffset: float.NaN);

    public static ConstraintSpace ForViewport(float width, float height) =>
        new(width, height, false, false, viewportWidth: width, viewportHeight: height);

    public static ConstraintSpaceBuilder Builder(float inlineSize, float blockSize) =>
        new(inlineSize, blockSize);

    /// <summary>
    /// Builder for a CHILD space that inherits this space's unit-resolution
    /// context (root font-size &amp; viewport). Always prefer this over the static
    /// <see cref="Builder"/> when descending the layout tree, so rem/vw/vh keep
    /// resolving against the document root instead of defaults.
    /// </summary>
    public ConstraintSpaceBuilder InheritBuilder(float inlineSize, float blockSize) =>
        new ConstraintSpaceBuilder(inlineSize, blockSize)
            .SetRootFontSize(_rootFontSize)
            .SetDpiScale(_dpiScale)
            .SetViewportSize(_viewportWidth, _viewportHeight);

    public override string ToString() =>
        $"ConstraintSpace(inline:{(HasDefiniteInlineSize ? _inlineSizeValue.ToString("F1") : "auto")}, block:{(HasDefiniteBlockSize ? _blockSizeValue.ToString("F1") : "auto")})";
}

public class ConstraintSpaceBuilder
{
    private float _inlineSize;
    private float _blockSize;
    private bool _isFixedInline;
    private bool _isFixedBlock;
    private AutoBehavior _inlineAuto = AutoBehavior.StretchImplicit;
    private AutoBehavior _blockAuto = AutoBehavior.StretchImplicit;
    private bool _shrinkToFit;
    private WritingMode _writingMode = WritingMode.HorizontalTb;
    private float _pctInline;
    private float _pctBlock;
    private bool _isNewFc;
    private bool _hiddenForPaint;
    private bool _isTableCell;
    private FragmentationType _fragType;
    private float _bfcBlockOffset;
    private float _blockOffset;
    private BaselineAlgorithmType _baselineType;
    private float _bfcLineOffset;
    private TextDirection _direction = TextDirection.Ltr;
    private MarginStrut _marginStrut;
    private ExclusionSpace? _exclusionSpace;
    private AdjoiningObjectTypes _adjoiningObjectTypes = AdjoiningObjectTypes.None;
    private float? _forcedBfcBlockOffset;
    private float? _optimisticBfcBlockOffset;
    private float _expectedBfcBlockOffset;
    private float _clearanceOffset = float.MinValue;
    private bool _isPushedByFloats;
    private bool _ancestorHasClearancePastAdjoiningFloats;
    private float _rootFontSize = ConstraintSpace.DefaultRootFontSize;
    private float _dpiScale = 1f;
    private float _viewportWidth;
    private float _viewportHeight;
    private BoxStrut? _collapsedCellBorders;

    public ConstraintSpaceBuilder(float inlineSize, float blockSize)
    {
        _inlineSize = inlineSize;
        _blockSize = blockSize;
    }

    public ConstraintSpaceBuilder SetAvailableSize(float inlineSize, float blockSize) { _inlineSize = inlineSize; _blockSize = blockSize; return this; }
    public ConstraintSpaceBuilder SetPercentageResolution(float inlineSize, float blockSize) { _pctInline = inlineSize; _pctBlock = blockSize; return this; }
    public ConstraintSpaceBuilder SetIsFixedInlineSize(bool v) { _isFixedInline = v; return this; }
    public ConstraintSpaceBuilder SetIsFixedBlockSize(bool v) { _isFixedBlock = v; return this; }
    public ConstraintSpaceBuilder SetShrinkToFit(bool v) { _shrinkToFit = v; return this; }
    public ConstraintSpaceBuilder SetWritingMode(WritingMode v) { _writingMode = v; return this; }
    public ConstraintSpaceBuilder SetIsNewFormattingContext(bool v) { _isNewFc = v; return this; }
    public ConstraintSpaceBuilder SetIsHiddenForPaint(bool v) { _hiddenForPaint = v; return this; }
    public ConstraintSpaceBuilder SetIsTableCell(bool v) { _isTableCell = v; return this; }
    public ConstraintSpaceBuilder SetFragmentationType(FragmentationType v) { _fragType = v; return this; }
    public ConstraintSpaceBuilder SetBfcBlockOffset(float v) { _bfcBlockOffset = v; return this; }
    public ConstraintSpaceBuilder SetBlockOffset(float v) { _blockOffset = v; return this; }
    public ConstraintSpaceBuilder SetBaselineAlgorithmType(BaselineAlgorithmType v) { _baselineType = v; return this; }
    public ConstraintSpaceBuilder SetBfcLineOffset(float v) { _bfcLineOffset = v; return this; }
    public ConstraintSpaceBuilder SetDirection(TextDirection v) { _direction = v; return this; }
    public ConstraintSpaceBuilder SetMarginStrut(MarginStrut v) { _marginStrut = v; return this; }
    public ConstraintSpaceBuilder SetExclusionSpace(ExclusionSpace? v) { _exclusionSpace = v; return this; }
    public ConstraintSpaceBuilder SetAdjoiningObjectTypes(AdjoiningObjectTypes v) { _adjoiningObjectTypes = v; return this; }
    public ConstraintSpaceBuilder SetForcedBfcBlockOffset(float v) { _forcedBfcBlockOffset = v; return this; }
    public ConstraintSpaceBuilder SetOptimisticBfcBlockOffset(float v) { _optimisticBfcBlockOffset = v; return this; }
    public ConstraintSpaceBuilder SetExpectedBfcBlockOffset(float v) { _expectedBfcBlockOffset = v; return this; }
    public ConstraintSpaceBuilder SetClearanceOffset(float v) { _clearanceOffset = v; return this; }
    public ConstraintSpaceBuilder SetIsPushedByFloats(bool v) { _isPushedByFloats = v; return this; }
    public ConstraintSpaceBuilder SetAncestorHasClearancePastAdjoiningFloats() { _ancestorHasClearancePastAdjoiningFloats = true; return this; }
    public ConstraintSpaceBuilder SetRootFontSize(float v) { _rootFontSize = v > 0 ? v : ConstraintSpace.DefaultRootFontSize; return this; }
    public ConstraintSpaceBuilder SetDpiScale(float v) { _dpiScale = v > 0 ? v : 1f; return this; }
    public ConstraintSpaceBuilder SetViewportSize(float width, float height) { _viewportWidth = width; _viewportHeight = height; return this; }
    public ConstraintSpaceBuilder SetCollapsedCellBorders(BoxStrut? borders) { _collapsedCellBorders = borders; return this; }

    public ConstraintSpace ToConstraintSpace() => new(
        _inlineSize, _blockSize, _isFixedInline, _isFixedBlock, _inlineAuto, _blockAuto, _shrinkToFit, _writingMode,
        _pctInline, _pctBlock, _isNewFc, _hiddenForPaint, _isTableCell, _fragType, _bfcBlockOffset, _blockOffset, _baselineType,
        _bfcLineOffset, _direction, _marginStrut, _exclusionSpace, _adjoiningObjectTypes, _forcedBfcBlockOffset,
        _optimisticBfcBlockOffset, _expectedBfcBlockOffset, _clearanceOffset, _isPushedByFloats, _ancestorHasClearancePastAdjoiningFloats,
        _rootFontSize, _viewportWidth, _viewportHeight, collapsedCellBorders: _collapsedCellBorders, dpiScale: _dpiScale);
}

public enum AvailableSizeType { Auto, Definite, MinContent, MaxContent, FitContent }
public enum PercentageStorage { SameAsAvailable, Defined }
public enum AutoBehavior { StretchImplicit, FitContent, StretchExplicit }
public enum FragmentationType { None, Page, Column, Region }
public enum BaselineAlgorithmType { Default, InlineBlock }