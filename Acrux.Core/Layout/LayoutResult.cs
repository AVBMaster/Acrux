using Acrux.Core.Layout.Geometry;

namespace Acrux.Core.Layout;

// The EStatus enum (mirroring LayoutResult::EStatus in layout_result.h) is
// declared in FragmentBuilder.cs; LayoutResult below uses it.

/// <summary>
/// Result of a layout algorithm, mirroring the LayoutResult concept from
/// layout_result.h. Stores the physical fragment (geometry) plus additional
/// data which is only necessary during layout.
/// </summary>
public class LayoutResult
{
    public EStatus Status { get; set; } = EStatus.Success;
    public BoxFragment Fragment { get; set; } = new();
    public float IntrinsicBlockSize { get; set; }
    public float BfcLineOffset { get; set; }
    public float BfcBlockOffset { get; set; }

    /// <summary>
    /// Nullable BFC block offset of the fragment, mirroring
    /// LayoutResult::BfcBlockOffset(). null means "unresolved".
    /// </summary>
    public float? BfcBlockOffsetValue { get; set; }

    /// <summary>The BFC block offset of the last line box, if known.</summary>
    public float? LineBoxBfcBlockOffset { get; set; }

    public bool IsSelfCollapsing { get; set; }
    public bool IsPushedByFloats { get; set; }
    public MarginStrut EndMarginStrut { get; set; }
    public bool HasForcedBreak { get; set; }

    /// <summary>Whether this fragment modified its incoming margin strut.</summary>
    public bool SubtreeModifiedMarginStrut { get; set; }

    /// <summary>Annotation overflow and space from ruby / annotation boxes.</summary>
    public float AnnotationOverflow { get; set; }
    public float BlockEndAnnotationSpace { get; set; }

    /// <summary>Amount trimmed at the block-end by text-box-trim, if applied.</summary>
    public float? TrimBlockEndBy { get; set; }

    /// <summary>Clearance after a line (BR clear=all handling).</summary>
    public float? ClearanceAfterLine { get; set; }

    public AdjoiningObjectTypes AdjoiningObjectTypes { get; set; } = AdjoiningObjectTypes.None;
    public bool HasAdjoiningObjectDescendants { get; set; }

    /// <summary>The exclusion space after laying out this fragment.</summary>
    public ExclusionSpace? ExclusionSpaceValue { get; set; } = new();

    public int? LinesUntilClamp { get; set; }
    public EarlyBreak? EarlyBreakValue { get; set; }
    public int LineCount { get; set; }
    public bool IsBlockEndTrimmableLine { get; set; }

    public BreakToken? BreakToken { get; set; }

    /// <summary>The constraint space used when this result was computed, for cache matching.</summary>
    public ConstraintSpace? ConstraintSpaceForCaching { get; set; }

    public bool IsFloating => Fragment.IsFloating;
    public bool IsInlineBox => Fragment.IsInlineBox;
    public bool IsBlockInInline => Fragment.IsBlockInInline;
    public bool IsOutOfFlowPositioned => Fragment.IsOutOfFlowPositioned;

    public static LayoutResult FromFragment(BoxFragment fragment) => new()
    {
        Status = EStatus.Success,
        Fragment = fragment,
        IntrinsicBlockSize = fragment.BlockSize
    };

    /// <summary>Create an aborted layout result. Mirrors BoxFragmentBuilder::Abort().</summary>
    public static LayoutResult Abort(EStatus status) => new() { Status = status };
}