namespace Acrux.Core.Dom;

public class StaticRange
{
    public Node StartContainer { get; }
    public int StartOffset { get; }
    public Node EndContainer { get; }
    public int EndOffset { get; }
    public bool Collapsed => StartContainer == EndContainer && StartOffset == EndOffset;

    public StaticRange(Node startContainer, int startOffset, Node endContainer, int endOffset)
    {
        StartContainer = startContainer;
        StartOffset = startOffset;
        EndContainer = endContainer;
        EndOffset = endOffset;
    }
}
