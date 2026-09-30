using SkiaSharp;
using UpBrowser.Core;
using UpBrowser.Platform;

namespace UpBrowser.Rendering.DevTools;

public class DevToolsSource : IImeSupport
{
    private string _html = "";
    private float _scrollOffset;
    private float _contentHeight;
    private string[] _lines = Array.Empty<string>();
    private float _viewHeight;
    private float _renderX, _renderY, _renderW, _renderH;

    private bool _thumbDragging;
    private float _thumbDragStartY;
    private float _thumbDragStartOffset;

    private bool _editing;
    private int _editLine;
    private int _editCol;
    private int _selAnchorCol = -1;
    private int _selAnchorLine = -1;
    private bool _showCursor = true;
    private DateTime _lastBlink = DateTime.Now;

    private bool _mouseDown;

    // IME composition state
    private string _imeCompositionString = "";

    public Action<string>? OnHtmlChanged;
    public Action<char>? OnImeChar;
    public Action? OnInputChanged;

    public bool IsEditing => _editing;
    public int EditLine => _editLine;
    public int EditCol => _editCol;
    public float ScrollOffset => _scrollOffset;

    private bool HasSelection => _selAnchorLine >= 0;

    private (int startLine, int startCol, int endLine, int endCol) SelRange
    {
        get
        {
            if (!HasSelection) return (_editLine, _editCol, _editLine, _editCol);
            if (_selAnchorLine == _editLine)
            {
                int a = Math.Min(_selAnchorCol, _editCol);
                int b = Math.Max(_selAnchorCol, _editCol);
                return (_editLine, a, _editLine, b);
            }
            if (_selAnchorLine < _editLine)
                return (_selAnchorLine, _selAnchorCol, _editLine, _editCol);
            return (_editLine, _editCol, _selAnchorLine, _selAnchorCol);
        }
    }

    private void ClearSelection() { _selAnchorCol = -1; _selAnchorLine = -1; }

    private bool DeleteSelectedText()
    {
        if (!HasSelection) return false;
        var (sl, sc, el, ec) = SelRange;
        if (sl == el)
        {
            string line = _lines[sl];
            _lines[sl] = line[..sc] + line[ec..];
            _editLine = sl;
            _editCol = sc;
        }
        else
        {
            string firstPart = _lines[sl][..sc];
            string lastPart = _lines[el][ec..];
            var list = _lines.ToList();
            int removeCount = el - sl + 1;
            list.RemoveRange(sl, removeCount);
            list.Insert(sl, firstPart + lastPart);
            _lines = list.ToArray();
            _editLine = sl;
            _editCol = sc;
        }
        ClearSelection();
        _html = string.Join('\n', _lines);
        OnHtmlChanged?.Invoke(_html);
        OnInputChanged?.Invoke();
        return true;
    }

