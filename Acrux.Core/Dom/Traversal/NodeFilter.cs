namespace Acrux.Core.Dom;

public enum FilterResult
{
    Accept = 1,
    Reject = 2,
    Skip = 3
}

public abstract class NodeFilter
{
    public const int FilterAccept = 1;
    public const int FilterReject = 2;
    public const int FilterSkip = 3;

    public const int ShowAll = -1;
    public const int ShowElement = 0x1;
    public const int ShowAttribute = 0x2;
    public const int ShowText = 0x4;
    public const int ShowCdataSection = 0x8;
    public const int ShowEntityReference = 0x10;
    public const int ShowEntity = 0x20;
    public const int ShowProcessingInstruction = 0x40;
    public const int ShowComment = 0x80;
    public const int ShowDocument = 0x100;
    public const int ShowDocumentType = 0x200;
    public const int ShowDocumentFragment = 0x400;
    public const int ShowNotation = 0x800;

    public abstract FilterResult AcceptNode(Node node);
}
