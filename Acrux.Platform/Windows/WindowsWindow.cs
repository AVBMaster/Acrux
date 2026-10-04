using System.Diagnostics;
using System.Text;
using Acrux.Core;
using Acrux.Native.Windows;

namespace Acrux.Platform.Windows;

public class WindowsWindow : IWindow
{
    private IntPtr _hwnd;
    private bool _disposed;
    private bool _isRunning;
    private Action<double>? _onFrame;
    private Action<double, double>? _onMouseWheel;
    private Action<Key>? _onKeyDown;
    private Action<Key>? _onKeyUp;
    private long _lastFrameStamp; // Stopwatch ticks — TickCount64's ~15.6ms granularity halved a 60Hz target
    private int _width;
    private int _height;
    private NativeWindow.WndProc? _wndProc;

    private WindowsImeHandler? _imeHandler;
    private IImeSupport? _imeTarget;
    private IntPtr _detachedImeContext;
    private bool _imeContextDetached;
    private float _dpiScale = 1.0f;
    private float _targetFrameTimeMs = 16.0f;

    // Memory-DC back buffer so the full frame is composed off-screen and blitted
    // to the window in one atomic BitBlt — a direct StretchDIBits to the client DC
    // each frame tears and flickers on high-activity pages.
    private IntPtr _backDC = IntPtr.Zero;
    private IntPtr _backBmp = IntPtr.Zero;
    private IntPtr _backOldBmp = IntPtr.Zero;
    private int _backW;
    private int _backH;

    // True between WM_ENTERSIZEMOVE and WM_EXITSIZEMOVE (an interactive resize
    // or move drag). Exposed via IWindow.IsInSizeMove so the renderer can use a
    // cheap direct whole-page draw while the drag runs.
    private bool _inSizeMove;

    private Action<char>? _onChar;
    private Action<char>? _onImeChar;
    private Func<char, Key, bool>? _onKeyDownWithChar;
    private Action<float, float>? _onMouseMove;
    private Action<float, float, bool>? _onMouseClick;
    private Action<float>? _onDpiChanged;
    private Action? _onSetFocus;
    private Action? _onKillFocus;

    public Action<char>? OnChar
    {
        get => _onChar;
        set => _onChar = value;
    }

    public Action<char>? OnImeChar
    {
        get => _onImeChar;
        set => _onImeChar = value;
    }

    public Func<char, Key, bool>? OnKeyDownWithChar
    {
        get => _onKeyDownWithChar;
        set => _onKeyDownWithChar = value;
    }

    public Action<float, float>? OnMouseMove
    {
        get => _onMouseMove;
        set => _onMouseMove = value;
    }

    public Action<float, float, bool>? OnMouseClick
    {
        get => _onMouseClick;
        set => _onMouseClick = value;
    }

    public Action<double, double>? OnMouseWheel
    {
        get => _onMouseWheel;
        set => _onMouseWheel = value;
    }

    public Action<Key>? OnKeyDown
    {
        get => _onKeyDown;
        set => _onKeyDown = value;
    }

    public Action<Key>? OnKeyUp
    {
        get => _onKeyUp;
        set => _onKeyUp = value;
    }

    public float TargetFrameTimeMs
    {
        get => _targetFrameTimeMs;
        set => _targetFrameTimeMs = Math.Clamp(value, 1f, 100f);
    }

    public Action<float>? OnDpiChanged
    {
        get => _onDpiChanged;
        set => _onDpiChanged = value;
    }

    public Action? OnSetFocus
    {
        get => _onSetFocus;
        set => _onSetFocus = value;
    }

    public Action? OnKillFocus
    {
        get => _onKillFocus;
        set => _onKillFocus = value;
    }

    public int Width => _width;
    public int Height => _height;
    public IntPtr Handle => _hwnd;
    public IntPtr? GetNativeHandle() => _hwnd;
    public bool IsInSizeMove => _inSizeMove;

    public IImeHandler? ImeHandler => _imeHandler;

