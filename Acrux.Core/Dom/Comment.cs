namespace Acrux.Core.Dom;

public class CommentNode : CharacterData
{
    public CommentNode(string data) : base(data, "#comment")
    {
    }

    public override NodeType NodeType => NodeType.Comment;
    public override string NodeName => "#comment";

    protected override Node CloneNodeInternal(bool deep) => new CommentNode(Data);
}
