namespace Acrux.Core.Layout;

/// <summary>
/// Fragment tree mutator / cloner / repeater.
///
/// This is needed in order to implement repeated content in block fragmentation
/// (repeated table headers / footers, and also fixed-positioned elements when
/// printing).
///
/// On the layout side, we only lay out the element once, but pre-paint and paint
/// require one unique fragment for each time it repeats, since we need one
/// FragmentData object for each, each with its own global-ish paint offset.
/// </summary>
public sealed class FragmentRepeater
{
    private readonly bool _isFirstClone;
    private readonly bool _isLastFragment;

    public FragmentRepeater(bool isFirstClone, bool isLastFragment)
    {
        _isFirstClone = isFirstClone;
        _isLastFragment = isLastFragment;
    }

    // Deep-clone the subtree of an already shallowly cloned fragment. This will
    // also create new break tokens inside, in order to set unique sequence
    // numbers. The result is only usable by pre-paint / painting, not by actual
    // layout.
    public void CloneChildFragments(BoxFragment clonedFragment)
    {
        for (int i = 0; i < clonedFragment.Children.Count; i++)
        {
            var child = clonedFragment.Children[i];
            if (child.LayoutObject is AuroraBox)
            {
                var childResult = GetClonableLayoutResult((AuroraBox)child.LayoutObject, child);
                childResult = Repeat(childResult);
                clonedFragment.Children[i] = childResult.Fragment;
            }
        }
    }

    private LayoutResult Repeat(LayoutResult other)
    {
        LayoutResult clonedResult = LayoutResultExtensions.Clone(other);
        var clonedFragment = clonedResult.Fragment;
        var layoutBox = (AuroraBox)clonedFragment.LayoutObject!;

        if (_isFirstClone && clonedResult.Fragment.IsFirstForNode)
        {
            // We're (re-)inserting cloned results, and we're at the first clone. Remove
            // the old results first.
            RemoveClonedResults(layoutBox);
        }

        CloneChildFragments(clonedFragment);

        // The first-for-node bit has also been cloned. But we're obviously not the
        // first anymore if we're repeated.
        clonedResult.Fragment.IsFirstForNode = false;

        layoutBox.AppendLayoutResult(clonedResult);
        if (_isLastFragment && (clonedFragment.BreakToken == null || clonedFragment.BreakToken.IsRepeated))
        {
            // We've reached the end. We can finally add missing break tokens, and
            // update cloned sequence numbers.
            UpdateBreakTokens(layoutBox);
            layoutBox.ClearNeedsLayout();
            layoutBox.FinalizeLayoutResults();
        }
        return clonedResult;
    }

    private static LayoutResult GetClonableLayoutResult(AuroraBox layoutBox, BoxFragment fragment)
    {
        var bt = fragment.BreakToken as BlockBreakToken;
        if (bt != null)
        {
            if (!bt.IsRepeated)
                return layoutBox.GetLayoutResult((uint)bt.SequenceNumber);
        }
        // Cloned results may already have been added (so we can't just pick the last
        // one), but the break tokens have not yet been updated. Look for the first
        // result without a break token. Or look for the first result with a repeated
        // break token (unless the repeated break token is the result of an inner
        // fragmentation context), in case we've already been through this. This will
        // actually be the very first result, unless there's a fragmentation context
        // established inside the repeated root.
        foreach (var result in layoutBox.GetLayoutResults())
        {
            var breakToken2 = result.Fragment.BreakToken;
            if (breakToken2 == null || breakToken2.IsRepeated)
                return result;
        }
        throw new InvalidOperationException("No clonable layout result found");
    }

    // Remove all cloned results, but keep the first original one(s).
    private static void RemoveClonedResults(AuroraBox layoutBox)
    {
        for (uint idx = 0; idx < layoutBox.PhysicalFragmentCount; idx++)
        {
            var breakToken = layoutBox.GetPhysicalFragment(idx).BreakToken as BlockBreakToken;
            if (breakToken == null || breakToken.IsRepeated)
            {
                layoutBox.ShrinkLayoutResults(idx + 1);
                return;
            }
        }
        throw new InvalidOperationException("No clonable result to remove");
    }