    public WindowsWindow(int width, int height, string title)
    {
        _width = width;
        _height = height;
        Initialize(width, height, title);
    }

    public void SetImeTarget(IImeSupport? target)
    {
        bool changed = _imeTarget != target;
        _imeTarget = target;
        UpdateInputMethodAssociation();
        if (changed && _imeTarget != null)
        {
            UpdateImeCompositionWindow();
        }
    }

    private void UpdateInputMethodAssociation()
    {
        if (_hwnd == IntPtr.Zero)
            return;

        bool shouldEnableIme = _imeTarget != null;

        if (shouldEnableIme)
        {
            if (!_imeContextDetached)
                return;

            if (_detachedImeContext != IntPtr.Zero)
            {
                _ = Imm32Interop.ImmAssociateContext(_hwnd, _detachedImeContext);
            }
            else
            {
                _ = Imm32Interop.ImmAssociateContextEx(_hwnd, IntPtr.Zero, 0x0010);
            }

            _detachedImeContext = IntPtr.Zero;
            _imeContextDetached = false;
            return;
        }

        if (_imeContextDetached)
            return;

        _detachedImeContext = Imm32Interop.ImmAssociateContext(_hwnd, IntPtr.Zero);
        _imeContextDetached = true;
    }

    public void UpdateImeCompositionWindow()
    {
        if (_imeTarget == null || _hwnd == IntPtr.Zero)
            return;

        var caretPos = _imeTarget.GetImeCaretPosition();

        IntPtr hIMC = Imm32Interop.ImmGetContext(_hwnd);
        if (hIMC == IntPtr.Zero)
            return;

        try
        {
            int caretX = (int)Math.Round(caretPos.X * _dpiScale);
            int caretY = (int)Math.Round(caretPos.Y * _dpiScale);

            var compForm = new Imm32Interop.COMPOSITIONFORM
            {
                dwStyle = Imm32Interop.CFS_POINT,
                ptCurrentPos = new Imm32Interop.POINT
                {
                    X = caretX,
                    Y = caretY
                }
            };

            Imm32Interop.ImmSetCompositionWindow(hIMC, ref compForm);

            var candForm = new Imm32Interop.CANDIDATEFORM
            {
                dwIndex = 0,
                dwStyle = Imm32Interop.CFS_CANDIDATEPOS,
                ptCurrentPos = new Imm32Interop.POINT
                {
                    X = caretX,
                    Y = caretY
                }
            };

            Imm32Interop.ImmSetCandidateWindow(hIMC, ref candForm);
        }
        finally
        {
            Imm32Interop.ImmReleaseContext(_hwnd, hIMC);
        }
    }

    private unsafe void Initialize(int width, int height, string title)
    {
        var hInstance = NativeWindow.GetModuleHandleW(null);

        _wndProc = new NativeWindow.WndProc(WndProc);
        NativeWindow.GetOrRegisterClass(_wndProc, hInstance);

        _hwnd = NativeWindow.CreateWindowExW(
            0,
            NativeWindow.GetClassName(),
            title,
            NativeWindow.WS_OVERLAPPEDWINDOW | NativeWindow.WS_VISIBLE,
            NativeWindow.CW_USEDEFAULT,
            NativeWindow.CW_USEDEFAULT,
            width,
            height,
            NativeWindow.HWND_DESKTOP,
            IntPtr.Zero,
            hInstance,
            IntPtr.Zero);

        if (_hwnd == IntPtr.Zero)
            throw new InvalidOperationException("Failed to create window");

        _imeHandler = new WindowsImeHandler(_hwnd);

        _detachedImeContext = Imm32Interop.ImmAssociateContext(_hwnd, IntPtr.Zero);
        _imeContextDetached = true;

        NativeWindow.ShowWindow(_hwnd, NativeWindow.SW_SHOWNORMAL);
        NativeWindow.UpdateWindow(_hwnd);
    }

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case NativeWindow.WM_DESTROY:
                _isRunning = false;
                NativeWindow.PostQuitMessage(0);
                return IntPtr.Zero;

