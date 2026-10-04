using Acrux.Core.Dom;

namespace Acrux.Core.Layout;

/// <summary>
/// Represents the text of a CSS counter. Mirrors layout_counter.cc.
/// Counters are always generated content ("content: counter(a)") and anonymous.
/// </summary>
public class LayoutCounter
{
    public string Identifier { get; }
    public string ListStyle { get; }
    public string Separator { get; }
    public List<int> Values { get; private set; } = new();

    public LayoutCounter(string identifier, string listStyle, string separator = "")
    {
        Identifier = identifier;
        ListStyle = listStyle;
        Separator = separator;
    }

    public void UpdateValues(List<int> values)
    {
        Values = values;
    }

    public string RenderText()
    {
        if (Values.Count == 0) return "0";
        return string.Join(Separator, Values.Select(v => FormatCounterValue(v, ListStyle)));
    }

    public bool IsDirectionalSymbolMarker =>
        ListStyle == "disclosure-open" || ListStyle == "disclosure-closed";

    /// <summary>A counter style name rendered through the engine's one counter table.
    /// The suffix a list marker adds is not part of a counter representation, so a name
    /// resolves to its digits or letters only (CSS Lists 3 §4.1).</summary>
    public static string FormatCounterValue(int value, string listStyle) =>
        List.ListMarkerFormatter.CounterRepresentation(listStyle, value, null);

    public static string ListStyleName(LayoutCounter? counter, ComputedStyle style)
    {
        if (counter != null)
            return counter.ListStyle;
        var listStyle = style.ListStyleType;
        return listStyle switch
        {
            ListStyleType.Decimal => "decimal",
            ListStyleType.DecimalLeadingZero => "decimal-leading-zero",
            ListStyleType.LowerRoman => "lower-roman",
            ListStyleType.UpperRoman => "upper-roman",
            ListStyleType.LowerAlpha => "lower-alpha",
            ListStyleType.UpperAlpha => "upper-alpha",
            ListStyleType.Disc => "disc",
            ListStyleType.Circle => "circle",
            ListStyleType.Square => "square",
            _ => "decimal"
        };
    }
}

/// <summary>
/// CSS counter management. Mirrors the counter system in layout_counter.cc.
/// </summary>
public class CounterManager
{
    private readonly Dictionary<string, int> _counters = new();

    public void Reset(string name, int value = 0)
    {
        _counters[name] = value;
    }

    public void Increment(string name, int delta = 1)
    {
        if (!_counters.ContainsKey(name))
            _counters[name] = 0;
        _counters[name] += delta;
    }

    public int GetValue(string name)
    {
        return _counters.GetValueOrDefault(name, 0);
    }

    public void SetValue(string name, int value)
    {
        _counters[name] = value;
    }

    public void PushScope()
    {
        // Each scope resets counters to previous values
    }

    public void PopScope()
    {
        // Restore counter values to previous scope
    }

    public void Clear()
    {
        _counters.Clear();
    }
}