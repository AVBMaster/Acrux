namespace Acrux.Core.JavaScript;

/// <summary>
/// The number coercions WebIDL performs on a value before the engine ever sees it, gathered in one
/// place because the refusal an engine writes has to quote the coerced number, not the spelling the
/// page used: measured, setStart with an offset of -1 is refused as "The offset 4294967295 is larger
/// than the node's length (2).", and an offset of 4294967296 is accepted as 0.
/// </summary>
internal static class WebIdL
{
    /// <summary>ToInteger (truncated toward zero) taken modulo 2^32, which is what an 'unsigned long'
    /// parameter or attribute holds. NaN and the infinities are 0, as the reference makes them.</summary>
    internal static double ToUnsignedLong(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value)) return 0;
        double integer = Math.Floor(Math.Abs(value)) * Math.Sign(value);
        double wrapped = integer % 4294967296d;
        return wrapped < 0 ? wrapped + 4294967296d : wrapped;
    }

    /// <summary>The same value as the digits a message quotes it with — an unsigned long never carries
    /// a fraction point, however the page spelled it.</summary>
    internal static string Format(double value)
        => value.ToString("0", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>ToNumber over what the engine hands a host: the interop layer keeps a number as a
    /// double and a string as a string, so a value that is neither is coerced the way JavaScript
    /// coerces it and a value it cannot read is NaN — the number every rule below refuses.</summary>
    internal static double ToNumber(object? value)
    {
        switch (value)
        {
            case null: return double.NaN;
            case double d: return d;
            case float f: return f;
            case bool b: return b ? 1 : 0;
            case string s: return Parse(s);
            case IConvertible c:
                try { return c.ToDouble(System.Globalization.CultureInfo.InvariantCulture); }
                catch (Exception) { return double.NaN; }
            default: return double.NaN;
        }
    }

    private static double Parse(string text)
    {
        var trimmed = text.Trim(White);
        if (trimmed.Length == 0) return 0;
        if (double.TryParse(trimmed, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var number)) return number;
        if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            && long.TryParse(trimmed.AsSpan(2), System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out var hex)) return hex;
        if (trimmed.Equals("Infinity", StringComparison.Ordinal)
            || trimmed.Equals("+Infinity", StringComparison.Ordinal)) return double.PositiveInfinity;
        if (trimmed.Equals("-Infinity", StringComparison.Ordinal)) return double.NegativeInfinity;
        return double.NaN;
    }

    private static readonly char[] White = { '\t', '\n', '\v', '\f', '\r', ' ', '\u00a0', '\ufeff' };

    /// <summary>Web IDL 'double' — the finite numbers, and the sentence the platform writes for one
    /// that is not. An absent argument is not refused here; that is the arity check, and it runs
    /// first.</summary>
    internal static double RequiredDouble(object? value, string method, string owner)
    {
        var number = ToNumber(value);
        if (double.IsFinite(number)) return number;
        throw NotAFloat(number, method, owner);
    }

    /// <summary>The same, limited to a range — the quality of an image encoder is a number between
    /// 0 and 1 and nothing else.</summary>
    internal static double RequiredDouble(object? value, double min, double max,
        string method, string owner)
    {
        var number = ToNumber(value);
        if (double.IsFinite(number) && number >= min && number <= max) return number;
        throw NotAFloat(number, method, owner);
    }

    private static Acrux.Core.Dom.DOMException NotAFloat(double number, string method, string owner)
        => new($"Failed to execute '{method}' on '{owner}': The value provided "
               + $"({Spell(number)}) is not a valid float.", "TypeError");

    /// <summary>How the reference writes the refused number back: the shortest digits that read again
    /// as the same value, and JavaScript's own spelling for the values no other number has.</summary>
    private static string Spell(double number) => number switch
    {
        double.PositiveInfinity => "Infinity",
        double.NegativeInfinity => "-Infinity",
        _ => number.ToString(System.Globalization.CultureInfo.InvariantCulture),
    };
}