            case NativeWindow.WM_PAINT:
                {
                    NativeWindow.BeginPaint(hWnd, out var ps);

                    // The client area has no background brush and WM_ERASEBKGND is a
                    // no-op, so an invalidation left unbacked shows raw black — most
                    // visibly the freshly exposed strip during a resize drag. Paint
                    // the last composed frame over the client so every system-driven
                    // repaint (resize, occlusion restore, minimize/restore) shows
                    // content instead of black. During a drag the frame is stretched;
                    // once settled the sizes match and it is a 1:1 copy.
                    if (ps.hdc != IntPtr.Zero && _backDC != IntPtr.Zero && _backBmp != IntPtr.Zero
                        && _backW > 0 && _backH > 0)
                    {
                        NativeWindow.GetClientRect(hWnd, out var cr);
                        int cw = cr.Right - cr.Left;
                        int ch = cr.Bottom - cr.Top;
                        if (cw > 0 && ch > 0)
                        {
                            if (cw == _backW && ch == _backH)
                            {
                                NativeWindow.BitBlt(ps.hdc, 0, 0, cw, ch, _backDC, 0, 0, NativeWindow.SRCCOPY);
                            }
                            else
                            {
                                int prevMode = NativeWindow.SetStretchBltMode(ps.hdc, NativeWindow.HALFTONE);
                                NativeWindow.SetBrushOrgEx(ps.hdc, 0, 0, IntPtr.Zero);
                                NativeWindow.StretchBlt(ps.hdc, 0, 0, cw, ch,
                                    _backDC, 0, 0, _backW, _backH, NativeWindow.SRCCOPY);
                                if (prevMode != 0)
                                    NativeWindow.SetStretchBltMode(ps.hdc, prevMode);
                            }
                        }
                    }

                    NativeWindow.EndPaint(hWnd, ref ps);
                    return IntPtr.Zero;
                }

            case NativeWindow.WM_ERASEBKGND:
                return new IntPtr(1);

            case NativeWindow.WM_NCPAINT:
                return IntPtr.Zero;

            case NativeWindow.WM_ENTERSIZEMOVE:
                _inSizeMove = true;
                return IntPtr.Zero;

            case NativeWindow.WM_EXITSIZEMOVE:
                {
                    _inSizeMove = false;
                    // Drag settled: render one more frame so the renderer hands
                    // back to the tile path and warms the cache at the final size.
                    if (_onFrame != null && _width > 0 && _height > 0)
                    {
                        _lastFrameStamp = Stopwatch.GetTimestamp();
                        _onFrame(0.016);
                    }
                    return IntPtr.Zero;
                }

            case NativeWindow.WM_SIZE:
                {
                    _width = (int)(lParam.ToInt64() & 0xFFFF);
                    _height = (int)((lParam.ToInt64() >> 16) & 0xFFFF);

                    NativeWindow.InvalidateRect(_hwnd, IntPtr.Zero, false);

                    // Growing the window exposes fresh client pixels that DWM shows
                    // as black until something paints them. Blit the last composed
                    // frame over them synchronously (WM_PAINT fallback) BEFORE the
                    // relayout runs, so no black strip is ever visible; the live
                    // frame below replaces it within the same message.
                    NativeWindow.UpdateWindow(_hwnd);

                    // Re-layout and render at the new size on EVERY resize tick so
                    // the page tracks the drag live (reflow, media queries). While
                    // the drag is in flight the renderer switches to a cheap
                    // whole-page direct draw (IWindow.IsInSizeMove), which keeps
                    // each resize tick fast instead of re-rasterizing every tile.
                    if (_onFrame != null && _width > 0 && _height > 0)
                    {
                        _lastFrameStamp = Stopwatch.GetTimestamp();
                        _onFrame(0.016);
                    }
                    return IntPtr.Zero;
                }

