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
}
