using UpBrowser.Platform;
using UpBrowser.Rendering;

namespace UpBrowser;

public class InputHandler
{
    private readonly ChromeRenderer _chrome;
    private readonly ScrollManager _scroll;
    private readonly IWindow _window;
    private readonly float _dpiScale;
    private readonly float _contentOffset;

    private float _mouseX, _mouseY;
    private bool _mouseDown;

    private bool _pageThumbDragging;
    private float _pageThumbDragStartY;
    private float _pageThumbDragStartScroll;

    private bool _pageThumbXDragging;
    private float _pageThumbDragStartX;
    private float _pageThumbDragStartScrollX;

    private bool _needsRedraw = true;
    public bool NeedsRedraw
    {
        get => _needsRedraw;
        set
        {
            _needsRedraw = value;
            // Diagnostics (UPBROWSER_TAB_STATS=1): who keeps requesting redraws.
            if (value && RedrawTraceGate != null && Environment.TickCount64 >= _nextTraceTick)
            {
                _nextTraceTick = Environment.TickCount64 + 400;
                RedrawTraceGate(Environment.StackTrace);
            }
        }
    }
    public static Action<string>? RedrawTraceGate;
    private long _nextTraceTick;

    public Action<float, float>? OnDomClick { get; set; }
    public Action<float, float>? OnDomMouseMove { get; set; }
    public Action<float, float>? OnDevToolsMouseMove { get; set; }
    public Action<float, float, bool>? OnDomMouseDown { get; set; }
    public Action<float, float, bool>? OnDomMouseUp { get; set; }
    public Action<char, Key, bool>? OnDomKeyDown { get; set; }
    public Action<char, Key, bool>? OnDomKeyUp { get; set; }
    public Action<char>? OnDomChar { get; set; }
    public Action? OnDevToolsKey { get; set; }
    public Func<char, Key, bool, bool>? OnDevToolsInput { get; set; }
    public Func<float, float, bool, bool>? OnDevToolsClick { get; set; }
    public Func<double, float, float, bool>? OnDevToolsWheel { get; set; }
    public Func<float, float, bool>? OnDialogClick { get; set; }
    /// <summary>Clicks on the "page unresponsive" bubble, consulted before page input.</summary>
    public Func<float, float, bool>? OnPageBubbleClick { get; set; }
    public Func<float, float, bool, bool>? OnSettingsPageClick { get; set; }
    public Func<float, float, bool>? OnSettingsPageMove { get; set; }
    public Func<float, bool>? OnSettingsPageWheel { get; set; }
    public Func<float, float, bool, bool>? OnTaskManagerPageClick { get; set; }
    public Func<float, float, bool>? OnTaskManagerPageMove { get; set; }
    public Func<float, bool>? OnTaskManagerPageWheel { get; set; }
    public Func<double, double, float, float, bool>? OnScrollContainerWheel { get; set; }
    public Func<char, Key, bool, bool>? OnFormInputKey { get; set; }
    public Action<char>? OnImeChar { get; set; }
    public Action? OnCopy { get; set; }
    public Action? OnPaste { get; set; }
    public Action? OnCut { get; set; }
    public Action? OnSelectAll { get; set; }
    public Action? OnImeTargetChanged { get; set; }
    public Action? OnTaskManagerKey { get; set; }

    public bool IsCtrlDown { get; private set; }
    public bool IsShiftDown { get; private set; }
    public bool IsAltDown { get; private set; }

    public InputHandler(ChromeRenderer chrome, ScrollManager scroll, IWindow window, float dpiScale)
    {
        _chrome = chrome;
        _scroll = scroll;
        _window = window;
        _dpiScale = dpiScale;
        _contentOffset = chrome.GetContentOffset();
    }

    public void WireEvents()
    {
        _window.OnMouseMove = OnMouseMove;
        _window.OnMouseClick = OnMouseClick;
        _window.OnKeyDownWithChar = OnKeyDownWithChar;
        _window.OnKeyDown = OnKeyDown;
        _window.OnKeyUp = OnKeyUp;
        _window.OnMouseWheel = OnMouseWheel;
        _window.OnImeChar = OnImeChar;
        _chrome.OnUrlBarFocus = () => { };
        _chrome.OnUrlBarBlur = () => { };
    }

    private void OnMouseMove(float x, float y)
    {
        float logicalX = x / _dpiScale;
        float logicalY = y / _dpiScale;
        _mouseX = logicalX;
        _mouseY = logicalY;
        bool handledByTaskMgr = OnTaskManagerPageMove?.Invoke(logicalX, logicalY) ?? false;
        if (!handledByTaskMgr)
        {
            bool handledBySettings = OnSettingsPageMove?.Invoke(logicalX, logicalY) ?? false;
            if (!handledBySettings)
                _chrome.HandleMouseMove(logicalX, logicalY);
            OnDomMouseMove?.Invoke(logicalX, logicalY);
            OnDevToolsMouseMove?.Invoke(logicalX, logicalY);
        }
    }