            case 0x02E0:
                {
                    uint dpi = (uint)(wParam.ToInt64() & 0xFFFF);
                    _dpiScale = dpi / 96.0f;
                    _onDpiChanged?.Invoke(_dpiScale);

                    unsafe
                    {
                        NativeWindow.RECT* suggestedRect = (NativeWindow.RECT*)lParam;
                        NativeWindow.SetWindowPos(_hwnd, IntPtr.Zero,
                            suggestedRect->Left, suggestedRect->Top,
                            suggestedRect->Right - suggestedRect->Left,
                            suggestedRect->Bottom - suggestedRect->Top,
                            0x0040);
                    }

                    UpdateImeCompositionWindow();
                    return IntPtr.Zero;
                }

            case NativeWindow.WM_MOUSEWHEEL:
                {
                    int rawDelta = (int)((short)((wParam.ToInt64() >> 16) & 0xFFFF));
                    _onMouseWheel?.Invoke(0, rawDelta);
                    return IntPtr.Zero;
                }

            case 0x020E: // WM_MOUSEHWHEEL
                {
                    int rawDelta = (int)((short)((wParam.ToInt64() >> 16) & 0xFFFF));
                    _onMouseWheel?.Invoke(rawDelta, 0);
                    return IntPtr.Zero;
                }

            case NativeWindow.WM_SETFOCUS:
                {
                    _onSetFocus?.Invoke();
                    return IntPtr.Zero;
                }

            case NativeWindow.WM_KILLFOCUS:
                {
                    _onKillFocus?.Invoke();
                    return IntPtr.Zero;
                }

            case NativeWindow.WM_IME_STARTCOMPOSITION:
                {
                    _imeTarget?.OnImeCompositionStart();
                    UpdateImeCompositionWindow();
                    return IntPtr.Zero;
                }

            case NativeWindow.WM_IME_COMPOSITION:
                {
                    if (_imeHandler == null)
                        return IntPtr.Zero;

                    int flags = (int)lParam;
                    var state = _imeHandler.GetCompositionState();

                    if ((flags & Imm32Interop.GCS_RESULTSTR) != 0)
                    {
                        if (!string.IsNullOrEmpty(state.CommittedText))
                        {
                            if (_imeTarget != null)
                            {
                                _imeTarget.OnImeCompositionEnd(state.CommittedText);
                            }
                            else
                            {
                                foreach (char c in state.CommittedText)
                                {
                                    _onImeChar?.Invoke(c);
                                }
                            }
                        }
                    }

                    if ((flags & Imm32Interop.GCS_COMPSTR) != 0)
                    {
                        if (!string.IsNullOrEmpty(state.CompositionText))
                        {
                            if (_imeTarget != null)
                            {
                                _imeTarget.OnImeCompositionUpdate(state.CompositionText, state.CursorPosition);
                                UpdateImeCompositionWindow();
                            }
                        }
                    }

                    return IntPtr.Zero;
                }

            case NativeWindow.WM_IME_ENDCOMPOSITION:
                {
                    _imeTarget?.OnImeCompositionEnd(null);
                    _imeHandler?.Reset();
                    return IntPtr.Zero;
                }

            case NativeWindow.WM_IME_NOTIFY:
                return IntPtr.Zero;

            case NativeWindow.WM_CHAR:
                {
                    char charCode = (char)(wParam.ToInt32() & 0xFFFF);

                    if (_onKeyDownWithChar != null)
                    {
                        _onKeyDownWithChar(charCode, Key.Unknown);
                    }
                    else if (_onChar != null)
                    {
                        _onChar(charCode);
                    }
                    return IntPtr.Zero;
                }

            case NativeWindow.WM_KEYDOWN:
                {
                    int virtualKey = wParam.ToInt32();
                    var key = (Key)virtualKey;

                    if (_onKeyDownWithChar != null)
                    {
                        bool handled = _onKeyDownWithChar('\0', key);
                        if (handled) return IntPtr.Zero;
                    }

                    _onKeyDown?.Invoke(key);
                    return IntPtr.Zero;
                }