    public void SetHtml(string html) { _html = html ?? ""; _lines = _html.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n'); }

    public bool HandleWheel(double delta)
    {
        float maxScroll = Math.Max(0, _contentHeight - _viewHeight + 18);
        _scrollOffset -= (float)delta * 3;
        _scrollOffset = Math.Max(0, Math.Min(_scrollOffset, maxScroll));
        return true;
    }

    public void SetScrollOffset(float offset)
    {
        float maxScroll = Math.Max(0, _contentHeight - _viewHeight + 18);
        _scrollOffset = Math.Max(0, Math.Min(offset, maxScroll));
    }

    public bool HandleThumbDragStart(float y)
    {
        if (_contentHeight <= _viewHeight) return false;
        float sh = _viewHeight * _viewHeight / Math.Max(1, _contentHeight);
        float maxScroll = Math.Max(0, _contentHeight - _viewHeight);
        if (maxScroll <= 0) return false;
        float sy = _renderY + (_scrollOffset / maxScroll) * (_viewHeight - sh);
        if (y >= sy && y <= sy + sh)
        {
            _thumbDragging = true;
            _thumbDragStartY = y;
            _thumbDragStartOffset = _scrollOffset;
            return true;
        }
        return false;
    }

    public bool HandleThumbDrag(float y)
    {
        if (!_thumbDragging) return false;
        float sh = _viewHeight * _viewHeight / Math.Max(1, _contentHeight);
        float maxScroll = Math.Max(0, _contentHeight - _viewHeight);
        if (maxScroll <= 0) return false;
        float delta = (y - _thumbDragStartY) / Math.Max(1, _viewHeight - sh) * maxScroll;
        _scrollOffset = Math.Max(0, Math.Min(maxScroll, _thumbDragStartOffset + delta));
        return true;
    }

    public void HandleThumbDragEnd() { _thumbDragging = false; }

    public bool HandleClick(float x, float y)
    {
        float localY = y - _renderY;
        int clickedLine = (int)((localY + _scrollOffset) / 18);
        if (clickedLine >= 0 && clickedLine < _lines.Length)
        {
            _editing = true;
            _editLine = clickedLine;
            ClearSelection();
            _mouseDown = true;

            float textStartX = _renderX + 50;
            float clickX = x - textStartX;
            string line = _lines[clickedLine];
            _editCol = 0;
            float currentX = 0;
            using var testPaint = new SKPaint { IsAntialias = true };
            using var testFont = FontHelper.CreateDevToolsFont(12);
            for (int i = 0; i < line.Length; i++)
            {
                float charWidth = testFont.MeasureText(line[i].ToString());
                if (currentX + charWidth / 2 >= clickX)
                    break;
                _editCol = i + 1;
                currentX += charWidth;
            }

            OnInputChanged?.Invoke();
            return true;
        }
        _editing = false;
        return false;
    }

    public void HandleImeChar(char c)
    {
        if (!_editing) return;
        if (_editLine < 0 || _editLine >= _lines.Length) return;

        DeleteSelectedText();

        InsertAtCursor(c.ToString());
    }

    public string GetSelectedText()
    {
        if (!_editing || !HasSelection) return "";
        var (sl, sc, el, ec) = SelRange;
        if (sl == el)
            return _lines[sl][sc..ec];
        var parts = new List<string>();
        parts.Add(_lines[sl][sc..]);
        for (int i = sl + 1; i < el; i++)
            parts.Add(_lines[i]);
        parts.Add(_lines[el][..ec]);
        return string.Join("\n", parts);
    }

    public void SelectAll()
    {
        if (!_editing || _lines.Length == 0) return;
        _selAnchorCol = 0;
        _selAnchorLine = 0;
        _editLine = _lines.Length - 1;
        _editCol = _lines[_editLine].Length;
    }

    public void HandleMouseMove(float x, float y)
    {
        if (!_mouseDown || !_editing) return;
        float localY = y - _renderY;
        int line = (int)((localY + _scrollOffset) / 18);
        if (line < 0) line = 0;
        if (line >= _lines.Length) line = _lines.Length - 1;

        if (!HasSelection)
        {
            _selAnchorCol = _editCol;
            _selAnchorLine = _editLine;
        }

        float textStartX = _renderX + 50;
        float clickX = x - textStartX;
        _editLine = line;
        _editCol = 0;
        float currentX = 0;
        using var testFont = FontHelper.CreateDevToolsFont(12);
        string lineText = _lines[line];
        for (int i = 0; i < lineText.Length; i++)
        {
            float charWidth = testFont.MeasureText(lineText[i].ToString());
            if (currentX + charWidth / 2 >= clickX)
                break;
            _editCol = i + 1;
            currentX += charWidth;
        }
        OnInputChanged?.Invoke();
    }

    public void HandleMouseUp() { _mouseDown = false; }

    private void InsertAtCursor(string text)
    {
        string line = _lines[_editLine];
        _lines[_editLine] = line[..Math.Min(_editCol, line.Length)] + text + line[Math.Min(_editCol, line.Length)..];
        _editCol += text.Length;
        _html = string.Join('\n', _lines);
        OnHtmlChanged?.Invoke(_html);
        OnInputChanged?.Invoke();
    }

    public bool HandleKeyPress(char keyChar, Key key, bool shift = false)
    {
        if (!_editing) return false;
        if (_editLine < 0 || _editLine >= _lines.Length) return false;

        switch (key)
        {
            case Key.Enter:
                {
                    DeleteSelectedText();
                    string line = _lines[_editLine];
                    string before = line[..Math.Min(_editCol, line.Length)];
                    string after = line[Math.Min(_editCol, line.Length)..];
                    _lines[_editLine] = before;
                    var newLines = _lines.ToList();
                    newLines.Insert(_editLine + 1, after);
                    _lines = newLines.ToArray();
                    _editLine++;
                    _editCol = 0;
                    _html = string.Join('\n', _lines);
                    OnHtmlChanged?.Invoke(_html);
                    return true;
                }

            case Key.Backspace:
                {
                    if (DeleteSelectedText()) { _html = string.Join('\n', _lines); OnHtmlChanged?.Invoke(_html); return true; }
                    if (_editCol > 0)
                    {
                        string line = _lines[_editLine];
                        _lines[_editLine] = line[..(_editCol - 1)] + line[_editCol..];
                        _editCol--;
                    }
                    else if (_editLine > 0)
                    {
                        string currentLine = _lines[_editLine];
                        _editCol = _lines[_editLine - 1].Length;
                        _lines[_editLine - 1] += currentLine;
                        var list = _lines.ToList();
                        list.RemoveAt(_editLine);
                        _lines = list.ToArray();
                        _editLine--;
                    }
                    _html = string.Join('\n', _lines);
                    OnHtmlChanged?.Invoke(_html);
                    return true;
                }

            case Key.Delete:
                {
                    if (DeleteSelectedText()) return true;
                    string line = _lines[_editLine];
                    if (_editCol < line.Length)
                        _lines[_editLine] = line[.._editCol] + line[(_editCol + 1)..];
                    else if (_editLine < _lines.Length - 1)
                    {
                        string nextLine = _lines[_editLine + 1];
                        _lines[_editLine] += nextLine;
                        var list = _lines.ToList();
                        list.RemoveAt(_editLine + 1);
                        _lines = list.ToArray();
                    }
                    _html = string.Join('\n', _lines);
                    OnHtmlChanged?.Invoke(_html);
                    return true;
                }

            case Key.Left:
                if (shift)
                {
                    if (!HasSelection) { _selAnchorCol = _editCol; _selAnchorLine = _editLine; }
                    if (_editCol > 0) _editCol--;
                    else if (_editLine > 0) { _editLine--; _editCol = _lines[_editLine].Length; }
                }
                else
                {
                    ClearSelection();
                    if (_editCol > 0) _editCol--;
                    else if (_editLine > 0) { _editLine--; _editCol = _lines[_editLine].Length; }
                }
                return true;

            case Key.Right:
                if (shift)
                {
                    if (!HasSelection) { _selAnchorCol = _editCol; _selAnchorLine = _editLine; }
                    if (_editCol < _lines[_editLine].Length) _editCol++;
                    else if (_editLine < _lines.Length - 1) { _editLine++; _editCol = 0; }
                }
                else
                {
                    ClearSelection();
                    if (_editCol < _lines[_editLine].Length) _editCol++;
                    else if (_editLine < _lines.Length - 1) { _editLine++; _editCol = 0; }
                }
                return true;

            case Key.Up:
                if (shift)
                {
                    if (!HasSelection) { _selAnchorCol = _editCol; _selAnchorLine = _editLine; }
                    if (_editLine > 0) { _editLine--; _editCol = Math.Min(_editCol, _lines[_editLine].Length); }
                }
                else
                {
                    ClearSelection();
                    if (_editLine > 0) { _editLine--; _editCol = Math.Min(_editCol, _lines[_editLine].Length); }
                }
                return true;

            case Key.Down:
                if (shift)
                {
                    if (!HasSelection) { _selAnchorCol = _editCol; _selAnchorLine = _editLine; }
                    if (_editLine < _lines.Length - 1) { _editLine++; _editCol = Math.Min(_editCol, _lines[_editLine].Length); }
                }
                else
                {
                    ClearSelection();
                    if (_editLine < _lines.Length - 1) { _editLine++; _editCol = Math.Min(_editCol, _lines[_editLine].Length); }
                }
                return true;

            case Key.Home:
                if (shift)
                {
                    if (!HasSelection) { _selAnchorCol = _editCol; _selAnchorLine = _editLine; }
                    _editCol = 0;
                }
                else
                {
                    ClearSelection();
                    _editCol = 0;
                }
                return true;

            case Key.End:
                if (shift)
                {
                    if (!HasSelection) { _selAnchorCol = _editCol; _selAnchorLine = _editLine; }
                    _editCol = _lines[_editLine].Length;
                }
                else
                {
                    ClearSelection();
                    _editCol = _lines[_editLine].Length;
                }
                return true;

            case Key.Tab:
                {
                    DeleteSelectedText();
                    string line = _lines[_editLine];
                    _lines[_editLine] = line[..Math.Min(_editCol, line.Length)] + "    " + line[Math.Min(_editCol, line.Length)..];
                    _editCol += 4;
                    _html = string.Join('\n', _lines);
                    OnHtmlChanged?.Invoke(_html);
                    return true;
                }

            default:
                if (!char.IsControl(keyChar))
                {
                    DeleteSelectedText();
                    string curLine = _lines[_editLine];
                    _lines[_editLine] = curLine[..Math.Min(_editCol, curLine.Length)] + keyChar + curLine[Math.Min(_editCol, curLine.Length)..];
                    _editCol++;
                    _html = string.Join('\n', _lines);
                    OnHtmlChanged?.Invoke(_html);
                }
                return true;
        }
    }

    public bool TickCursorBlink()
    {
        if (!_editing) return false;
        if ((DateTime.Now - _lastBlink).TotalMilliseconds > 500)
        {
            _showCursor = !_showCursor;
            _lastBlink = DateTime.Now;
            return true;
        }
        return false;
    }

    private bool _skiaAvailable = true;
    private SKFont? _cachedSkFont;
    private SKPaint? _cachedFont;

    public void Render(SKCanvas canvas, float x, float y, float width, float height, DevToolsTheme theme)
    {
        if (!_skiaAvailable)
        {
            Console.WriteLine("[DevToolsSource.Render] SKPaint/SKFont unavailable on this OS, skipping render");
            return;
        }

        _renderX = x; _renderY = y; _renderW = width; _renderH = height;
        _viewHeight = height;

        try
        {
            using var bg = new SKPaint { Color = theme.PanelBg, Style = SKPaintStyle.Fill };
            canvas.DrawRect(x, y, width, height, bg);

            using var font = _cachedFont ?? new SKPaint { IsAntialias = true };
            using var skFont = _cachedSkFont ?? FontHelper.CreateDevToolsFont(12);

            if (_cachedFont == null) _cachedFont = font;
            if (_cachedSkFont == null) _cachedSkFont = skFont;

            using var tagPaint = new SKPaint { Color = theme.AccentBlue, IsAntialias = true };
            using var attrPaint = new SKPaint { Color = theme.AccentOrange, IsAntialias = true };
            using var strPaint = new SKPaint { Color = theme.AccentOrange, IsAntialias = true };
            using var commentPaint = new SKPaint { Color = theme.AccentGreen, IsAntialias = true };
            using var defPaint = new SKPaint { Color = theme.TextPrimary, IsAntialias = true };

            float lh = 18;
        float lnW = 50;
        _contentHeight = _lines.Length * lh;
        float maxScroll = Math.Max(0, _contentHeight - _viewHeight + lh);
        _scrollOffset = Math.Max(0, Math.Min(_scrollOffset, maxScroll));

        canvas.Save();
        canvas.ClipRect(new SKRect(x, y, x + width, y + height));

        float drawY = y + lh - _scrollOffset;
        for (int i = 0; i < _lines.Length; i++)
        {
            float lineDrawY = drawY;
            if (lineDrawY < y - lh || lineDrawY > y + height) { drawY += lh; continue; }

            if (_editing && i == _editLine)
            {
                using var hl = new SKPaint { Color = theme.LineHighlight, Style = SKPaintStyle.Fill };
                canvas.DrawRect(x + lnW, lineDrawY - lh + 4, width - lnW, lh, hl);
            }

            using var lineNumPaint = new SKPaint { Color = theme.LineNumberText, IsAntialias = true };
            string ln = (i + 1).ToString().PadLeft(4);
            canvas.DrawText(ln, x + 4, lineDrawY, SKTextAlign.Left, skFont, lineNumPaint);

            float tx = x + lnW;
            string lineText = _lines[i];

            // Selection highlight
            if (_editing && HasSelection)
            {
                var (sl, sc, el, ec) = SelRange;
                if (i >= sl && i <= el)
                {
                    float selStartX, selEndX;
                    if (sl == el)
                    {
                        selStartX = tx + skFont.MeasureText(lineText[..Math.Min(sc, lineText.Length)]);
                        selEndX = tx + skFont.MeasureText(lineText[..Math.Min(ec, lineText.Length)]);
                    }
                    else if (i == sl)
                    {
                        selStartX = tx + skFont.MeasureText(lineText[..Math.Min(sc, lineText.Length)]);
                        selEndX = tx + skFont.MeasureText(lineText);
                    }
                    else if (i == el)
                    {
                        selStartX = tx;
                        selEndX = tx + skFont.MeasureText(lineText[..Math.Min(ec, lineText.Length)]);
                    }
                    else
                    {
                        selStartX = tx;
                        selEndX = tx + skFont.MeasureText(lineText);
                    }
                    using var selBg = new SKPaint { Color = theme.SelectionBg, Style = SKPaintStyle.Fill };
                    canvas.DrawRect(selStartX, lineDrawY - lh + 4, selEndX - selStartX, lh, selBg);
                }
            }

            HighlightLine(canvas, lineText, tx, lineDrawY, width - lnW - 8, skFont, tagPaint, attrPaint, strPaint, commentPaint, defPaint);

            if (_editing && i == _editLine && _showCursor)
            {
                float cursorX = tx + skFont.MeasureText(lineText[..Math.Min(_editCol, lineText.Length)]);
                using var cp = new SKPaint { Color = theme.CursorColor, Style = SKPaintStyle.Fill, StrokeWidth = 1 };
                canvas.DrawLine(cursorX, lineDrawY - lh + 4, cursorX, lineDrawY + 2, cp);
            }

            drawY += lh;
        }

        canvas.Restore();

        if (_contentHeight > _viewHeight)
        {
            float sh = _viewHeight * _viewHeight / _contentHeight;
            float maxScrollBar = Math.Max(0, _contentHeight - _viewHeight);
            float sy = y + (maxScrollBar > 0 ? (_scrollOffset / maxScrollBar) * (_viewHeight - sh) : 0);
            using var sp = new SKPaint { Color = theme.ScrollbarThumb, Style = SKPaintStyle.Fill };
            canvas.DrawRoundRect(x + width - 6, sy, 4, sh, 2, 2, sp);
        }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DevToolsSource.Render] SKPaint/SKFont unavailable on this OS: {ex.GetType().Name}: {ex.Message}");
            _skiaAvailable = false;
            return;
        }
    }

