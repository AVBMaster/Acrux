using System.Text;

namespace Acrux.Core.Css.Values;

public enum CssValueListSeparator
{
    Space, Comma, Slash
}

/// <summary>A list of CSS values, mirroring Blink's CSSValueList.</summary>
public class CssValueList : CssValue
{
    public List<CssValue> Values { get; } = new();
    public CssValueListSeparator Separator { get; }

    public CssValueList() : base(CssClassType.ValueList)
    {
        Separator = CssValueListSeparator.Space;
    }

    public CssValueList(CssValueListSeparator separator) : base(CssClassType.ValueList)
    {
        Separator = separator;
    }

    public override bool IsBaseValueList => true;
    public int Length => Values.Count;

    public void Append(CssValue value) => Values.Add(value);

    public CssValue? First => Values.Count > 0 ? Values[0] : null;
    public CssValue? Last => Values.Count > 0 ? Values[^1] : null;

    public override string CssText()
    {
        if (Values.Count == 0) return "";
        string sep = Separator switch
        {
            CssValueListSeparator.Comma => ", ",
            CssValueListSeparator.Slash => " / ",
            _ => " "
        };
        var sb = new StringBuilder();
        for (int i = 0; i < Values.Count; i++)
        {
            if (i > 0) sb.Append(sep);
            sb.Append(Values[i].CssText());
        }
        return sb.ToString();
    }

    public override bool Equals(CssValue other)
    {
        if (other is not CssValueList list || list.Separator != Separator || list.Values.Count != Values.Count)
            return false;
        for (int i = 0; i < Values.Count; i++)
        {
            if (!Values[i].Equals(list.Values[i]))
                return false;
        }
        return true;
    }
}

/// <summary>A function value like calc(), var(), etc. Contains the function name + argument values.</summary>
public sealed class CssFunctionValue : CssValueList
{
    public string Name { get; }

    public CssFunctionValue(string name) : base(CssValueListSeparator.Space)
    {
        Name = name;
    }

    public CssFunctionValue(string name, CssValueListSeparator separator) : base(separator)
    {
        Name = name;
    }

    public override string CssText() => $"{Name}({base.CssText()})";
}