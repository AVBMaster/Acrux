using Acrux.Core.Dom;

namespace Acrux.Core.Layout;

/// <summary>
/// Responsible for iterating through block-level children of a LayoutInputNode.
/// Walks through children in layout order (in-flow, then floats, then
/// out-of-flow). Mirrors block_child_iterator.cc.
/// </summary>
public class BlockChildIterator
{
    public struct Entry
    {
        public LayoutBox? Child;
        public BreakToken? ChildBreakToken;
        public int? ChildIndex;

        public Entry(LayoutBox? child, BreakToken? breakToken, int? childIndex)
        {
            Child = child;
            ChildBreakToken = breakToken;
            ChildIndex = childIndex;
        }
    }

    private LayoutBox? _nextUnstartedChild;
    private BlockBreakToken? _breakToken;
    private int _childTokenIdx;
    private bool _didHandleFirstChild;
    private int? _childIdx;
    private LayoutBox? _trackedChild;

    public BlockChildIterator(LayoutBox? firstChild, BlockBreakToken? breakToken, bool calculateChildIdx)
    {
        _nextUnstartedChild = firstChild;
        _breakToken = breakToken;
        _childTokenIdx = 0;

        if (calculateChildIdx)
        {
            _childIdx = 0;
            _trackedChild = firstChild;
        }

        if (breakToken != null)
        {
            var childBreakTokens = breakToken.ChildBreakTokens;
            if (childBreakTokens.Count > 0 || breakToken.HasSeenAllChildren)
                _nextUnstartedChild = null;
            if (childBreakTokens.Count == 0)
                _breakToken = null;
        }
    }

    public Entry NextChild(BreakToken? previousInlineBreakToken)
    {
        if (previousInlineBreakToken != null)
        {
            _childIdx = null;
            return new Entry(previousInlineBreakToken.Node, previousInlineBreakToken, null);
        }

        if (_didHandleFirstChild)
        {
            if (_breakToken != null)
            {
                var childBreakTokens = _breakToken.ChildBreakTokens;
                if (_childTokenIdx == childBreakTokens.Count)
                {
                    if (!_breakToken.HasSeenAllChildren)
                    {
                        var last = childBreakTokens[_childTokenIdx - 1];
                        AdvanceToNextChild(last.Node);
                    }
                    _breakToken = null;
                }
            }
            else if (_nextUnstartedChild != null)
            {
                AdvanceToNextChild(_nextUnstartedChild);
            }
        }
        else
        {
            _didHandleFirstChild = true;
        }

        BreakToken? currentChildBreakToken = null;
        int? currentChildIdx;
        LayoutBox? currentChild = _nextUnstartedChild;

        if (_breakToken != null)
        {
            var childBreakTokens = _breakToken.ChildBreakTokens;
            if (_childTokenIdx >= childBreakTokens.Count)
            {
                currentChild = null;
                currentChildIdx = _childIdx;
                return new Entry(currentChild, null, currentChildIdx);
            }
            currentChildBreakToken = childBreakTokens[_childTokenIdx++];
            currentChild = currentChildBreakToken.Node;

            if (_childIdx.HasValue)
            {
                while (_trackedChild != currentChild)
                {
                    _trackedChild = _trackedChild?.NextSibling;
                    _childIdx = _childIdx.Value + 1;
                }
                currentChildIdx = _childIdx;
            }
            else
            {
                currentChildIdx = null;
            }
        }
        else if (_nextUnstartedChild != null)
        {
            currentChildIdx = _childIdx;
        }
        else
        {
            currentChildIdx = _childIdx;
        }

        return new Entry(currentChild, currentChildBreakToken, currentChildIdx);
    }

    private void AdvanceToNextChild(LayoutBox? child)
    {
        _nextUnstartedChild = child?.NextSibling;
        if (_childIdx.HasValue)
            _childIdx = _childIdx.Value + 1;
    }
}
