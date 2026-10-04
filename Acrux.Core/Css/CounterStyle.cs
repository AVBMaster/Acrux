using System.Globalization;
using Acrux.Core.Css.Rules;
using Acrux.Core.Dom;
using Acrux.Core.Layout.List;

namespace Acrux.Core.Css;

/// <summary>The 'system' descriptor of @counter-style (CSS Counter Styles §4.1.1).</summary>
public enum CounterStyleSystemKind
{
    /// <summary>No 'system' was declared: the style generates with whatever it extends.</summary>
    Extends,
    Cyclic,
    Numeric,
    Alphabetic,
    Symbolic,
    Additive,
    Fixed,
}

/// <summary>
/// A counter style that a marker or a counter() call can render with: either a
/// predefined style, whose table lives in <see cref="ListMarkerFormatter"/> and which
/// this class only wraps so 'extends' and 'fallback' can name it, or a style the
/// document declared with @counter-style.
/// A descriptor the rule leaves out comes from the style it extends, and extends is a
/// chain, so the values are read through accessors that walk it (§4.1.1).
/// </summary>
public sealed class CounterStyle
{
    /// <summary>Engines cap the length of a representation so a pathological symbol list
    /// cannot turn one counter into megabytes of text.</summary>
    private const int LengthLimit = 100;

    private readonly ListStyleType? _preset;

    private List<string>? _symbols;
    private List<(int Weight, string Symbol)>? _additiveSymbols;
    private List<(int Low, int High)>? _ranges;
    private bool _rangeIsAuto;
    private string? _prefix;
    private string? _suffix;
    private int? _padLength;
    private string? _padSymbol;
    private string? _negativePrefix;
    private string? _negativeSuffix;
    private int? _firstSymbolValue;

    private CounterStyle(string name, ListStyleType? preset)
    {
        Name = name;
        _preset = preset;
    }

    public string Name { get; }

    /// <summary>True for a style that cannot generate at all - a system with no table, an
    /// 'extends' of a name the document never declared, a cycle, or symbol images this
    /// engine does not paint. Such a name behaves as if it were never declared (§3.2).</summary>
    public bool IsInvalid { get; private set; }

    public bool IsPreset => _preset.HasValue;

    public CounterStyleSystemKind System { get; private set; } = CounterStyleSystemKind.Extends;
    public string? ExtendsName { get; private set; }
    public string? FallbackName { get; private set; }
    public CounterStyle? ExtendsStyle { get; private set; }
    public CounterStyle? FallbackStyle { get; private set; }

    private IReadOnlyList<string> Symbols =>
        _symbols is { Count: > 0 } ? _symbols : ExtendsStyle?.Symbols ?? Array.Empty<string>();

    private IReadOnlyList<(int Weight, string Symbol)> AdditiveSymbols =>
        _additiveSymbols is { Count: > 0 } ? _additiveSymbols
            : ExtendsStyle?.AdditiveSymbols ?? Array.Empty<(int, string)>();

    private IReadOnlyList<(int Low, int High)> Ranges =>
        _rangeIsAuto ? Array.Empty<(int, int)>()
            : _ranges is { Count: > 0 } ? _ranges : ExtendsStyle?.Ranges ?? Array.Empty<(int, int)>();

    private string PrefixText => _prefix ?? ExtendsStyle?.PrefixText ?? string.Empty;
    private string SuffixText => _suffix ?? ExtendsStyle?.SuffixText ?? ". ";
    private int Pad => _padLength ?? ExtendsStyle?.Pad ?? 0;
    private string PadGlyph => _padSymbol ?? ExtendsStyle?.PadGlyph ?? "0";
    private string NegativeStart => _negativePrefix ?? ExtendsStyle?.NegativeStart ?? "-";
    private string NegativeEnd => _negativeSuffix ?? ExtendsStyle?.NegativeEnd ?? string.Empty;
    private int SymbolStart => _firstSymbolValue ?? ExtendsStyle?.SymbolStart ?? 1;

    /// <summary>The style whose system does the generating: this one, or, when it declares
    /// no system, the nearest parent that does.</summary>
    private CounterStyle Generator =>
        System != CounterStyleSystemKind.Extends || ExtendsStyle == null ? this : ExtendsStyle.Generator;

