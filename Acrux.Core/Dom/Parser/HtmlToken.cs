using System.Text;

namespace Acrux.Core.Dom.Parser;

internal class HtmlToken
{
    public enum TokenType
    {
        Uninitialized,
        Doctype,
        StartTag,
        EndTag,
        Comment,
        Character,
        EndOfFile,
    }

    public class Attribute
    {
        public StringBuilder Name { get; } = new();
        public StringBuilder Value { get; } = new();

        public string GetName() => Name.ToString();
        public string GetValue() => Value.ToString();
        public bool NameIsEmpty => Name.Length == 0;

        public void AppendToName(char c) => Name.Append(c);
        public void AppendToValue(char c) => Value.Append(c);
        public void ClearValue() => Value.Clear();
    }

    public class DoctypeData
    {
        public bool HasPublicIdentifier;
        public bool HasSystemIdentifier;
        public List<char> PublicIdentifier { get; } = new();
        public List<char> SystemIdentifier { get; } = new();
        public bool ForceQuirks;
    }

    public TokenType Type { get; private set; } = TokenType.Uninitialized;
    public StringBuilder Data { get; } = new();
    public List<Attribute> Attributes { get; } = new();
    public Attribute? CurrentAttribute { get; private set; }
    public DoctypeData? DoctypeDataValue { get; private set; }
    public bool SelfClosing { get; private set; }

    public bool IsUninitialized => Type == TokenType.Uninitialized;
    public bool IsAll8BitData => true;

    public void Clear()
    {
        if (Type == TokenType.Uninitialized)
            return;
        Type = TokenType.Uninitialized;
        Data.Clear();
        if (CurrentAttribute != null)
        {
            CurrentAttribute = null;
            Attributes.Clear();
        }
    }

    public void MakeEndOfFile()
    {
        Type = TokenType.EndOfFile;
    }

    public string GetName() => Data.ToString();

    public void AppendToName(char character)
    {
        Data.Append(character);
    }

    public void AppendToName(string s)
    {
        Data.Append(s);
    }

    public bool ForceQuirks => DoctypeDataValue?.ForceQuirks ?? false;

    public void SetForceQuirks()
    {
        if (DoctypeDataValue != null)
            DoctypeDataValue.ForceQuirks = true;
    }

    public void BeginDoctype()
    {
        Type = TokenType.Doctype;
        DoctypeDataValue = new DoctypeData();
    }

    public void BeginDoctype(char character)
    {
        BeginDoctype();
        Data.Append(character);
    }

    public List<char> PublicIdentifier => DoctypeDataValue?.PublicIdentifier ?? new List<char>();
    public List<char> SystemIdentifier => DoctypeDataValue?.SystemIdentifier ?? new List<char>();

    public void SetPublicIdentifierToEmptyString()
    {
        if (DoctypeDataValue != null)
        {
            DoctypeDataValue.HasPublicIdentifier = true;
            DoctypeDataValue.PublicIdentifier.Clear();
        }
    }

    public void SetSystemIdentifierToEmptyString()
    {
        if (DoctypeDataValue != null)
        {
            DoctypeDataValue.HasSystemIdentifier = true;
            DoctypeDataValue.SystemIdentifier.Clear();
        }
    }

    public void AppendToPublicIdentifier(char character)
    {
        DoctypeDataValue?.PublicIdentifier.Add(character);
    }

    public void AppendToSystemIdentifier(char character)
    {
        DoctypeDataValue?.SystemIdentifier.Add(character);
    }

    public void SetSelfClosing()
    {
        SelfClosing = true;
    }

    public void BeginStartTag(char character)
    {
        Type = TokenType.StartTag;
        SelfClosing = false;
        Data.Append(character);
    }

    public void BeginEndTag(char character)
    {
        Type = TokenType.EndTag;
        SelfClosing = false;
        Data.Append(character);
    }

    public void BeginEndTag(string characters)
    {
        Type = TokenType.EndTag;
        SelfClosing = false;
        Data.Append(characters);
    }

    public void AddNewAttribute(char character)
    {
        var attr = new Attribute();
        attr.AppendToName(character);
        Attributes.Add(attr);
        CurrentAttribute = attr;
    }

    public void AppendToAttributeName(char character)
    {
        CurrentAttribute?.AppendToName(character);
    }

    public void AppendToAttributeValue(char character)
    {
        CurrentAttribute?.AppendToValue(character);
    }

    public void EnsureIsCharacterToken()
    {
        if (Type == TokenType.Uninitialized)
            Type = TokenType.Character;
    }

    public string Characters => Data.ToString();

    public void AppendToCharacter(char character)
    {
        Data.Append(character);
    }

    public void AppendToCharacter(string characters)
    {
        Data.Append(characters);
    }

    public string Comment => Data.ToString();

    public void BeginComment()
    {
        Type = TokenType.Comment;
    }

    public void AppendToComment(char character)
    {
        Data.Append(character);
    }
}