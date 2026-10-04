using SkiaSharp;
using Acrux.Core;
using Acrux.PageContract;
using Acrux.Platform;

namespace Acrux.Rendering.DevTools;

public class DevToolsPanel
{
    private readonly DevToolsConsole _console;
    private readonly DevToolsElements _elements;
    private readonly DevToolsSource _source;

    private bool _visible;
    private int _activeTab;
    private readonly string[] _tabNames = { "Console", "Elements", "Source" };
    private float _tabBarHeight = 28;
    private float _panelHeight = 250;
    private float _panelTop;
    private float _panelLeft;
    private float _panelWidth;
    private float _dragStartY;
    private float _dragStartHeight;
    private bool _dragging;

    private int _thumbDragTab = -1;
    private int _hoveredTab = -1;
    private bool _hoveredClose;
    private bool _hoveredThemeBtn;

    public bool Visible => _visible;
    public float PanelHeight => _panelHeight;
    public Action? OnChanged;

    public DevToolsPanel()
    {
        Console.WriteLine("[DevToolsPanel] Creating DevToolsConsole...");
        _console = new DevToolsConsole();
        Console.WriteLine("[DevToolsPanel] DevToolsConsole OK");
        Console.WriteLine("[DevToolsPanel] Creating DevToolsElements...");
        _elements = new DevToolsElements();
        // Streamed chunks mutate the tree cache off the input path: repaint through the
        // panel's own change notification.
        _elements.OnRowsChanged = () => OnChanged?.Invoke();
        Console.WriteLine("[DevToolsPanel] DevToolsElements OK");
        Console.WriteLine("[DevToolsPanel] Creating DevToolsSource...");
        _source = new DevToolsSource();
        Console.WriteLine("[DevToolsPanel] DevToolsSource OK");
    }

    public void Toggle()
    {
        _visible = !_visible;
        OnChanged?.Invoke();
    }
    public void Show() { _visible = true; OnChanged?.Invoke(); }
    public void Hide() { _visible = false; _dragging = false; _thumbDragTab = -1; _hoveredTab = -1; _hoveredClose = false; _hoveredThemeBtn = false; OnChanged?.Invoke(); }

    public void SetSourceChangeHandler(Action<string> handler) { _source.OnHtmlChanged = handler; }

    /// <summary>Point the Elements/Console panels at an inspectable page. The channel
    /// answers with DTOs — the in-process one over the live document, the remote one over
    /// the pipe — so both modes drive the same panel code. <paramref name="htmlSource"/>
    /// feeds the Source tab, which still shows page text directly.</summary>
    public void SetInspection(IDevToolsChannel? channel, string htmlSource)
    {
        _elements.SetChannel(channel);
        _console.SetChannel(channel);
        _source.SetHtml(htmlSource);
    }

    public IImeSupport? GetActiveImeSupport()
    {
        return _activeTab switch
        {
            0 => _console,
            2 => _source,
            _ => null
        };
    }

    public void SelectAllInActiveTab()
    {
        switch (_activeTab)
        {
            case 0: _console.SelectAll(); break;
            case 2: _source.SelectAll(); break;
        }
    }

    public int GetActiveTab() => _activeTab;

    public void HandleMouseUp(float x, float y)
    {
        switch (_activeTab)
        {
            case 0: _console.HandleMouseUp(); break;
            case 2: _source.HandleMouseUp(); break;
        }
    }

    public bool TickCursorBlink()
    {
        bool c = _console.TickCursorBlink();
        bool s = _activeTab == 2 && _source.TickCursorBlink();
        return c || s;
    }

    public bool HandleWheel(double delta, float mouseX, float mouseY)
    {
        if (!_visible) return false;
        if (mouseX < _panelLeft || mouseX > _panelLeft + _panelWidth || mouseY < _panelTop || mouseY > _panelTop + _panelHeight)
            return false;
        return _activeTab switch
        {
            0 => _console.HandleWheel(delta),
            1 => _elements.HandleWheel(delta),
            2 => _source.HandleWheel(delta),
            _ => false
        };
    }