    /// <summary>The punctuation a list marker appends after the representation.
    /// counter() in generated content leaves both the prefix and the suffix off.</summary>
    public string Suffix => SuffixText;

    /// <summary>The whole marker text for a value.</summary>
    public string MarkerLabel(int value) => PrefixText + Representation(value) + Suffix;

    /// <summary>A predefined style addressed by its keyword, so 'system: extends' and
    /// 'fallback' can name one. Its suffix is the punctuation the preset table already
    /// prints with, which a child may override. Null for a name that is not a style.</summary>
    public static CounterStyle? ForKeyword(string name)
    {
        if (Resolver.CssPropertyApplier.MatchListStyleType(name) is not { } type)
            return null;
        return new CounterStyle(name.ToLowerInvariant(), type)
        {
            _suffix = ListMarkerFormatter.MarkerSuffix(type),
        };
    }

    /// <summary>The counter representation of a value, without prefix or suffix
    /// (CSS Counter Styles §3).</summary>
    public string Representation(int value)
    {
        if (Generator._preset is { } preset)
            return ListMarkerFormatter.Representation(preset, value);

        if (IsInvalid)
            return Decimal(value);

        string? core = RangeContains(value) ? Generator.InitialRepresentation(value) : null;
        if (string.IsNullOrEmpty(core))
            return FallbackStyle?.Representation(value) ?? Decimal(value);

        if (NeedsNegativeSign(value))
            core = NegativeStart + core + NegativeEnd;

        // 'pad' counts grapheme clusters of the text the marker shows, the negative
        // sign included (§4.1.5).
        if (Pad > core.Length && Pad <= LengthLimit)
            core = string.Concat(Enumerable.Repeat(PadGlyph, Pad - core.Length)) + core;

        return core;
    }

