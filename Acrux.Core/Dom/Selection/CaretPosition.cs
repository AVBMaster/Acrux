namespace Acrux.Core.Dom;

public class CaretPosition
{
    public Node OffsetNode { get; }
    public int Offset { get; }

    public CaretPosition(Node offsetNode, int offset)
    {
        OffsetNode = offsetNode;
        Offset = offset;
    }

    public Node? GetClientRect()
    {
        return null;
    }
}
