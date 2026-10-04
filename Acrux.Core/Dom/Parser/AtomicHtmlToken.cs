namespace Acrux.Core.Dom.Parser;

internal class AtomicHtmlToken
{
    public HtmlToken.TokenType Type { get; }
    public string Name { get; }
    public bool SelfClosing { get; }
    public bool ForceQuirks { get; }
    public string Data { get; }
    public string Comment { get; }
    public List<char> PublicIdentifier { get; }
    public List<char> SystemIdentifier { get; }
    public List<HtmlToken.Attribute> Attributes { get; }

    public AtomicHtmlToken(HtmlToken token)
    {
        Type = token.Type;
        switch (Type)
        {
            case HtmlToken.TokenType.StartTag:
            case HtmlToken.TokenType.EndTag:
                SelfClosing = token.SelfClosing;
                Name = token.GetName();
                Attributes = new List<HtmlToken.Attribute>(token.Attributes);
                break;
            case HtmlToken.TokenType.Doctype:
                Name = token.GetName();
                ForceQuirks = token.ForceQuirks;
                PublicIdentifier = new List<char>(token.PublicIdentifier);
                SystemIdentifier = new List<char>(token.SystemIdentifier);
                Attributes = new List<HtmlToken.Attribute>();
                break;
            case HtmlToken.TokenType.Character:
                Data = token.Characters;
                Attributes = new List<HtmlToken.Attribute>();
                break;
            case HtmlToken.TokenType.Comment:
                Comment = token.Comment;
                Attributes = new List<HtmlToken.Attribute>();
                break;
            case HtmlToken.TokenType.EndOfFile:
                Attributes = new List<HtmlToken.Attribute>();
                break;
            default:
                Attributes = new List<HtmlToken.Attribute>();
                break;
        }
    }

    public AtomicHtmlToken(HtmlToken.TokenType type, string name, List<HtmlToken.Attribute>? attributes = null)
    {
        Type = type;
        Name = name;
        Attributes = attributes ?? new List<HtmlToken.Attribute>();
        Data = string.Empty;
        Comment = string.Empty;
        PublicIdentifier = new List<char>();
        SystemIdentifier = new List<char>();
    }

    public AtomicHtmlToken(HtmlToken.TokenType type, string name)
    {
        Type = type;
        Name = name;
        Attributes = new List<HtmlToken.Attribute>();
        Data = string.Empty;
        Comment = string.Empty;
        PublicIdentifier = new List<char>();
        SystemIdentifier = new List<char>();
    }

    public string GetName() => Name;

    public bool UsesName => Type == HtmlToken.TokenType.StartTag || Type == HtmlToken.TokenType.EndTag || Type == HtmlToken.TokenType.Doctype;
    public bool UsesAttributes => Type == HtmlToken.TokenType.StartTag || Type == HtmlToken.TokenType.EndTag;
}