    private void OnMouseClick(float x, float y, bool isDown)
    {
        float logicalX = x / _dpiScale;
        float logicalY = y / _dpiScale;
        _mouseX = logicalX;
        _mouseY = logicalY;

        if (isDown)
        {
            _mouseDown = true;
            OnDomMouseDown?.Invoke(logicalX, logicalY, false);
            if (OnDialogClick?.Invoke(logicalX, logicalY) == true)
            {
                NeedsRedraw = true;
                return;
            }
            if (OnPageBubbleClick?.Invoke(logicalX, logicalY) == true)
            {
                NeedsRedraw = true;
                return;
            }
            bool handled = OnTaskManagerPageClick?.Invoke(logicalX, logicalY, false) ?? false;
            if (!handled)
            {
                handled = OnSettingsPageClick?.Invoke(logicalX, logicalY, false) ?? false;
                if (!handled)
                    handled = _chrome.HandleMouseClick(logicalX, logicalY);
                if (!handled)
                    handled = OnDevToolsClick?.Invoke(logicalX, logicalY, false) ?? false;
                NeedsRedraw = true;
                if (!handled)
                {
                    if (!HandleScrollbarClick(logicalX, logicalY))
                        OnDomClick?.Invoke(logicalX, logicalY);
                }
            }
            else
            {
                NeedsRedraw = true;
            }
        }
        else
        {
            _mouseDown = false;
            _pageThumbDragging = false;
            _pageThumbXDragging = false;
            _chrome.HandleMouseUp();
            OnTaskManagerPageClick?.Invoke(logicalX, logicalY, true);
            OnSettingsPageClick?.Invoke(logicalX, logicalY, true);
            OnDevToolsClick?.Invoke(logicalX, logicalY, true);
            OnDomMouseUp?.Invoke(logicalX, logicalY, false);
        }
        if (isDown)
            OnImeTargetChanged?.Invoke();
    }

    private bool HandleScrollbarClick(float logicalX, float logicalY)
    {
        float statusBarHeight = _chrome.GetStatusBarHeight();
        float viewportHeight = _window.Height / _dpiScale - _contentOffset - statusBarHeight;
        float viewportWidth = _window.Width / _dpiScale;

        // 垂直滚动条
        if (_scroll.CanScrollY)
        {
            float scrollbarLeft = _window.Width / _dpiScale - ScrollManager.ScrollbarWidth;
            if (logicalX >= scrollbarLeft && logicalX <= scrollbarLeft + ScrollManager.ScrollbarWidth &&
                logicalY >= _contentOffset && logicalY <= _contentOffset + viewportHeight)
            {
                float trackHeight = viewportHeight;
                float thumbHeight = Math.Max(ScrollManager.ScrollbarMinThumbSize,
                    trackHeight * viewportHeight / _scroll.ContentHeight);
                float maxScrollY = _scroll.ContentHeight - viewportHeight;
                float thumbTop = maxScrollY > 0
                    ? (_scroll.ScrollY / maxScrollY) * (trackHeight - thumbHeight)
                    : 0;
                float thumbEnd = thumbTop + thumbHeight;

                if (logicalY >= _contentOffset + thumbTop && logicalY <= _contentOffset + thumbEnd)
                {
                    _pageThumbDragging = true;
                    _pageThumbDragStartY = logicalY;
                    _pageThumbDragStartScroll = _scroll.ScrollY;
                    return true;
                }
                else
                {
                    if (logicalY < _contentOffset + thumbTop)
                        _scroll.PageUp();
                    else
                        _scroll.PageDown();
                    return true;
                }
            }
        }

        // 水平滚动条
        if (_scroll.CanScrollX)
        {
            float scrollbarTop = _window.Height / _dpiScale - statusBarHeight - ScrollManager.ScrollbarWidth;
            float scrollbarRightEdge = _scroll.CanScrollY ? viewportWidth - ScrollManager.ScrollbarWidth : viewportWidth;
            if (logicalY >= scrollbarTop && logicalY <= scrollbarTop + ScrollManager.ScrollbarWidth &&
                logicalX >= 0 && logicalX <= scrollbarRightEdge)
            {
                float trackWidth = viewportWidth;
                float thumbWidth = Math.Max(ScrollManager.ScrollbarMinThumbSize,
                    trackWidth * viewportWidth / _scroll.ContentWidth);
                float maxScrollX = _scroll.ContentWidth - viewportWidth;
                float thumbLeft = maxScrollX > 0
                    ? (_scroll.ScrollX / maxScrollX) * (trackWidth - thumbWidth)
                    : 0;
                float thumbEnd = thumbLeft + thumbWidth;

                if (logicalX >= thumbLeft && logicalX <= thumbEnd)
                {
                    _pageThumbXDragging = true;
                    _pageThumbDragStartX = logicalX;
                    _pageThumbDragStartScrollX = _scroll.ScrollX;
                    return true;
                }
                else
                {
                    if (logicalX < thumbLeft)
                        _scroll.ScrollBy(-viewportWidth * 0.9f, 0);
                    else
                        _scroll.ScrollBy(viewportWidth * 0.9f, 0);
                    return true;
                }
            }
        }

        return false;
    }

