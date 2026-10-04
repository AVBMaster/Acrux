namespace Acrux.Core.Layout;

// One visual line of wrapped text. Start/Length are offsets into the original
// string; a hard newline (\r\n or \n) does NOT belong to any visual line.
public readonly struct TextLine
{
    public int Start { get; }
    public int Length { get; }

    public TextLine(int start, int length)
    {
        Start = start;
        Length = length;
    }
}

public static class TextWrapHelper
{
    // Splits text into visual lines: honors \n/\r\n as hard breaks and soft-wraps
    // when a line would exceed maxWidth. Used by textarea rendering and caret
    // hit-testing so both sides map flat char indices to visual lines identically.
    public static List<TextLine> WrapToLines(string text, string fontFamily, float fontSize, float maxWidth)
    {
        var result = new List<TextLine>();
        if (string.IsNullOrEmpty(text) || maxWidth <= 0)
        {
            if (text != null && text.Length > 0)
            {
                int start = 0;
                for (int i = 0; i < text.Length; i++)
                {
                    if (text[i] == '\r' || text[i] == '\n')
                    {
                        result.Add(new TextLine(start, i - start));
                        if (text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                        start = i + 1;
                    }
                }
                if (start < text.Length)
                    result.Add(new TextLine(start, text.Length - start));
            }
            return result;
        }

        float width = 0;
        int lineStart = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\r' || c == '\n')
            {
                result.Add(new TextLine(lineStart, i - lineStart));
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                lineStart = i + 1;
                width = 0;
                continue;
            }
            float cw = MeasureChar(c, fontFamily, fontSize);
            if (width + cw > maxWidth && width > 0)
            {
                result.Add(new TextLine(lineStart, i - lineStart));
                lineStart = i;
                width = 0;
            }
            width += cw;
        }
        if (lineStart < text.Length)
            result.Add(new TextLine(lineStart, text.Length - lineStart));
        return result;
    }

    // Maps a flat character index to a (lineIndex, column) pair using the same
    // visual-line model as WrapToLines. The caret is allowed to sit at the very
    // end of a visual line; positions after a hard newline belong to the next line.
    public static (int line, int column) GetLineColumn(List<TextLine> lines, int flatIndex)
    {
        if (lines.Count == 0) return (0, 0);
        for (int i = 0; i < lines.Count; i++)
        {
            var ln = lines[i];
            if (flatIndex < ln.Start + ln.Length)
                return (i, Math.Max(0, flatIndex - ln.Start));
        }
        // Past the end of all lines: end of the last line.
        var last = lines[^1];
        return (lines.Count - 1, last.Length);
    }

    // Maps a (lineIndex, column) pair back to a flat character index.
    public static int GetFlatIndex(List<TextLine> lines, int line, int column)
    {
        if (line < 0) return 0;
        if (line >= lines.Count)
        {
            if (lines.Count == 0) return 0;
            var last = lines[^1];
            return last.Start + last.Length;
        }
        var ln = lines[line];
        return Math.Min(ln.Start + ln.Length, ln.Start + Math.Max(0, column));
    }

    private static float MeasureChar(char c, string fontFamily, float fontSize)
    {
        if (TextMeasurer.Instance != null)
            return TextMeasurer.Instance.MeasureText(c.ToString(), fontFamily ?? "Arial", fontSize);
        return fontSize * 0.55f;
    }
}
