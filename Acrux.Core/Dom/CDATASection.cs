namespace Acrux.Core.Dom;

public class CDataSection : TextNode
{
    public CDataSection(string data) : base(data)
    {
        NodeName = "#cdata-section";
    }

    public override NodeType NodeType => NodeType.CDataSection;
    public override string NodeName => "#cdata-section";

    protected override Node CloneNodeInternal(bool deep) => new CDataSection(Data);
}