    private static string Decimal(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static uint Magnitude(int value) =>
        value == int.MinValue ? 2147483648u : (uint)Math.Abs(value);

    private bool RangeContains(int value)
    {
        var ranges = Ranges;
        if (ranges.Count == 0)
        {
            // 'auto' depends on the system: only the systems that walk a symbol list
            // forever refuse zero and the negatives (§4.1.7).
            return Generator.System switch
            {
                CounterStyleSystemKind.Symbolic or CounterStyleSystemKind.Alphabetic => value >= 1,
                CounterStyleSystemKind.Additive => value >= 0,
                _ => true,
            };
        }

        foreach (var (low, high) in ranges)
            if (value >= low && value <= high)
                return true;
        return false;
    }

    /// <summary>cyclic and fixed address the symbol list by position, so a negative value
    /// is simply another position and takes no sign; the systems that spell out a
    /// number do (§3, step 4).</summary>
    private bool NeedsNegativeSign(int value) =>
        value < 0 && Generator.System is not (CounterStyleSystemKind.Cyclic or CounterStyleSystemKind.Fixed);

    private string? InitialRepresentation(int value)
    {
        uint abs = Magnitude(value);
        return System switch
        {
            CounterStyleSystemKind.Cyclic => Cyclic(value),
            CounterStyleSystemKind.Fixed => Fixed(value),
            CounterStyleSystemKind.Numeric => Numeric(abs),
            CounterStyleSystemKind.Symbolic => Symbolic(abs),
            CounterStyleSystemKind.Alphabetic => Alphabetic(abs),
            CounterStyleSystemKind.Additive => Additive(abs),
            _ => null,
        };
    }

    private string? Cyclic(int value)
    {
        var symbols = Symbols;
        if (symbols.Count == 0) return null;
        int index = value % symbols.Count - 1;
        if (index < 0) index += symbols.Count;
        return symbols[index];
    }

    private string? Fixed(int value)
    {
        var symbols = Symbols;
        int index = value - SymbolStart;
        return index < 0 || index >= symbols.Count ? null : symbols[index];
    }

    private string? Symbolic(uint value)
    {
        var symbols = Symbols;
        int n = symbols.Count;
        if (value == 0 || n == 0) return null;
        uint index = (value - 1) % (uint)n;
        uint repetitions = (value + (uint)n - 1) / (uint)n;
        if (repetitions > LengthLimit) return null;
        return string.Concat(Enumerable.Repeat(symbols[(int)index], (int)repetitions));
    }

    private string? Alphabetic(uint value)
    {
        var symbols = Symbols;
        int n = symbols.Count;
        if (value == 0 || n == 0) return null;
        var text = new List<string>();
        while (value > 0)
        {
            value -= 1;
            text.Add(symbols[(int)(value % (uint)n)]);
            value /= (uint)n;
        }
        text.Reverse();
        return string.Concat(text);
    }

    private string? Numeric(uint value)
    {
        var symbols = Symbols;
        int n = symbols.Count;
        // A positional system with a single symbol could never count past zero (§4.1.2).
        if (n < 2) return null;
        if (value == 0) return symbols[0];
        var text = new List<string>();
        while (value > 0)
        {
            text.Add(symbols[(int)(value % (uint)n)]);
            value /= (uint)n;
        }
        text.Reverse();
        return string.Concat(text);
    }

    private string? Additive(uint value)
    {
        var units = AdditiveSymbols;
        if (units.Count == 0) return null;
        if (value == 0)
            return units[^1].Weight == 0 ? units[^1].Symbol : null;

        var text = new List<string>();
        uint remaining = value;
        foreach (var (weight, symbol) in units)
        {
            if (weight <= 0) break;
            uint copies = remaining / (uint)weight;
            if (copies > 0)
            {
                if (text.Count + copies > LengthLimit) return null;
                for (uint i = 0; i < copies; i++) text.Add(symbol);
            }
            remaining %= (uint)weight;
            if (remaining == 0) break;
        }
        return remaining == 0 ? string.Concat(text) : null;
    }

    // ==================== descriptor parsing ====================

    /// <summary>Builds a style from one @counter-style block. A descriptor whose value the
    /// grammar rejects is dropped on its own and the rest of the rule still applies - the
    /// same way a declaration the cascade cannot parse behaves, and measured against
    /// Edge. Only a rule left without the symbol table its system needs is unusable.</summary>
    public static CounterStyle? Parse(StyleRuleCounterStyle rule)
    {
        if (!Resolver.CssPropertyApplier.IsCounterStyleName(rule.Name))
            return null;

        var style = new CounterStyle(rule.Name, null);
        var descriptors = rule.Descriptors;

        if (descriptors.TryGetValue("system", out string? systemText))
            ApplySystem(style, systemText);

        if (descriptors.TryGetValue("fallback", out string? fallbackText))
        {
            var parts = Tokenize(fallbackText);
            if (parts.Count == 1 && Resolver.CssPropertyApplier.IsCounterStyleName(parts[0]))
                style.FallbackName = parts[0];
        }

        if (descriptors.TryGetValue("symbols", out string? symbolsText) &&
            TrySymbolList(symbolsText, out List<string>? symbols))
            style._symbols = symbols;

        if (descriptors.TryGetValue("additive-symbols", out string? additiveText) &&
            TryAdditiveSymbols(additiveText, out List<(int Weight, string Symbol)>? units))
        {
            // The greedy search needs the largest weight first (§4.1.2).
            units.Sort((a, b) => b.Weight.CompareTo(a.Weight));
            style._additiveSymbols = units;
        }

        if (descriptors.TryGetValue("negative", out string? negativeText))
        {
            var parts = Tokenize(negativeText);
            string? start = null, end = null;
            bool negativeOk = parts.Count is 1 or 2 &&
                TrySymbol(parts[0], out start) && start != null &&
                (parts.Count == 1 || (TrySymbol(parts[1], out end) && end != null));
            if (negativeOk)
            {
                style._negativePrefix = start!;
                style._negativeSuffix = parts.Count == 2 ? end! : string.Empty;
            }
        }

        if (descriptors.TryGetValue("pad", out string? padText))
        {
            var parts = Tokenize(padText);
            string? glyph = null;
            int pad = 0;
            bool padOk = parts.Count is 1 or 2 && TryInt(parts[0], out pad) && pad >= 0 &&
                (parts.Count == 1 || (TrySymbol(parts[1], out glyph) && glyph != null));
            if (padOk)
            {
                style._padLength = pad;
                // With no symbol the padding adds nothing: measured against Edge, a bare
                // 'pad: 4' leaves the representation exactly as long as it is (§4.1.5).
                style._padSymbol = parts.Count == 2 ? glyph! : string.Empty;
            }
        }

        if (descriptors.TryGetValue("range", out string? rangeText))
            ApplyRange(style, rangeText);

        if (descriptors.TryGetValue("prefix", out string? prefixText) &&
            TrySingleSymbol(prefixText, out string? prefix))
            style._prefix = prefix;

        if (descriptors.TryGetValue("suffix", out string? suffixText) &&
            TrySingleSymbol(suffixText, out string? suffix))
            style._suffix = suffix;

        // 'system' defaults to cyclic, which also covers the rule whose value was rejected:
        // the descriptor is dropped, not the rule (measured against Edge, §4.1.1).
        if (style.System == CounterStyleSystemKind.Extends && style.ExtendsName == null)
            style.System = CounterStyleSystemKind.Cyclic;

        return style;
    }

    /// <summary>'system' is the one descriptor whose initial value gives a rule something
    /// to do: with no valid value the style still generates cyclically (§4.1.1).</summary>
    private static void ApplySystem(CounterStyle style, string text)
    {
        var tokens = Tokenize(text);
        switch (tokens.Count > 0 ? tokens[0].ToLowerInvariant() : "")
        {
            case "cyclic": style.System = CounterStyleSystemKind.Cyclic; break;
            case "numeric": style.System = CounterStyleSystemKind.Numeric; break;
            case "alphabetic": style.System = CounterStyleSystemKind.Alphabetic; break;
            case "symbolic": style.System = CounterStyleSystemKind.Symbolic; break;
            case "additive": style.System = CounterStyleSystemKind.Additive; break;
            case "fixed":
                style.System = CounterStyleSystemKind.Fixed;
                if (tokens.Count > 1 && TryInt(tokens[1], out int first))
                    style._firstSymbolValue = first;
                break;
            case "extends" when tokens.Count == 2 &&
                Resolver.CssPropertyApplier.IsCounterStyleName(tokens[1]):
                style.ExtendsName = tokens[1];
                break;
        }
    }

    /// <summary>'range' is a comma separated list of inclusive pairs; 'auto' asks for the
    /// per-system default and a pair whose bounds are reversed is not a range at all.</summary>
    private static void ApplyRange(CounterStyle style, string text)
    {
        if (Tokenize(text).SequenceEqual(["auto"], StringComparer.OrdinalIgnoreCase))
        {
            style._rangeIsAuto = true;
            return;
        }

        var bounds = new List<(int Low, int High)>();
        foreach (string item in SplitList(text, ','))
        {
            var parts = Tokenize(item);
            if (parts.Count is 0 or > 2 || !TryInt(parts[0], out int low))
                return;
            int high = low;
            if (parts.Count == 2)
            {
                if (parts[1].Equals("infinite", StringComparison.OrdinalIgnoreCase))
                    high = int.MaxValue;
                else if (!TryInt(parts[1], out high))
                    return;
            }
            if (low > high) return;
            bounds.Add((low, high));
        }
        if (bounds.Count > 0)
            style._ranges = bounds;
    }

    private static bool TrySymbolList(string text, out List<string> symbols)
    {
        symbols = new List<string>();
        foreach (string token in Tokenize(text))
        {
            // An image symbol cannot be written into a text label, so the whole list is
            // rejected rather than silently dropping one of its entries.
            if (!TrySymbol(token, out string? symbol) || symbol == null) return false;
            symbols.Add(symbol);
        }
        return symbols.Count > 0;
    }

    private static bool TryAdditiveSymbols(string text, out List<(int Weight, string Symbol)> units)
    {
        units = new List<(int Weight, string Symbol)>();
        // '<integer> && <symbol>' pairs, comma separated.
        foreach (string item in SplitList(text, ','))
        {
            var parts = Tokenize(item);
            if (parts.Count != 2) return false;
            (string weightText, string symbolText) = TryInt(parts[0], out _)
                ? (parts[0], parts[1])
                : (parts[1], parts[0]);
            if (!TryInt(weightText, out int weight) || weight < 0 ||
                !TrySymbol(symbolText, out string? symbol) || symbol == null)
                return false;
            units.Add((weight, symbol));
        }
        return units.Count > 0;
    }

    private static bool TrySingleSymbol(string text, out string? symbol)
    {
        symbol = null;
        var parts = Tokenize(text);
        // 'suffix' takes one symbol; a list of them is not a value Edge accepts either.
        return parts.Count == 1 && TrySymbol(parts[0], out symbol) && symbol != null;
    }
    /// <summary>Links 'extends' and 'fallback' once every rule of the document is in,
    /// since either can name a style declared further down the sheet. Walking a parent
    /// first makes the order of the rules irrelevant.</summary>
    public void ResolveLinks(CounterStyleRegistry registry, HashSet<string>? visiting = null)
    {
        if (IsInvalid || IsPreset) return;

        visiting ??= new HashSet<string>(StringComparer.Ordinal);
        if (!visiting.Add(Name))
        {
            IsInvalid = true; // an extends chain that comes back on itself
            return;
        }

        if (ExtendsName != null)
        {
            CounterStyle? parent = registry.FindRaw(ExtendsName) ?? ForKeyword(ExtendsName);
            if (parent == null)
            {
                // Extending a name that is not a style leaves nothing to borrow (§4.1.1).
                IsInvalid = true;
                visiting.Remove(Name);
                return;
            }
            if (!parent.IsPreset)
                parent.ResolveLinks(registry, visiting);
            if (parent.IsInvalid)
                IsInvalid = true;
            else
                ExtendsStyle = parent;
            if (IsInvalid)
            {
                visiting.Remove(Name);
                return;
            }
        }

        FallbackStyle = ResolveFallback(registry);
        visiting.Remove(Name);
    }

    private CounterStyle? ResolveFallback(CounterStyleRegistry registry)
    {
        if (FallbackName == null)
            return ExtendsStyle?.FallbackStyle ?? ForKeyword("decimal");

        var seen = new HashSet<string>(StringComparer.Ordinal) { Name };
        string? step = FallbackName;
        while (step != null && seen.Add(step))
        {
            CounterStyle? target = registry.FindRaw(step) ?? ForKeyword(step);
            if (target == null) break; // an unknown fallback name resolves to decimal
            if (!target.IsPreset)
                target.ResolveLinks(registry);
            if (target.IsInvalid)
            {
                step = target.FallbackName;
                continue;
            }
            return ReferenceEquals(target, this) ? ForKeyword("decimal") : target;
        }

        return ForKeyword("decimal"); // including the cycle case, which has no other end
    }

    /// <summary>Run after the links are resolved: a system that needs a symbol table
    /// which neither the rule nor its parents supplies cannot generate either.</summary>
    public void Validate()
    {
        if (IsInvalid || Generator.IsPreset) return;
        switch (Generator.System)
        {
            case CounterStyleSystemKind.Additive:
                if (AdditiveSymbols.Count == 0) IsInvalid = true;
                break;
            case CounterStyleSystemKind.Extends:
                IsInvalid = true;
                break;
            default:
                if (Symbols.Count == 0) IsInvalid = true;
                break;
        }
    }

    // ==================== descriptor tokenising ====================

    private static bool TryInt(string text, out int value)
    {
        value = 0;
        string trimmed = text.Trim();
        if (trimmed.Length == 0) return false;
        int digits = 0;
        for (int i = 0; i < trimmed.Length; i++)
        {
            char c = trimmed[i];
            if (char.IsAsciiDigit(c))
            {
                digits++;
                continue;
            }
            if (i == 0 && (c == '-' || c == '+')) continue;
            // An integer descriptor never carries a unit; 'pad: 3em' is a bad value.
            return false;
        }
        return digits > 0 && int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>A &lt;symbol&gt; is a &lt;string&gt;, a single character, or an image.
    /// Images come back as a null symbol: an image marker is a different box model, and a
    /// counter that mixed one into its text would print the URL.</summary>
    private static bool TrySymbol(string token, out string? symbol)
    {
        symbol = null;
        token = token.Trim();
        if (token.Length == 0) return false;

        if (token[0] is '"' or '\'')
        {
            int close = IndexOfClosingQuote(token, token[0]);
            if (close < 0) return false;
            symbol = Resolver.CssPropertyApplier.UnescapeCssString(token[1..close]);
            return true;
        }

        if (token.StartsWith("url(", StringComparison.OrdinalIgnoreCase) ||
            token.StartsWith("image(", StringComparison.OrdinalIgnoreCase) ||
            token.StartsWith("paint(", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // A bare <character> is exactly one codepoint; anything else is not a symbol.
        if (token.Length == 1 && !char.IsWhiteSpace(token[0]))
        {
            symbol = token;
            return true;
        }
        return false;
    }

    private static int IndexOfClosingQuote(string text, char quote)
    {
        for (int i = 1; i < text.Length; i++)
        {
            if (text[i] == '\\') i++;
            else if (text[i] == quote) return i;
        }
        return -1;
    }

    /// <summary>Split a descriptor value on top-level whitespace and commas, keeping
    /// quoted strings and function calls in one piece.</summary>
    internal static List<string> Tokenize(string text)
    {
        var parts = new List<string>();
        int i = 0;
        while (i < text.Length)
        {
            while (i < text.Length && (char.IsWhiteSpace(text[i]) || text[i] == ',')) i++;
            if (i >= text.Length) break;

            int start = i;
            if (text[i] is '"' or '\'')
            {
                char quote = text[i++];
                while (i < text.Length && text[i] != quote)
                {
                    if (text[i] == '\\' && i + 1 < text.Length) i++;
                    i++;
                }
                i = Math.Min(i + 1, text.Length);
            }
            else
            {
                int depth = 0;
                while (i < text.Length)
                {
                    char c = text[i];
                    if (c is '(' or '[') depth++;
                    else if (c is ')' or ']') depth--;
                    else if (depth == 0 && (char.IsWhiteSpace(c) || c == ',')) break;
                    i++;
                }
            }
            parts.Add(text[start..i]);
        }
        return parts;
    }

    private static List<string> SplitList(string text, char delimiter)
    {
        var parts = new List<string>();
        int depth = 0;
        int start = 0;
        char quote = '\0';
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (quote != '\0')
            {
                if (c == '\\' && i + 1 < text.Length) i++;
                else if (c == quote) quote = '\0';
                continue;
            }
            switch (c)
            {
                case '"' or '\'':
                    quote = c;
                    break;
                case '(' or '[':
                    depth++;
                    break;
                case ')' or ']':
                    depth--;
                    break;
                default:
                    if (c == delimiter && depth == 0)
                    {
                        if (i > start) parts.Add(text[start..i]);
                        start = i + 1;
                    }
                    break;
            }
        }
        if (start < text.Length) parts.Add(text[start..]);
        return parts;
    }
}

/// <summary>The @counter-style rules of one document. Names are case-sensitive CSS
/// identifiers, so the lookup is ordinal; a predefined keyword never reaches it because
/// the cascade already maps that onto the preset table.</summary>
public sealed class CounterStyleRegistry
{
    private readonly Dictionary<string, CounterStyle> _styles = new(StringComparer.Ordinal);

    public int Count => _styles.Count;

    public void Clear() => _styles.Clear();

    /// <summary>A later rule of the same name replaces the earlier one, the way a
    /// redeclared custom property replaces the previous definition.</summary>
    public void Register(StyleRuleCounterStyle rule)
    {
        CounterStyle? style = CounterStyle.Parse(rule);
        if (style == null) return;
        _styles[style.Name] = style;
    }

    /// <summary>Resolves the 'extends' and 'fallback' chains, then rejects the styles left
    /// without a table. Twice, because a fallback can name a style whose own chain has not
    /// been walked yet on the first pass.</summary>
    public void Finish()
    {
        foreach (var style in _styles.Values)
            style.ResolveLinks(this);
        foreach (var style in _styles.Values)
            style.ResolveLinks(this);
        foreach (var style in _styles.Values)
            style.Validate();
    }

    /// <summary>The style a name selects, or null for a name the document never declared
    /// or declared unusably; the caller then renders as decimal (§3.2).</summary>
    public CounterStyle? Find(string? name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        var style = FindRaw(name);
        return style is { IsInvalid: true } ? null : style;
    }

    /// <summary>Lookup that also reports the broken styles, since 'extends' has to mark a
    /// child invalid when its parent is rather than silently losing the table.</summary>
    internal CounterStyle? FindRaw(string name)
    {
        if (_styles.TryGetValue(name, out var style)) return style;
        return CounterStyle.ForKeyword(name);
    }
}
