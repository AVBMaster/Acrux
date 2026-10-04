namespace Acrux.Core.Dom;

public enum NodeType : ushort
{
    Element = 1,
    Attribute = 2,
    Text = 3,
    CDataSection = 4,
    EntityReference = 5,
    Entity = 6,
    ProcessingInstruction = 7,
    Comment = 8,
    Document = 9,
    DocumentType = 10,
    DocumentFragment = 11,
    Notation = 12
}

[Flags]
public enum DocumentPosition : ushort
{
    Same = 0,
    Disconnected = 0x01,
    Preceding = 0x02,
    Following = 0x04,
    Contains = 0x08,
    ContainedBy = 0x10,
    ImplementationSpecific = 0x20
}