    public void HandleMouseMove(float x, float y, float windowWidth, float windowHeight)
    {
        if (!_visible) return;

        _panelLeft = 0;
        _panelWidth = windowWidth;
        _panelTop = windowHeight - _panelHeight;

        bool inPanel = x >= _panelLeft && x <= _panelLeft + _panelWidth &&
                       y >= _panelTop - 5 && y <= _panelTop + _panelHeight;
        int newTab = -1;
        bool newClose = false;
        bool newTheme = false;
        if (inPanel)
        {
            float tabBarTop = _panelTop;
            float tabX = 10;
            var tabSkFont = FontHelper.CreateDevToolsFont(12);
            for (int i = 0; i < _tabNames.Length; i++)
            {
                float tw = tabSkFont.MeasureText(_tabNames[i]) + 24;
                var tr = new SKRect(tabX, tabBarTop, tabX + tw, tabBarTop + _tabBarHeight);
                if (tr.Contains(x, y))
                {
                    newTab = i;
                    break;
                }
                tabX += tw;
            }

            float cx_close = _panelWidth - 30;
            float cy_close = tabBarTop + 4;
            newClose = new SKRect(cx_close, cy_close, cx_close + 20, cy_close + 20).Contains(x, y);
            newTheme = new SKRect(cx_close - 30, cy_close, cx_close - 10, cy_close + 20).Contains(x, y);
        }

        if (newTab != _hoveredTab || newClose != _hoveredClose || newTheme != _hoveredThemeBtn)
        {
            _hoveredTab = newTab;
            _hoveredClose = newClose;
            _hoveredThemeBtn = newTheme;
            OnChanged?.Invoke();
        }

        // Route mouse move to active tab for drag selection
        if (inPanel)
        {
            switch (_activeTab)
            {
                case 0: _console.HandleMouseMove(x, y); break;
                case 2: _source.HandleMouseMove(x, y); break;
            }
        }
    }

    public bool HandleClick(float x, float y)
    {
        if (!_visible) return false;

        if (x < _panelLeft || x > _panelLeft + _panelWidth || y < _panelTop || y > _panelTop + _panelHeight)
            return false;

        if (CloseButtonRect.Contains(x, y))
        {
            Hide();
            return true;
        }

        if (ThemeToggleRect.Contains(x, y))
        {
            DevToolsTheme.Current = DevToolsTheme.Current.Toggle();
            OnChanged?.Invoke();
            return true;
        }

        for (int i = 0; i < TabRects.Count; i++)
        {
            if (TabRects[i].Contains(x, y))
            {
                _activeTab = i;
                OnChanged?.Invoke();
                return true;
            }
        }

        float contentTop = _panelTop + _tabBarHeight;

        bool thumbHit = false;
        switch (_activeTab)
        {
            case 0: thumbHit = _console.HandleThumbDragStart(y); break;
            case 1: thumbHit = _elements.HandleThumbDragStart(y); break;
            case 2: thumbHit = _source.HandleThumbDragStart(y); break;
        }
        if (thumbHit) { _thumbDragTab = _activeTab; return true; }

        switch (_activeTab)
        {
            case 0:
                _console.HandleClick(x, y);
                OnChanged?.Invoke();
                break;
            case 2:
                _source.HandleClick(x, y);
                OnChanged?.Invoke();
                break;
        }

        return true;
    }

    public bool HandleDragStart(float x, float y)
    {
        if (!_visible) return false;
        float handleTop = _panelTop - 5;
        if (y >= handleTop && y <= handleTop + 10)
        {
            _dragging = true;
            _dragStartY = y;
            _dragStartHeight = _panelHeight;
            return true;
        }
        return false;
    }