    public void UpdatePageThumbDrag()
    {
        if (!_mouseDown) return;

        if (_pageThumbDragging)
        {
            float statusBarHeight = _chrome.GetStatusBarHeight();
            float viewportHeight = _window.Height / _dpiScale - _contentOffset - statusBarHeight;
            if (!_scroll.CanScrollY || viewportHeight <= 0) return;

            float trackHeight = viewportHeight;
            float thumbHeight = Math.Max(ScrollManager.ScrollbarMinThumbSize,
                trackHeight * viewportHeight / _scroll.ContentHeight);
            float maxScrollY = _scroll.ContentHeight - viewportHeight;
            if (maxScrollY <= 0) return;

            float delta = (_mouseY - _pageThumbDragStartY) / (trackHeight - thumbHeight) * maxScrollY;
            _scroll.ScrollToInstant(_pageThumbDragStartScroll + delta);
        }

        if (_pageThumbXDragging)
        {
            float statusBarHeight = _chrome.GetStatusBarHeight();
            float viewportWidth = _window.Width / _dpiScale;
            float trackWidth = viewportWidth;
            float thumbWidth = Math.Max(ScrollManager.ScrollbarMinThumbSize,
                trackWidth * viewportWidth / _scroll.ContentWidth);
            float maxScrollX = _scroll.ContentWidth - viewportWidth;
            if (maxScrollX <= 0) return;

            float delta = (_mouseX - _pageThumbDragStartX) / (trackWidth - thumbWidth) * maxScrollX;
            _scroll.ScrollToInstant(_pageThumbDragStartScrollX + delta, _scroll.ScrollY);
        }
    }

    private bool OnKeyDownWithChar(char charCode, Key key)
    {
        UpdateModifierKeys(key, true);

        if (OnDevToolsInput != null && !_chrome.IsUrlBarFocused())
        {
            if (OnDevToolsInput(charCode, key, IsShiftDown))
            {
                NeedsRedraw = true;
                return true;
            }
        }

        if (key == Key.Unknown && charCode != '\0')
        {
            if (charCode == 1) { OnSelectAll?.Invoke(); NeedsRedraw = true; return true; }
            if (charCode == 3) { OnCopy?.Invoke(); NeedsRedraw = true; return true; }
            if (charCode == 22) { OnPaste?.Invoke(); NeedsRedraw = true; return true; }
            if (charCode == 24) { OnCut?.Invoke(); NeedsRedraw = true; return true; }
            if (charCode == 26) { return true; }

            // Dispatch keydown and keypress to DOM
            OnDomKeyDown?.Invoke(charCode, key, false);
            OnDomChar?.Invoke(charCode);

            // Form input handling (e.g. text input on page)
            if (OnFormInputKey == null || !OnFormInputKey(charCode, key, IsShiftDown))
                _chrome.HandleKeyPress(charCode, SKKey.None);

            NeedsRedraw = true;
            return true;
        }

        SKKey chromeKey = key switch
        {
            Key.Enter => SKKey.Enter,
            Key.Escape => SKKey.Escape,
            Key.Left => SKKey.Left,
            Key.Up => SKKey.Up,
            Key.Right => SKKey.Right,
            Key.Down => SKKey.Down,
            Key.Home => SKKey.Home,
            Key.End => SKKey.End,
            Key.Backspace => SKKey.Backspace,
            Key.Delete => SKKey.Delete,
            Key.Tab => SKKey.Tab,
            Key.Space => SKKey.Space,
            _ => SKKey.None
        };

        // Dispatch keydown to DOM before Chrome handles it
        OnDomKeyDown?.Invoke(charCode, key, false);

        // Form input handling (e.g. text input on page)
        if (OnFormInputKey != null && OnFormInputKey(charCode, key, IsShiftDown))
        {
            OnImeTargetChanged?.Invoke();
            OnDomKeyUp?.Invoke(charCode, key, false);
            NeedsRedraw = true;
            return true;
        }

        bool handledByChrome = _chrome.HandleKeyPress(charCode, chromeKey, IsShiftDown);
        NeedsRedraw = true;

        if (handledByChrome)
        {
            OnImeTargetChanged?.Invoke();
            OnDomKeyUp?.Invoke(charCode, key, false);
            return true;
        }

        if (_chrome.IsUrlBarFocused())
            return false;

        switch (key)
        {
            case Key.Tab:
                _chrome.NextTab();
                OnDomKeyUp?.Invoke(charCode, key, false);
                return true;
            case Key.F5:
                _chrome.OnRefresh?.Invoke();
                OnDomKeyUp?.Invoke(charCode, key, false);
                return true;
            case Key.PageUp:
                _scroll.PageUp();
                OnDomKeyUp?.Invoke(charCode, key, false);
                return true;
            case Key.PageDown:
                _scroll.PageDown();
                OnDomKeyUp?.Invoke(charCode, key, false);
                return true;
            case Key.Home:
                _scroll.ScrollHome();
                OnDomKeyUp?.Invoke(charCode, key, false);
                return true;
            case Key.End:
                _scroll.ScrollEnd();
                OnDomKeyUp?.Invoke(charCode, key, false);
                return true;
            case Key.Up:
                _scroll.ScrollBy(0, -40);
                OnDomKeyUp?.Invoke(charCode, key, false);
                return true;
            case Key.Down:
                _scroll.ScrollBy(0, 40);
                OnDomKeyUp?.Invoke(charCode, key, false);
                return true;
            case Key.Left:
                _scroll.ScrollBy(-40, 0);
                OnDomKeyUp?.Invoke(charCode, key, false);
                return true;
            case Key.Right:
                _scroll.ScrollBy(40, 0);
                OnDomKeyUp?.Invoke(charCode, key, false);
                return true;
            default:
                OnDomKeyUp?.Invoke(charCode, key, false);
                return false;
        }
    }