            case NativeWindow.WM_KEYUP:
                {
                    int virtualKey = wParam.ToInt32();
                    var key = (Key)virtualKey;
                    _onKeyUp?.Invoke(key);
                    return IntPtr.Zero;
                }

            case NativeWindow.WM_LBUTTONDOWN:
                {
                    int mouseX = (int)(lParam.ToInt64() & 0xFFFF);
                    int mouseY = (int)((lParam.ToInt64() >> 16) & 0xFFFF);

                    _onMouseClick?.Invoke(mouseX, mouseY, true);
                    return IntPtr.Zero;
                }

            case NativeWindow.WM_MOUSEMOVE:
                {
                    int mouseX = (int)(lParam.ToInt64() & 0xFFFF);
                    int mouseY = (int)((lParam.ToInt64() >> 16) & 0xFFFF);

                    _onMouseMove?.Invoke(mouseX, mouseY);
                    return IntPtr.Zero;
                }

            case NativeWindow.WM_LBUTTONUP:
                {
                    int mouseX = (int)(lParam.ToInt64() & 0xFFFF);
                    int mouseY = (int)((lParam.ToInt64() >> 16) & 0xFFFF);

                    _onMouseClick?.Invoke(mouseX, mouseY, false);
                    return IntPtr.Zero;
                }

            case NativeWindow.WM_CLOSE:
                _isRunning = false;
                NativeWindow.DestroyWindow(_hwnd);
                return IntPtr.Zero;