    public bool HandleDragMove(float x, float y)
    {
        if (_dragging)
        {
            float delta = y - _dragStartY;
            _panelHeight = Math.Max(80, Math.Min(800, _dragStartHeight - delta));
            OnChanged?.Invoke();
            return true;
        }
        if (_thumbDragTab >= 0)
        {
            bool moved = false;
            switch (_thumbDragTab)
            {
                case 0: moved = _console.HandleThumbDrag(y); break;
                case 1: moved = _elements.HandleThumbDrag(y); break;
                case 2: moved = _source.HandleThumbDrag(y); break;
            }
            if (moved) OnChanged?.Invoke();
            return moved;
        }
        return false;
    }

    public void HandleDragEnd()
    {
        _dragging = false;
        _thumbDragTab = -1;
        _console.HandleThumbDragEnd();
        _elements.HandleThumbDragEnd();
        _source.HandleThumbDragEnd();
    }

    public bool HandleKeyPress(char keyChar, Key key, bool shift = false)
    {
        if (!_visible) return false;

        if (_activeTab == 2)
        {
            if (_source.HandleKeyPress(keyChar, key, shift))
            {
                OnChanged?.Invoke();
                return true;
            }
            return false;
        }

        if (_activeTab == 0)
            return _console.HandleKeyPress(keyChar, key, shift);

        return false;
    }

    public bool HandleImeChar(char c)
    {
        if (!_visible) return false;
        switch (_activeTab)
        {
            case 0:
                _console.HandleImeChar(c);
                OnChanged?.Invoke();
                return true;
            case 2:
                _source.HandleImeChar(c);
                OnChanged?.Invoke();
                return true;
            default:
                return false;
        }
    }

    public string GetActiveTabSelectedText()
    {
        return _activeTab switch
        {
            0 => _console.GetSelectedText(),
            2 => _source.GetSelectedText(),
            _ => ""
        };
    }

    public bool IsInputField(float x, float y)
    {
        if (!_visible) return false;
        if (_activeTab == 0)
        {
            float inputY = _console.InputFieldY;
            return y >= inputY && y <= inputY + 24;
        }
        return _activeTab == 2;
    }

    public SKPoint? GetCaretScreenPosition(float windowWidth, float windowHeight)
    {
        if (!_visible) return null;

        if (_activeTab == 2 && _source.IsEditing)
        {
            float py = _panelTop + _tabBarHeight;
            float sourceLineY = (_source.EditLine * 18) - _source.ScrollOffset + py;
            return new SKPoint(50 + _source.EditCol * 8, sourceLineY);
        }
        return null;
    }