    private void OnKeyDown(Key key)
    {
        UpdateModifierKeys(key, true);
        OnDomKeyDown?.Invoke('\0', key, false);

        if (key == Key.F12)
        {
            OnDevToolsKey?.Invoke();
            NeedsRedraw = true;
            return;
        }
        if (key == Key.Escape && IsShiftDown && !_chrome.IsUrlBarFocused())
        {
            OnTaskManagerKey?.Invoke();
            NeedsRedraw = true;
            return;
        }
        if (!_chrome.IsUrlBarFocused())
        {
            if (key == Key.F5)
                _chrome.OnRefresh?.Invoke();
        }
        else if (key == Key.Tab)
            _chrome.NextTab();

        OnDomKeyUp?.Invoke('\0', key, false);
    }

    private void OnKeyUp(Key key)
    {
        UpdateModifierKeys(key, false);
    }

    private void OnMouseWheel(double deltaX, double deltaY)
    {
        if ((OnTaskManagerPageWheel?.Invoke((float)deltaY) ?? false) ||
            (OnSettingsPageWheel?.Invoke((float)deltaY) ?? false))
        {
            NeedsRedraw = true;
            return;
        }

        if (OnDevToolsWheel != null && OnDevToolsWheel(deltaY, _mouseX, _mouseY))
        {
            NeedsRedraw = true;
            return;
        }

        if (!_chrome.IsUrlBarFocused())
        {
            // Try per-element scroll container first
            if (OnScrollContainerWheel != null &&
                OnScrollContainerWheel(deltaX, deltaY, _mouseX, _mouseY))
            {
                NeedsRedraw = true;
                return;
            }

            if (IsShiftDown)
                _scroll.ScrollBy((float)deltaY, 0);
            else
            {
                if (Math.Abs(deltaX) > 0)
                    _scroll.ScrollBy((float)deltaX, 0);
                else
                    _scroll.ScrollBy((float)deltaY);
            }
            // Don't set NeedsRedraw here - scrollChanged will trigger re-render without layout rebuild
        }
    }

    public (float x, float y) GetMousePosition() => (_mouseX, _mouseY);
    public bool IsMouseDown() => _mouseDown;

    public void HandleChar(char charCode)
    {
        if (charCode == '\0') return;
        if (OnDevToolsInput != null && !_chrome.IsUrlBarFocused())
        {
            if (OnDevToolsInput(charCode, Key.Unknown, false))
            {
                NeedsRedraw = true;
                return;
            }
        }
        _chrome.HandleKeyPress(charCode, SKKey.None);
        NeedsRedraw = true;
    }

    private void HandleImeChar(char charCode)
    {
        OnImeChar?.Invoke(charCode);
    }

    private void UpdateModifierKeys(Key key, bool isDown)
    {
        if (key is Key.Shift or Key.LShift or Key.RShift) IsShiftDown = isDown;
        else if (key is Key.Ctrl or Key.LCtrl or Key.RCtrl) IsCtrlDown = isDown;
        else if (key is Key.Alt or Key.LAlt or Key.RAlt) IsAltDown = isDown;
    }
}