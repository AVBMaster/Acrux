namespace Acrux.Core.Dom;

public class ProcessingInstruction : CharacterData
{
    public string Target { get; }

    public ProcessingInstruction(string target, string data) : base(data, target)
    {
        Target = target;
        NodeName = target;
    }

    public override NodeType NodeType => NodeType.ProcessingInstruction;
    public override string NodeName => Target;

    protected override Node CloneNodeInternal(bool deep) => new ProcessingInstruction(Target, Data);
}