    public void Render(SKCanvas canvas, float windowWidth, float windowHeight, float contentTop)
    {
        if (!_visible) return;

        var theme = DevToolsTheme.Current;

        _panelLeft = 0;
        _panelWidth = windowWidth;
        _panelTop = windowHeight - _panelHeight;

        float tabBarTop = _panelTop;
        float tabX = 10;
        var tabSkFont = FontHelper.CreateDevToolsFont(12);

        TabRects.Clear();
        for (int i = 0; i < _tabNames.Length; i++)
        {
            float tw = tabSkFont.MeasureText(_tabNames[i]) + 24;
            TabRects.Add(new SKRect(tabX, tabBarTop, tabX + tw, tabBarTop + _tabBarHeight));
            tabX += tw;
        }

        float cx_close = _panelWidth - 30;
        float cy_close = tabBarTop + 4;
        CloseButtonRect = new SKRect(cx_close, cy_close, cx_close + 20, cy_close + 20);

        float themeBtnX = cx_close - 30;
        ThemeToggleRect = new SKRect(themeBtnX, cy_close, themeBtnX + 20, cy_close + 20);

        // --- Phase 3: draw everything using hover state ---
        using var bg = new SKPaint { Color = theme.PanelBg, Style = SKPaintStyle.Fill };
        canvas.DrawRect(_panelLeft, _panelTop - 1, _panelWidth, _panelHeight + 1, bg);

        using var div = new SKPaint { Color = theme.Separator, Style = SKPaintStyle.Stroke, StrokeWidth = 1 };
        canvas.DrawLine(_panelLeft, _panelTop, _panelLeft + _panelWidth, _panelTop, div);

        using var dragPaint = new SKPaint { Color = theme.DragHandle, Style = SKPaintStyle.Fill };
        canvas.DrawRect(_panelLeft, _panelTop - 5, _panelWidth, 10, dragPaint);

        using var dragLine = new SKPaint { Color = theme.DragLine, Style = SKPaintStyle.Stroke, StrokeWidth = 1 };
        float cx1 = _panelWidth / 2 - 20;
        float cy1 = _panelTop - 1;
        canvas.DrawLine(cx1, cy1, cx1 + 40, cy1, dragLine);

        float tabX2 = 10;
        for (int i = 0; i < _tabNames.Length; i++)
        {
            var tr = TabRects[i];
            float tx = tabX2 + 12;
            float ty = tabBarTop + _tabBarHeight * 0.7f;

            if (i == _activeTab)
            {
                using var tabBg = new SKPaint { Color = theme.TabActiveBg, Style = SKPaintStyle.Fill };
                canvas.DrawRect(tr.Left, tr.Top, tr.Width, tr.Height, tabBg);

                using var indicator = new SKPaint { Color = theme.TabIndicator, Style = SKPaintStyle.Fill, StrokeWidth = 0 };
                float pad = 8;
                canvas.DrawRoundRect(tr.Left + pad, tr.Bottom - 3, tr.Width - pad * 2, 3, 1.5f, 1.5f, indicator);

                using var tabTextPaint = new SKPaint { Color = theme.TabActiveText, IsAntialias = true };
                canvas.DrawText(_tabNames[i], tx, ty, SKTextAlign.Left, tabSkFont, tabTextPaint);
            }
            else
            {
                if (i == _hoveredTab)
                {
                    using var tabHoverBg = new SKPaint { Color = theme.TabHoverBg, Style = SKPaintStyle.Fill };
                    canvas.DrawRect(tr.Left, tr.Top, tr.Width, tr.Height, tabHoverBg);
                }

                using var tabTextPaint = new SKPaint { Color = theme.TabInactiveText, IsAntialias = true };
                canvas.DrawText(_tabNames[i], tx, ty, SKTextAlign.Left, tabSkFont, tabTextPaint);
            }

            tabX2 += tr.Width;
        }

        if (_hoveredClose)
        {
            using var closeHover = new SKPaint { Color = theme.CloseBtnHoverBg, Style = SKPaintStyle.Fill };
            canvas.DrawRoundRect(CloseButtonRect, 4, 4, closeHover);
        }

        using var cpFont = FontHelper.CreateFont(14);
        using var cp = new SKPaint { Color = _hoveredClose ? theme.CloseBtnHoverText : theme.CloseBtnText, IsAntialias = true };
        canvas.DrawText("✕", cx_close + 3, cy_close + 15, SKTextAlign.Left, cpFont, cp);

        if (_hoveredThemeBtn)
        {
            using var themeHover = new SKPaint { Color = theme.BtnHoverBg, Style = SKPaintStyle.Fill };
            canvas.DrawRoundRect(ThemeToggleRect, 4, 4, themeHover);
        }

        string themeIcon = theme.IsLight ? "☀" : "☾";
        using var themeFont = FontHelper.CreateFont(13);
        using var themePaint = new SKPaint { Color = _hoveredThemeBtn ? theme.CloseBtnHoverText : theme.BtnDefault, IsAntialias = true };
        canvas.DrawText(themeIcon, themeBtnX + 3, cy_close + 15, SKTextAlign.Left, themeFont, themePaint);

        float cl = 0, ct = tabBarTop + _tabBarHeight, cw = _panelWidth, ch = _panelHeight - _tabBarHeight;
        switch (_activeTab)
        {
            case 0: _console.Render(canvas, cl, ct, cw, ch, theme); break;
            case 1: _elements.Render(canvas, cl, ct, cw, ch, theme); break;
            case 2: _source.Render(canvas, cl, ct, cw, ch, theme); break;
        }
    }

    public List<SKRect> TabRects { get; } = new();
    public SKRect CloseButtonRect { get; private set; }
    public SKRect ThemeToggleRect { get; private set; }
}
