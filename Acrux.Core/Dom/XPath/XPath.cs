namespace Acrux.Core.Dom.XPath;

public class XPathResult
{
    public XPathResultType ResultType { get; }
    public XPathResultType ResultTypeValue => ResultType;
    public double NumberValue { get; }
    public string StringValue { get; } = "";
    public bool BooleanValue { get; }
    public Node? SingleNodeValue { get; }
    public bool InvalidIteratorState { get; }
    public int SnapshotLength { get; }

    private readonly List<Node> _nodes = new();

    public XPathResult(XPathResultType type, List<Node>? nodes = null)
    {
        ResultType = type;
        _nodes = nodes ?? new();
        SnapshotLength = _nodes.Count;
        SingleNodeValue = _nodes.FirstOrDefault();
        BooleanValue = _nodes.Count > 0;
        NumberValue = _nodes.Count;
    }

    public Node? IterateNext()
    {
        return null;
    }

    public Node? SnapshotItem(int index)
    {
        return index >= 0 && index < _nodes.Count ? _nodes[index] : null;
    }
}

public enum XPathResultType
{
    Any = 0,
    Number = 1,
    String = 2,
    Boolean = 3,
    UnorderedNodeIterator = 4,
    OrderedNodeIterator = 5,
    UnorderedNodeSnapshot = 6,
    OrderedNodeSnapshot = 7,
    AnyOrderedNodeType = 8,
    FirstOrderedNodeType = 9
}

public static class XPathEvaluator
{
    public static XPathResult Evaluate(string expression, Node contextNode,
        object? resolver = null, XPathResultType type = XPathResultType.Any, XPathResult? result = null)
    {
        return new XPathResult(XPathResultType.Any);
    }

    public static XPathExpression CreateExpression(string expression, object? resolver = null)
    {
        return new XPathExpression(expression);
    }
}

public class XPathExpression
{
    public string Expression { get; }

    public XPathExpression(string expression)
    {
        Expression = expression;
    }

    public XPathResult Evaluate(Node contextNode, XPathResultType type = XPathResultType.Any, XPathResult? result = null)
    {
        return XPathEvaluator.Evaluate(Expression, contextNode, null, type, result);
    }
}

public class XPathNamespace
{
    public string Prefix { get; }
    public string NamespaceUri { get; }

    public XPathNamespace(string prefix, string namespaceUri)
    {
        Prefix = prefix;
        NamespaceUri = namespaceUri;
    }
}