            default:
                return NativeWindow.DefWindowProcW(hWnd, msg, wParam, lParam);
        }
    }

    public void Run(Action<double> onFrame)
    {
        if (_hwnd == IntPtr.Zero) return;

        _onFrame = onFrame;
        _lastFrameStamp = Stopwatch.GetTimestamp();
        _isRunning = true;

        NativeWindow.MSG msg;
        while (_isRunning)
        {
            bool hasMessage = false;
            while (NativeWindow.PeekMessageW(out msg, IntPtr.Zero, 0, 0, 1))
            {
                hasMessage = true;
                if (msg.message == NativeWindow.WM_QUIT)
                {
                    _isRunning = false;
                    return;
                }
                NativeWindow.TranslateMessage(ref msg);
                NativeWindow.DispatchMessageW(ref msg);
            }

            if (_onFrame != null)
            {
                long nowTs = Stopwatch.GetTimestamp();
                double dtMs = (nowTs - _lastFrameStamp) * 1000.0 / Stopwatch.Frequency;
                double dt = dtMs / 1000.0;
                double targetDt = _targetFrameTimeMs / 1000.0;

                if (dt >= targetDt)
                {
                    _lastFrameStamp = nowTs;
                    try
                    {
                        _onFrame(dt);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[WindowsWindow.Run] _onFrame crashed: {ex.GetType().FullName}: {ex.Message}");
                        Console.WriteLine(ex.StackTrace);
                        try
                        {
                            File.WriteAllText("acrux_frame_crash.log", ex.ToString());
                        }
                        catch { }
                        throw;
                    }
                }
                else if (!hasMessage)
                {
                    int sleepMs = Math.Max(1, (int)(_targetFrameTimeMs + 0.5 - dtMs));
                    // MsgWait wakes the instant a message (input, paint) arrives —
                    // Thread.Sleep would strand fresh input until the sleep expires,
                    // which reads as scroll/paint latency multiples of the frame time.
                    NativeWindow.MsgWaitForMultipleObjects(0, null, false, (uint)sleepMs, NativeWindow.QS_ALLINPUT);
                }
            }
        }
    }

    public (int width, int height) GetClientSize() => (_width, _height);

    public bool PumpPendingMessage()
    {
        NativeWindow.MSG msg;
        if (NativeWindow.PeekMessageW(out msg, IntPtr.Zero, 0, 0, 1))
        {
            if (msg.message == NativeWindow.WM_QUIT)
            {
                _isRunning = false;
                return false;
            }
            NativeWindow.TranslateMessage(ref msg);
            NativeWindow.DispatchMessageW(ref msg);
            return true;
        }
        return false;
    }

    public unsafe void Render(byte[] pixels, int width, int height)
    {
        if (_hwnd == IntPtr.Zero || pixels.Length == 0 || width <= 0 || height <= 0) return;

        var bmi = new NativeWindow.BITMAPINFO();
        bmi.bmiHeader.biSize = (uint)sizeof(NativeWindow.BITMAPINFOHEADER);
        bmi.bmiHeader.biWidth = width;
        bmi.bmiHeader.biHeight = -height;
        bmi.bmiHeader.biPlanes = 1;
        bmi.bmiHeader.biBitCount = 32;
        bmi.bmiHeader.biCompression = 0;

        var hdc = NativeWindow.GetDC(_hwnd);

        // Stretch to full window client area to handle ResolutionScale < 1.0
        NativeWindow.GetClientRect(_hwnd, out var clientRect);
        int destW = clientRect.Right - clientRect.Left;
        int destH = clientRect.Bottom - clientRect.Top;

        try
        {
            // Compose into the cached back buffer, then present with one BitBlt so
            // the screen never observes a partially updated frame.
            if (destW <= 0 || destH <= 0) return;
            if (_backDC == IntPtr.Zero || _backBmp == IntPtr.Zero || _backW != destW || _backH != destH)
            {
                if (_backBmp != IntPtr.Zero) NativeWindow.DeleteObject(_backBmp);
                if (_backDC != IntPtr.Zero) { NativeWindow.SelectObject(_backDC, _backOldBmp); NativeWindow.DeleteDC(_backDC); }
                _backDC = NativeWindow.CreateCompatibleDC(hdc);
                _backBmp = NativeWindow.CreateCompatibleBitmap(hdc, destW, destH);
                _backOldBmp = NativeWindow.SelectObject(_backDC, _backBmp);
                _backW = destW;
                _backH = destH;
            }

            if (width == destW && height == destH)
            {
                // Device-pixel-accurate copy. SetDIBitsToDevice never filters, so
                // the frame lands on screen exactly as Skia rasterised it.
                NativeWindow.SetDIBitsToDevice(_backDC, 0, 0, destW, destH,
                    0, 0, 0, height, pixels, ref bmi, NativeWindow.DIB_RGB_COLORS);
            }
            else
            {
                // ResolutionScale != 1: an intentional scale. COLORONCOLOR keeps
                // colour while interpolating — never HALFTONE/BLACKONWHITE here.
                NativeWindow.StretchDIBits(_backDC, 0, 0, destW, destH,
                    0, 0, width, height, pixels, ref bmi,
                    NativeWindow.DIB_RGB_COLORS, NativeWindow.SRCCOPY,
                    NativeWindow.COLORONCOLOR);
            }
            NativeWindow.BitBlt(hdc, 0, 0, destW, destH, _backDC, 0, 0, 0x00CC0020 /* SRCCOPY */);
        }
        finally
        {
            NativeWindow.ReleaseDC(_hwnd, hdc);
        }
    }

    public void Close()
    {
        if (_hwnd != IntPtr.Zero)
        {
            NativeWindow.DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
        }
        ReleaseBackBuffer();
        _isRunning = false;
    }

    private void ReleaseBackBuffer()
    {
        if (_backBmp != IntPtr.Zero)
        {
            NativeWindow.DeleteObject(_backBmp);
            _backBmp = IntPtr.Zero;
        }
        if (_backDC != IntPtr.Zero)
        {
            // Restore the original bitmap before deleting the memory DC.
            NativeWindow.SelectObject(_backDC, _backOldBmp);
            NativeWindow.DeleteDC(_backDC);
            _backDC = IntPtr.Zero;
        }
        _backOldBmp = IntPtr.Zero;
        _backW = _backH = 0;
    }

    public void Dispose()
    {
        if (_disposed) return;

        Close();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}