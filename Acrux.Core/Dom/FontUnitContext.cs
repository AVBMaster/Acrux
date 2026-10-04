namespace Acrux.Core.Dom;

/// <summary>
/// Font-relative units (ch/ex/cap/ic/lh and their root variants) resolve
/// against glyph metrics of the element's own font (CSS Values 4 §6.3), which
/// the size-only ToPixels signature cannot carry. Style resolution and layout
/// set this context around the code that converts lengths, so the conversion
/// can look the real metrics up through the font manager.
/// </summary>
public static class FontUnitContext
{
    [ThreadStatic]
    private static ComputedStyle? _current;

    public static ComputedStyle? Current => _current;

    public readonly struct Scope : IDisposable
    {
        private readonly ComputedStyle? _previous;
        internal Scope(ComputedStyle? style)
        {
            _previous = _current;
            _current = style;
        }
        public void Dispose() => _current = _previous;
    }

    public static Scope Use(ComputedStyle? style) => new(style);
}