    private static void UpdateBreakTokens(AuroraBox layoutBox)
    {
        uint sequenceNumber = 0;
        uint fragmentCount = layoutBox.PhysicalFragmentCount;

        // If this box is a fragmentation context root, we also need to update the
        // break tokens of the fragmentainers, since they aren't associated with a
        // layout object on their own.
        BoxFragment? lastFragmentainer = null;
        uint fragmentainerSequenceNumber = 0;

        for (uint idx = 0; idx < fragmentCount; idx++, sequenceNumber++)
        {
            var fragment = layoutBox.GetPhysicalFragment(idx);
            var breakToken = fragment.BreakToken as BlockBreakToken;
            if (breakToken != null && breakToken.IsRepeated)
                breakToken = null;
            if (breakToken != null)
            {
                // It may already have a break token, if there's another fragmentation
                // context inside the repeated root. But we need to update the sequence
                // number, unless we're inside the very first fragment generated for the
                // repeated root.
                if (breakToken.SequenceNumber != sequenceNumber)
                {
                    breakToken = BlockBreakToken.CreateForBreakInRepeatedFragment((int)sequenceNumber, breakToken.ConsumedBlockSize, breakToken.IsAtBlockEnd);
                }
            }
            else if (idx + 1 < fragmentCount)
            {
                // Unless it's the very last fragment, it needs a break token.
                breakToken = BlockBreakToken.CreateRepeated((int)sequenceNumber);
            }
            fragment.BreakToken = breakToken;

            // That's all we have to do, unless this is a fragmentation context root.

            if (!fragment.IsFragmentationContextRoot)
                continue;

            // If this is a fragmentation context root, we also need to update the
            // fragmentainers (which don't have a LayoutBox associated with them).

            foreach (var child in fragment.Children)
            {
                if (child.IsFragmentainerBox)
                {
                    var fragmentainerBreakToken = child.BreakToken as BlockBreakToken;
                    if (fragmentainerBreakToken != null && fragmentainerBreakToken.IsRepeated)
                        fragmentainerBreakToken = null;
                    if (fragmentainerBreakToken != null)
                    {
                        if (fragmentainerBreakToken.SequenceNumber != fragmentainerSequenceNumber)
                        {
                            fragmentainerBreakToken
                                = BlockBreakToken.CreateForBreakInRepeatedFragment((int)fragmentainerSequenceNumber, fragmentainerBreakToken.ConsumedBlockSize,
                                    /* isAtBlockEnd */ false);
                            child.BreakToken = fragmentainerBreakToken;
                        }
                    }
                    else
                    {
                        fragmentainerBreakToken = BlockBreakToken.CreateRepeated((int)fragmentainerSequenceNumber);
                        child.BreakToken = fragmentainerBreakToken;

                        // Since this fragmentainer didn't have a break token, it might be the
                        // very last one, but it's not straight-forward to figure out whether
                        // this is actually the case. So just keep track of what we're visiting.
                        // It's been given a break token for now. If it turns out that this was
                        // the last fragmentainer, we'll remove it again further below.
                        lastFragmentainer = child;
                    }
                    fragmentainerSequenceNumber++;
                }
            }
        }

        // The last fragmentainer shouldn't have an outgoing break token, but it got
        // one above.
        if (lastFragmentainer != null)
            lastFragmentainer.BreakToken = null;
    }
}

/// <summary>
/// Extension methods for LayoutResult to support cloning.
/// </summary>
public static class LayoutResultExtensions
{
    public static LayoutResult Clone(LayoutResult other)
    {
        return new LayoutResult
        {
            Status = other.Status,
            Fragment = other.Fragment,
            IntrinsicBlockSize = other.IntrinsicBlockSize,
            BfcBlockOffset = other.BfcBlockOffset,
            BfcLineOffset = other.BfcLineOffset,
            IsSelfCollapsing = other.IsSelfCollapsing,
            IsPushedByFloats = other.IsPushedByFloats,
            EndMarginStrut = other.EndMarginStrut,
            ConstraintSpaceForCaching = other.ConstraintSpaceForCaching,
        };
    }
}

/// <summary>
/// Extension methods for BoxFragment to support cloning.
/// </summary>
public static class BoxFragmentCloner
{
    public static BoxFragment Clone(BoxFragment source)
    {
        var result = new BoxFragment
        {
            InlineOffset = source.InlineOffset,
            BlockOffset = source.BlockOffset,
            InlineSize = source.InlineSize,
            BlockSize = source.BlockSize,
            MarginLeft = source.MarginLeft,
            MarginTop = source.MarginTop,
            MarginRight = source.MarginRight,
            MarginBottom = source.MarginBottom,
            BorderLeft = source.BorderLeft,
            BorderTop = source.BorderTop,
            BorderRight = source.BorderRight,
            BorderBottom = source.BorderBottom,
            PaddingLeft = source.PaddingLeft,
            PaddingTop = source.PaddingTop,
            PaddingRight = source.PaddingRight,
            PaddingBottom = source.PaddingBottom,
            Element = source.Element,
            FragmentItems = source.FragmentItems,
            IsFloating = source.IsFloating,
            IsInlineBox = source.IsInlineBox,
            IsBlockInInline = source.IsBlockInInline,
            IsOutOfFlowPositioned = source.IsOutOfFlowPositioned,
        };
        result.Children.Clear();
        result.Children.AddRange(source.Children);
        result.Lines.Clear();
        result.Lines.AddRange(source.Lines);
        return result;
    }
}