    #region IImeSupport

    public Point GetImeCaretPosition()
    {
        if (!_editing || _editLine < 0 || _editLine >= _lines.Length)
            return new Point(_renderX + 50, _renderY);

        float tx = _renderX + 50;
        float lh = 18;
        string line = _lines[_editLine];
        string textBeforeCursor = line[..Math.Min(_editCol, line.Length)];
        using var testFont = FontHelper.CreateDevToolsFont(12);
        float cursorX = tx + testFont.MeasureText(textBeforeCursor);
        float lineDrawY = _renderY + lh - _scrollOffset + _editLine * lh;
        float cursorY = lineDrawY - lh + 4;
        return new Point(cursorX, cursorY);
    }

    public void OnImeCompositionStart()
    {
        _imeCompositionString = "";
    }

    public void OnImeCompositionUpdate(string compositionString, int cursorPosition)
    {
        _imeCompositionString = compositionString;
    }

    public void OnImeCompositionEnd(string? resultString)
    {
        _imeCompositionString = "";

        if (resultString != null && _editing && _editLine >= 0 && _editLine < _lines.Length)
        {
            DeleteSelectedText();
            InsertAtCursor(resultString);
        }
    }

    #endregion

    private void HighlightLine(SKCanvas canvas, string line, float x, float y, float maxW,
        SKFont font, SKPaint tag, SKPaint attr, SKPaint str, SKPaint comment, SKPaint def)
    {
        if (string.IsNullOrEmpty(line)) return;

        float cx = x;
        int i = 0;
        while (i < line.Length && cx < x + maxW)
        {
            if (i + 3 < line.Length && line[i] == '<' && line[i + 1] == '!' && line[i + 2] == '-' && line[i + 3] == '-')
            {
                int end = line.IndexOf("-->", i + 4);
                if (end < 0) end = line.Length - 1; else end += 3;
                string c = line[i..(Math.Min(end + 1, line.Length))];
                comment.Color = SKColor.Parse("#6A9955");
                canvas.DrawText(c, cx, y, SKTextAlign.Left, font, comment);
                cx += font.MeasureText(c);
                i = end + 1;
                continue;
            }

            if (line[i] == '<')
            {
                int end = line.IndexOf('>', i + 1);
                if (end < 0) end = line.Length - 1;

                int ne = end;
                for (int j = i + 1; j < line.Length; j++)
                {
                    if (char.IsWhiteSpace(line[j]) || line[j] == '>') { ne = j; break; }
                }
                string tn = line[i..(Math.Min(ne + 1, line.Length))];
                tag.Color = SKColor.Parse("#569CD6");
                canvas.DrawText(tn, cx, y, SKTextAlign.Left, font, tag);
                cx += font.MeasureText(tn);
                i = ne;

                while (i < end)
                {
                    while (i < end && char.IsWhiteSpace(line[i])) i++;
                    if (i >= end) break;

                    int ae = i;
                    while (ae < end && line[ae] != '=' && !char.IsWhiteSpace(line[ae])) ae++;
                    if (ae > i)
                    {
                        attr.Color = SKColor.Parse("#CE9178");
                        canvas.DrawText(line[i..ae], cx, y, SKTextAlign.Left, font, attr);
                        cx += font.MeasureText(line[i..ae]);
                        i = ae;
                    }

                    if (i < end && line[i] == '=')
                    {
                        def.Color = SKColor.Parse("#D4D4D4");
                        canvas.DrawText("=", cx, y, SKTextAlign.Left, font, def);
                        cx += font.MeasureText("=");
                        i++;
                        if (i < end && (line[i] == '"' || line[i] == '\''))
                        {
                            char q = line[i];
                            int ve = line.IndexOf(q, i + 1);
                            if (ve < 0 || ve > end) ve = end;
                            string v = line[i..(Math.Min(ve + 1, line.Length))];
                            str.Color = SKColor.Parse("#CE9178");
                            canvas.DrawText(v, cx, y, SKTextAlign.Left, font, str);
                            cx += font.MeasureText(v);
                            i = ve + 1;
                        }
                    }
                }

                if (end < line.Length && line[end] == '>' && ne < end)
                {
                    tag.Color = SKColor.Parse("#569CD6");
                    canvas.DrawText(">", cx, y, SKTextAlign.Left, font, tag);
                    cx += font.MeasureText(">");
                }
                i = end + 1;
            }
            else
            {
                int nt = line.IndexOf('<', i + 1);
                if (nt < 0) nt = line.Length;
                def.Color = SKColor.Parse("#D4D4D4");
                canvas.DrawText(line[i..nt], cx, y, SKTextAlign.Left, font, def);
                cx += font.MeasureText(line[i..nt]);
                i = nt;
            }
        }
    }
}
