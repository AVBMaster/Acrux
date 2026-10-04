using System.Runtime.InteropServices;
using System.Text;
using Acrux.Core;

namespace Acrux.Platform.Linux;

/// <summary>
/// Linux window implementation backed by X11 (Xlib).
///
/// Design notes:
/// - XEvent is a fixed 192-byte union. Rather than declaring a struct that
///   interleaves every member (which marshals wrong), events are received into
///   a raw 192-byte buffer and typed fields are read through unsafe pointer
///   casts to the real Xlib event structs (XKeyEvent, XButtonEvent, ...).
/// - Frames are composed into an off-screen Pixmap (double buffering) before
///   being copied to the window, so Expose/resize repaints never show black.
/// - Interactive resize tracking mirrors the Windows WM_ENTERSIZEMOVE flow: a
///   root-window pointer grab followed by ConfigureNotify events marks the
///   drag via <see cref="IsInSizeMove"/>, and every ConfigureNotify pumps a
///   live frame so the page reflows while the drag is in flight.
/// - Text input goes through XIM (XFilterEvent + XLookupString); key codes are
///   mapped from keysyms to the VK-style <see cref="Key"/> enum used app-wide.
/// </summary>
public sealed unsafe class LinuxWindow : IWindow
{
    // ----- X11 event type codes -----
    private const int KeyPress = 2;
    private const int KeyRelease = 3;
    private const int ButtonPress = 4;
    private const int ButtonRelease = 5;
    private const int MotionNotify = 6;
    private const int FocusIn = 9;
    private const int FocusOut = 10;
    private const int Expose = 12;
    private const int DestroyNotify = 17;
    private const int ConfigureNotify = 22;
    private const int ClientMessage = 33;

    // ----- Event masks (real Xlib values — event N selects bit N-2) -----
    private const long KeyPressMask = 1L << 0;
    private const long KeyReleaseMask = 1L << 1;
    private const long ButtonPressMask = 1L << 2;
    private const long ButtonReleaseMask = 1L << 3;
    private const long PointerMotionMask = 1L << 6;
    private const long ExposureMask = 1L << 15;
    private const long StructureNotifyMask = 1L << 17;
    private const long FocusChangeMask = 1L << 21;

    private const long EventMaskAll = KeyPressMask | KeyReleaseMask | ButtonPressMask | ButtonReleaseMask
        | PointerMotionMask | StructureNotifyMask | FocusChangeMask | ExposureMask;

    private const int ZPixmap = 2;
    private const int NotifyNormal = 0;
    private const int EventBufferSize = 192;

    private IntPtr _display;
    private ulong _window;
    private ulong _root;
    private IntPtr _im;   // XIM handle
    private IntPtr _ic;   // XIC input context
    private ulong _gc;
    private ulong _backPixmap;
    private IntPtr _ximage;
    private byte* _framebuffer;
    private int _fbW, _fbH;

    private nint _deleteWindowAtom;
    private nint _protocolsAtom;

    private bool _disposed;
    private bool _isRunning;
    private Action<double>? _onFrame;
    private DateTime _lastFrameTime;
    private int _width;
    private int _height;
    private readonly string _title;

    // Size-move heuristic: the WM grabs the pointer for title-bar / border
    // drags, which shows up as ButtonPress on the root window. A
    // ConfigureNotify arriving while that button is held is an interactive
    // resize/move tick.
    private bool _rootButtonHeld;
    private bool _inSizeMove;
    private int _lastConfigX = int.MinValue, _lastConfigY = int.MinValue;

    private float _targetFrameTimeMs = 16.0f;

    public Action<char>? OnChar { get; set; }
    public Action<char>? OnImeChar { get; set; }
    public Func<char, Key, bool>? OnKeyDownWithChar { get; set; }
    public Action<Key>? OnKeyDown { get; set; }
    public Action<Key>? OnKeyUp { get; set; }
    public Action<float, float>? OnMouseMove { get; set; }
    public Action<float, float, bool>? OnMouseClick { get; set; }
    public Action<double, double>? OnMouseWheel { get; set; }
    public Action<float>? OnDpiChanged { get; set; }
    public Action? OnSetFocus { get; set; }
    public Action? OnKillFocus { get; set; }

    public int Width => _width;
    public int Height => _height;
    public IntPtr? GetNativeHandle() => _window != 0 ? new IntPtr(unchecked((long)_window)) : null;
    public bool IsInSizeMove => _inSizeMove;
    public IImeHandler? ImeHandler => null;

    public float TargetFrameTimeMs
    {
        get => _targetFrameTimeMs;
        set => _targetFrameTimeMs = Math.Clamp(value, 1f, 100f);
    }

    public LinuxWindow(int width, int height, string title)
    {
        _width = Math.Max(1, width);
        _height = Math.Max(1, height);
        _title = title;
        Initialize();
    }

    public (int width, int height) GetClientSize() => (_width, _height);

    public void SetImeTarget(IImeSupport? target) { /* XIM stays attached to the window for its lifetime. */ }
    public void UpdateImeCompositionWindow() { }

    // ------------------------------------------------------------------
    // Initialization / teardown
    // ------------------------------------------------------------------
    private void Initialize()
    {
        _display = XOpenDisplay(IntPtr.Zero);
        if (_display == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                "Cannot open X display. Ensure an X11 server (or XWayland) is available and DISPLAY is set.");
        }

        // Never let a benign BadAccess (e.g. root event selection) abort the process.
        XSetErrorHandler((nint)(IntPtr)(delegate* unmanaged<IntPtr, IntPtr, int>)&StaticErrorHandler);

        int screen = XDefaultScreen(_display);
        _root = XRootWindow(_display, screen);

        _window = XCreateSimpleWindow(_display, _root, 0, 0, (uint)_width, (uint)_height, 0,
            XBlackPixel(_display, screen), XBlackPixel(_display, screen));

        XSelectInput(_display, _window, EventMaskAll);
        // Observing root button presses lets us infer the WM's interactive
        // grab (title-bar / border drag) without XFixes or _NET_WM state.
        XSelectInput(_display, _root, ButtonPressMask | ButtonReleaseMask);

        _protocolsAtom = XInternAtom(_display, "WM_PROTOCOLS", 0);
        _deleteWindowAtom = XInternAtom(_display, "WM_DELETE_WINDOW", 0);
        nint* protocols = stackalloc nint[1] { _deleteWindowAtom };
        XSetWMProtocols(_display, _window, protocols, 1);

        SetStandardProperties();
        OpenInputMethod();

        int screenW = XDisplayWidth(_display, screen);
        int screenH = XDisplayHeight(_display, screen);
        XMoveWindow(_display, _window, Math.Max(0, (screenW - _width) / 2), Math.Max(0, (screenH - _height) / 2));

        XMapWindow(_display, _window);
        XSetInputFocus(_display, _window, 0 /* RevertToParent */, IntPtr.Zero /* CurrentTime */);
        XFlush(_display);

        EnsureFramebuffers();
    }

    private void SetStandardProperties()
    {
        XSizeHints* hints = stackalloc XSizeHints[1];
        hints->flags = PMinSize | PMaxSize | PResizeInc;
        hints->min_width = 1;
        hints->min_height = 1;
        hints->max_width = 1 << 20;
        hints->max_height = 1 << 20;
        hints->width_inc = 1;
        hints->height_inc = 1;

        nint resName = Marshal.StringToHGlobalAnsi("acrux");
        nint resClass = Marshal.StringToHGlobalAnsi("Acrux");
        try
        {
            XClassHint classHint = new XClassHint { res_name = resName, res_class = resClass };
            XSetWMProperties(_display, _window, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, hints, &classHint, IntPtr.Zero);
        }
        finally
        {
            Marshal.FreeHGlobal(resName);
            Marshal.FreeHGlobal(resClass);
        }

        XStoreName(_display, _window, _title);
    }

    private void OpenInputMethod()
    {
        _im = XOpenIM(_display, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (_im == IntPtr.Zero)
            return;

        // XCreateIC is variadic over (key, value) string/atom pairs terminated by
        // NULL. The keys are plain resource strings (XNInputStyle == "inputStyle"),
        // NOT X atoms — passing an interned atom as the key makes Xlib strcmp a
        // small integer pointer and segfaults. Style XIMPreeditNothing|XIMStatusNothing
        // (0x05|0x15) lets the IM own the preedit and deliver committed text via
        // Xutf8LookupString after XFilterEvent releases the event.
        const nint XIMPreeditNothing = 0x05;
        const nint XIMStatusNothing = 0x15;

        nint kStyle = Marshal.StringToHGlobalAnsi("inputStyle");
        nint kClient = Marshal.StringToHGlobalAnsi("clientWindow");
        nint kFocus = Marshal.StringToHGlobalAnsi("focusWindow");
        try
        {
            _ic = XCreateIC(_im,
                kStyle, XIMPreeditNothing | XIMStatusNothing,
                kClient, (nint)_window,
                kFocus, (nint)_window,
                IntPtr.Zero);
        }
        finally
        {
            Marshal.FreeHGlobal(kStyle);
            Marshal.FreeHGlobal(kClient);
            Marshal.FreeHGlobal(kFocus);
        }
    }

    private void EnsureFramebuffers()
    {
        if (_display == IntPtr.Zero) return;

        if (_gc == 0)
            _gc = XCreateGC(_display, _window, 0, IntPtr.Zero);

        if (_fbW == _width && _fbH == _height && _backPixmap != 0 && _ximage != IntPtr.Zero)
            return;

        ulong oldPixmap = _backPixmap;
        int oldW = _fbW, oldH = _fbH;

        uint depth = (uint)XDefaultDepth(_display, XDefaultScreen(_display));
        _backPixmap = XCreatePixmap(_display, _window, (uint)_width, (uint)_height, depth);

        if (_ximage != IntPtr.Zero)
        {
            // XDestroyImage frees the attached buffer (our framebuffer below),
            // so detach first and free it ourselves.
            XImageDetachData(_ximage);
            XDestroyImage(_ximage);
            _ximage = IntPtr.Zero;
        }
        if (_framebuffer != null)
        {
            Marshal.FreeHGlobal((nint)_framebuffer);
            _framebuffer = null;
        }

        _framebuffer = (byte*)Marshal.AllocHGlobal((nint)_width * _height * 4);
        NativeMemory.Clear(_framebuffer, (nuint)(_width * _height * 4));
        _fbW = _width;
        _fbH = _height;

        // 24 bpp over a 32bpp-padded ZPixmap is XRGB8888, which on
        // little-endian machines is byte-identical to Skia's BGRA8888.
        // Hand Xlib the real buffer pointer (not NULL) so XCreateImage owns a
        // valid backing store; we detach before XDestroyImage and free it here.
        IntPtr visual = XDefaultVisual(_display, XDefaultScreen(_display));
        _ximage = XCreateImage(_display, visual, 24, ZPixmap,
            0, (IntPtr)_framebuffer, (uint)_width, (uint)_height, 32 /*bitmap_pad*/, _width * 4 /*bytes_per_line*/);
        if (_ximage == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                $"XCreateImage failed (visual=0x{visual.ToInt64():X}, depth=24, {_width}x{_height}).");
        }

        if (oldPixmap != 0)
        {
            // Preserve visible pixels across the resize so newly exposed strips
            // briefly show the previous frame instead of black.
            int copyW = Math.Min(oldW, _width);
            int copyH = Math.Min(oldH, _height);
            if (copyW > 0 && copyH > 0)
                XCopyArea(_display, oldPixmap, _backPixmap, _gc, 0, 0, (uint)copyW, (uint)copyH, 0, 0);
            XFreePixmap(_display, oldPixmap);
        }
    }

    // Measured against /usr/include/X11/Xlib.h on x86-64 LP64:
    //   XImage:  int width@0, height@4, xoffset@8, format@12, char *data@16
    //   XClientMessageEvent: window@32, message_type@40, format@48, data@56
    //   XKeyEvent: window@32, x@64, y@68, state@80, keycode@84
    //   XConfigureEvent: window@40, x@48, y@52, width@56, height@60
    //   XFocusChangeEvent: mode@40
    //   sizeof(XEvent) == 192
    private const int XImageDataOffset = 16;

    private static void XImageDetachData(IntPtr image) => *(byte**)((byte*)image + XImageDataOffset) = null;

    // ------------------------------------------------------------------
    // Event loop
    // ------------------------------------------------------------------
    public void Run(Action<double> onFrame)
    {
        if (_display == IntPtr.Zero) return;

        _onFrame = onFrame;
        _lastFrameTime = DateTime.Now;
        _isRunning = true;

        while (_isRunning)
        {
            while (_isRunning && XPending(_display) > 0)
                PumpOneEvent();

            if (!_isRunning) break;

            var now = DateTime.Now;
            var dt = (now - _lastFrameTime).TotalSeconds;
            double targetDt = _targetFrameTimeMs / 1000.0;

            if (dt >= targetDt)
            {
                _lastFrameTime = now;
                try
                {
                    onFrame(dt);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[LinuxWindow.Run] _onFrame crashed: {ex.GetType().FullName}: {ex.Message}");
                    Console.WriteLine(ex.StackTrace);
                    try { File.WriteAllText("acrux_frame_crash.log", ex.ToString()); } catch { }
                    throw;
                }
            }
            else if (XPending(_display) == 0)
            {
                int sleepMs = Math.Max(1, (int)((targetDt - dt) * 1000.0));
                Thread.Sleep(sleepMs);
            }
        }

        Cleanup();
    }

    private void PumpOneEvent()
    {
        if (_display == IntPtr.Zero || XPending(_display) <= 0) return;

        Span<byte> buffer = stackalloc byte[EventBufferSize];
        fixed (byte* p = buffer)
        {
            XNextEvent(_display, (nint)p);
            DispatchEvent(p);
        }
    }

    private void DispatchEvent(byte* p)
    {
        int type = *(int*)(p + 0);

        // Give the input method first pass at key events (XIM composition).
        if ((type == KeyPress || type == KeyRelease) && _ic != IntPtr.Zero)
        {
            if (XFilterEvent(_display, (nint)p, _window) != 0)
                return;
        }

        switch (type)
        {
            case KeyPress: HandleKeyPress(p); break;
            case KeyRelease: HandleKeyRelease(p); break;
            case ButtonPress: HandleButton(p, press: true); break;
            case ButtonRelease: HandleButton(p, press: false); break;
            case MotionNotify:
            {
                var ev = (XMotionEvent*)p;
                OnMouseMove?.Invoke(ev->x, ev->y);
                break;
            }
            case FocusIn:
                if (((XFocusChangeEvent*)p)->mode == NotifyNormal)
                {
                    if (_ic != IntPtr.Zero) XSetICFocus(_ic);
                    OnSetFocus?.Invoke();
                }
                break;
            case FocusOut:
                if (((XFocusChangeEvent*)p)->mode == NotifyNormal)
                {
                    if (_ic != IntPtr.Zero) XUnsetICFocus(_ic);
                    OnKillFocus?.Invoke();
                }
                break;
            case Expose:
                RedrawWindow();
                break;
            case ConfigureNotify: HandleConfigure(p); break;
            case DestroyNotify:
                if (((XDestroyWindowEvent*)p)->window == _window)
                    _isRunning = false;
                break;
            case ClientMessage: HandleClientMessage(p); break;
        }
    }

    private void HandleClientMessage(byte* p)
    {
        // XClientMessageEvent (LP64): window@32, message_type@40, format@48, data@56
        ulong messageType = *(ulong*)(p + 40);
        if (messageType == (ulong)_protocolsAtom)
        {
            int data0 = *(int*)(p + 56);
            if (data0 == (int)_deleteWindowAtom)
                Close();
        }
    }

    // ------------------------------------------------------------------
    // Keyboard
    // ------------------------------------------------------------------
    private void HandleKeyPress(byte* p)
    {
        uint keycode = ((XKeyEvent*)p)->keycode;

        Span<byte> lookup = stackalloc byte[64];
        fixed (byte* lookupPtr = lookup)
        {
            int n, keysym;
            if (_ic != IntPtr.Zero)
                n = Xutf8LookupString(_ic, (nint)p, lookupPtr, 64, out keysym, out _);
            else
                n = XLookupString((nint)p, lookupPtr, 64, out keysym, out _);
            char ch = DecodeChar(lookup.Slice(0, Math.Max(0, n)), keysym);

            if (ch != '\0' && OnKeyDownWithChar != null)
            {
                // Mirrors Windows WM_CHAR: the char-carrying callback receives
                // Key.Unknown whenever the keysym is printable text, so the app
                // does not also run the special-key switch for it.
                Key key = IsTextKeysym(keysym) ? Key.Unknown : MapKeysym(RawKeysym(keycode) != 0 ? RawKeysym(keycode) : keysym);
                if (OnKeyDownWithChar(ch, key))
                    return;
            }

            Key physical = MapKeysym(RawKeysym(keycode) != 0 ? RawKeysym(keycode) : keysym);
            if (physical != Key.Unknown)
                OnKeyDown?.Invoke(physical);

            if (ch != '\0' && OnKeyDownWithChar == null)
                OnChar?.Invoke(ch);
        }
    }

    /// <summary>True when a keysym denotes printable text rather than a control key.</summary>
    private static bool IsTextKeysym(int keysym) =>
        (keysym >= 0x20 && keysym <= 0x7E) || (keysym >= 0xA0 && keysym != 0xAD && keysym <= 0xFF)
        || keysym >= 0x01000000;

    private void HandleKeyRelease(byte* p)
    {
        uint keycode = ((XKeyEvent*)p)->keycode;
        int raw = RawKeysym(keycode);
        Key key = MapKeysym(raw);
        if (key != Key.Unknown)
            OnKeyUp?.Invoke(key);
    }

    /// <summary>Keysym of the physical key ignoring shift state (VK-style semantics).</summary>
    private int RawKeysym(uint keycode)
        => _xkbKeycodeToKeysym != null && keycode != 0 && keycode < 256
            ? _xkbKeycodeToKeysym(_display, keycode, 0, 0)
            : 0;

    private static char DecodeChar(ReadOnlySpan<byte> utf8, int keysym)
    {
        if (utf8.Length > 0)
        {
            // XLookupString yields Latin-1 or UTF-8 bytes of the typed character.
            if (utf8.Length == 1)
                return (char)utf8[0];
            try
            {
                Span<char> chars = stackalloc char[4];
                int written = Encoding.UTF8.GetChars(utf8, chars);
                if (written > 0) return chars[0];
            }
            catch { }
        }

        // Unicode-plane keysym (0x01000000 | codepoint) or direct Latin-1 keysym.
        if (keysym >= 0x01000000 && keysym <= 0x0110FFFF)
            return (char)(keysym & 0x10FFFF);
        if (keysym >= 0x20 && keysym <= 0x7E)
            return (char)keysym;
        return '\0';
    }

    // ------------------------------------------------------------------
    // Mouse
    // ------------------------------------------------------------------
    private void HandleButton(byte* p, bool press)
    {
        var ev = (XButtonEvent*)p;
        ulong window = ev->window;
        uint button = ev->button;

        // Wheel buttons arrive as press+release pairs; only the press is real.
        if (button is >= 4 and <= 7)
        {
            if (press)
            {
                switch (button)
                {
                    case 4: OnMouseWheel?.Invoke(0, 120); break;    // up, matches WHEEL_DELTA scale
                    case 5: OnMouseWheel?.Invoke(0, -120); break;   // down
                    case 6: OnMouseWheel?.Invoke(-120, 0); break;   // left
                    case 7: OnMouseWheel?.Invoke(120, 0); break;    // right
                }
            }
            return;
        }

        if (window == _root)
        {
            // Press on the root means the window manager owns the pointer
            // (title-bar / border drag). Combine with ConfigureNotify to infer
            // an interactive size/move drag.
            _rootButtonHeld = press;
            if (!press && _inSizeMove)
            {
                _inSizeMove = false;
                // Drag settled: render one more frame so the renderer returns to
                // the tile path and warms the cache at the final size.
                PumpFrameAfterResize();
            }
            return;
        }

        if (button is 1 or 2 or 3)
            OnMouseClick?.Invoke(ev->x, ev->y, press);
    }

    private void HandleConfigure(byte* p)
    {
        var ev = (XConfigureEvent*)p;
        if (ev->window != _window)
            return;

        int w = Math.Max(1, ev->width);
        int h = Math.Max(1, ev->height);

        bool sizeChanged = w != _width || h != _height;
        bool moved = ev->x != _lastConfigX || ev->y != _lastConfigY;
        _lastConfigX = ev->x;
        _lastConfigY = ev->y;

        if (sizeChanged)
        {
            _width = w;
            _height = h;
            EnsureFramebuffers();
        }

        if (_rootButtonHeld && (sizeChanged || moved))
            _inSizeMove = true;

        if (sizeChanged)
        {
            // Re-layout and render on EVERY resize tick so the page tracks the
            // drag live; IsInSizeMove keeps each tick cheap during the drag.
            PumpFrameAfterResize();
        }
        else
        {
            RedrawWindow();
        }
    }

    private void PumpFrameAfterResize()
    {
        if (_onFrame != null && _width > 0 && _height > 0)
        {
            _lastFrameTime = DateTime.Now;
            try { _onFrame(0.016); }
            catch (Exception ex)
            {
                Console.WriteLine($"[LinuxWindow] resize frame crashed: {ex.Message}");
            }
        }
    }

    // ------------------------------------------------------------------
    // Rendering
    // ------------------------------------------------------------------
    public void Render(byte[] pixels, int width, int height)
    {
        if (_display == IntPtr.Zero || _window == 0 || _ximage == IntPtr.Zero ||
            pixels.Length == 0 || width <= 0 || height <= 0)
            return;

        if (width == _fbW && height == _fbH)
        {
            fixed (byte* src = pixels)
                Buffer.MemoryCopy(src, _framebuffer, (nint)_fbW * _fbH * 4, (nint)_fbW * _fbH * 4);
            XPutImage(_display, _backPixmap, _gc, _ximage, 0, 0, 0, 0, (uint)width, (uint)height);
        }
        else
        {
            // ResolutionScale != 1: copy the overlapping region 1:1; the rest
            // keeps the previous frame (stretched presentation is handled by
            // the caller resizing its surface).
            int copyW = Math.Min(width, _fbW);
            int copyH = Math.Min(height, _fbH);
            fixed (byte* src = pixels)
            {
                for (int y = 0; y < copyH; y++)
                    Buffer.MemoryCopy(src + (long)y * width * 4, _framebuffer + (long)y * _fbW * 4, _fbW * 4, copyW * 4);
            }
            XPutImage(_display, _backPixmap, _gc, _ximage, 0, 0, 0, 0, (uint)copyW, (uint)copyH);
        }

        RedrawWindow();
    }

    private void RedrawWindow()
    {
        if (_display == IntPtr.Zero || _window == 0 || _backPixmap == 0) return;
        XCopyArea(_display, _backPixmap, _window, _gc, 0, 0, (uint)_fbW, (uint)_fbH, 0, 0);
        XFlush(_display);
    }

    public bool PumpPendingMessage()
    {
        // Also used by nested dialog loops, which may run before the main Run
        // loop sets _isRunning — never gate on it here.
        if (_display == IntPtr.Zero) return false;
        if (XPending(_display) <= 0) return false;

        PumpOneEvent();
        return _isRunning;
    }

    public void Close()
    {
        _isRunning = false;
        if (_display != IntPtr.Zero)
        {
            if (_ic != IntPtr.Zero) { XDestroyIC(_ic); _ic = IntPtr.Zero; }
            if (_im != IntPtr.Zero) { XCloseIM(_im); _im = IntPtr.Zero; }
            XSelectInput(_display, _root, 0);
            XDestroyWindow(_display, _window);
            XCloseDisplay(_display);
            _display = IntPtr.Zero;
            _window = 0;
        }
        ReleaseBuffers();
    }

    private void Cleanup()
    {
        Close();
    }

    private void ReleaseBuffers()
    {
        if (_ximage != IntPtr.Zero)
        {
            XImageDetachData(_ximage);
            XDestroyImage(_ximage);
            _ximage = IntPtr.Zero;
        }
        if (_framebuffer != null)
        {
            Marshal.FreeHGlobal((nint)_framebuffer);
            _framebuffer = null;
        }
        // Pixmap and GC died with the display connection; no explicit free needed.
        _backPixmap = 0;
        _gc = 0;
        _fbW = _fbH = 0;
    }

    public void Dispose()
    {
        if (_disposed) return;
        Cleanup();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    // ------------------------------------------------------------------
    // Key mapping: X keysym -> VK-style Key enum used across the app.
    // ------------------------------------------------------------------
    private static Key MapKeysym(int keysym) => keysym switch
    {
        >= 'A' and <= 'Z' => (Key)keysym,
        >= 'a' and <= 'z' => (Key)(keysym - 32),
        >= '0' and <= '9' => (Key)keysym,
        0x20 => Key.Space,
        0xFF0D or 0xFF8D => Key.Enter,       // Return / KP_Enter
        0xFF09 => Key.Tab,
        0xFF1B => Key.Escape,
        0xFF08 => Key.Backspace,
        0xFFFF => Key.Delete,
        0xFF63 => Key.Insert,
        0xFF50 => Key.Home,
        0xFF57 => Key.End,
        0xFF55 => Key.PageUp,
        0xFF56 => Key.PageDown,
        0xFF51 => Key.Left,
        0xFF52 => Key.Up,
        0xFF53 => Key.Right,
        0xFF54 => Key.Down,
        0xFFE1 or 0xFFE2 => Key.Shift,
        0xFFE3 or 0xFFE4 => Key.Ctrl,
        0xFFE9 or 0xFFEA => Key.Alt,
        0xFFEB or 0xFFEC => Key.Alt,          // AltGr
        0xFF67 => Key.Alt,                    // Meta_R used as Alt on many layouts
        0xFFE7 or 0xFFE8 => Key.LCmd,         // Super_L / Super_R
        0xFFE5 => Key.CapsLock,
        0xFFBE => Key.F1,
        0xFFBF => Key.F2,
        0xFFC0 => Key.F3,
        0xFFC1 => Key.F4,
        0xFFC2 => Key.F5,
        0xFFC3 => Key.F6,
        0xFFC4 => Key.F7,
        0xFFC5 => Key.F8,
        0xFFC6 => Key.F9,
        0xFFC7 => Key.F10,
        0xFFC8 => Key.F11,
        0xFFC9 => Key.F12,
        0xFFD0 => Key.F13,
        0xFFD1 => Key.F14,
        0xFFD2 => Key.F15,
        0xFFD3 => Key.F16,
        0xFFD4 => Key.F17,
        0xFFD5 => Key.F18,
        0xFFD6 => Key.F19,
        // Numpad
        >= 0xFFB0 and <= 0xFFB9 => (Key)(keysym - 0xFFB0 + '0'),
        0xFFAB => Key.KeypadPlus,
        0xFFAD => Key.KeypadMinus,
        0xFFAA => Key.KeypadMultiply,
        0xFFAF => Key.KeypadDivide,
        0xFFAE => Key.KeypadDecimal,
        _ => Key.Unknown,
    };

    // ------------------------------------------------------------------
    // Blittable Xlib event structs (LP64 layout), read via pointers.
    // ------------------------------------------------------------------
    [StructLayout(LayoutKind.Sequential)]
    private struct XKeyEvent
    {
        public int type; public ulong serial; public int send_event; public IntPtr display;
        public ulong window, root, subwindow;
        public IntPtr time;
        public int x, y, x_root, y_root;
        public uint state; public uint keycode; public int same_screen;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XButtonEvent
    {
        public int type; public ulong serial; public int send_event; public IntPtr display;
        public ulong window, root, subwindow;
        public IntPtr time;
        public int x, y, x_root, y_root;
        public uint state; public uint button; public int same_screen;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XMotionEvent
    {
        public int type; public ulong serial; public int send_event; public IntPtr display;
        public ulong window, root, subwindow;
        public IntPtr time;
        public int x, y, x_root, y_root;
        public uint state; public int is_hint; public int same_screen;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XFocusChangeEvent
    {
        public int type; public ulong serial; public int send_event; public IntPtr display;
        public ulong window; public int mode; public int detail;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XDestroyWindowEvent
    {
        public int type; public ulong serial; public int send_event; public IntPtr display;
        public ulong window, root;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XConfigureEvent
    {
        public int type; public ulong serial; public int send_event; public IntPtr display;
        public ulong window, root;
        public int x, y, width, height;
        public int border_width; public ulong above; public int override_redirect;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XSizeHints
    {
        public long flags;
        public int x, y, width, height;
        public int min_width, min_height, max_width, max_height;
        public int width_inc, height_inc;
        public int min_aspect_x, min_aspect_y, max_aspect_x, max_aspect_y;
        public int grab_index, grab_offset;
    }

    private const long PResizeInc = 64;
    private const long PMinSize = 16;
    private const long PMaxSize = 32;

    [StructLayout(LayoutKind.Sequential)]
    private struct XClassHint
    {
        public nint res_name;
        public nint res_class;
    }

    // ------------------------------------------------------------------
    // P/Invoke (libX11.so.6)
    // ------------------------------------------------------------------
    private const string libX11 = "libX11.so.6";

    [DllImport(libX11)] private static extern IntPtr XOpenDisplay(IntPtr name);
    [DllImport(libX11)] private static extern int XCloseDisplay(IntPtr display);
    [DllImport(libX11)] private static extern int XDefaultScreen(IntPtr display);
    [DllImport(libX11)] private static extern int XDisplayWidth(IntPtr display, int screen);
    [DllImport(libX11)] private static extern int XDisplayHeight(IntPtr display, int screen);
    [DllImport(libX11)] private static extern ulong XRootWindow(IntPtr display, int screen);
    [DllImport(libX11)] private static extern IntPtr XDefaultVisual(IntPtr display, int screen);
    [DllImport(libX11)] private static extern int XDefaultDepth(IntPtr display, int screen);
    [DllImport(libX11)] private static extern ulong XBlackPixel(IntPtr display, int screen);
    [DllImport(libX11)] private static extern ulong XCreateSimpleWindow(IntPtr display, ulong parent,
        int x, int y, uint w, uint h, uint bw, ulong border, ulong background);
    [DllImport(libX11)] private static extern int XSelectInput(IntPtr display, ulong window, long mask);
    [DllImport(libX11)] private static extern int XStoreName(IntPtr display, ulong window, [MarshalAs(UnmanagedType.LPStr)] string name);
    [DllImport(libX11)] private static extern int XSetWMProperties(IntPtr display, ulong window,
        IntPtr windowName, IntPtr iconName, IntPtr argv, int argc,
        XSizeHints* normalHints, XClassHint* classHints, IntPtr wmHints);
    [DllImport(libX11)] private static extern int XSetWMProtocols(IntPtr display, ulong window, nint* protocols, int count);
    [DllImport(libX11)] private static extern nint XInternAtom(IntPtr display, [MarshalAs(UnmanagedType.LPStr)] string name, int onlyIfExists);
    [DllImport(libX11)] private static extern int XMapWindow(IntPtr display, ulong window);
    [DllImport(libX11)] private static extern int XMoveWindow(IntPtr display, ulong window, int x, int y);
    [DllImport(libX11)] private static extern int XSetInputFocus(IntPtr display, ulong window, int revertTo, IntPtr time);
    [DllImport(libX11)] private static extern int XFlush(IntPtr display);
    [DllImport(libX11)] private static extern int XPending(IntPtr display);
    [DllImport(libX11)] private static extern int XNextEvent(IntPtr display, nint eventReturn);
    [DllImport(libX11)] private static extern int XLookupString(nint keyEvent, byte* buffer, int buflen, out int keysymReturn, out IntPtr statusReturn);
    [DllImport(libX11)] private static extern int Xutf8LookupString(IntPtr ic, nint keyEvent, byte* buffer, int buflen, out int keysymReturn, out IntPtr statusReturn);
    [DllImport(libX11)] private static extern int XFilterEvent(IntPtr display, nint eventPtr, ulong window);
    [DllImport(libX11)] private static extern ulong XCreateGC(IntPtr display, ulong drawable, ulong mask, IntPtr values);
    [DllImport(libX11)] private static extern ulong XCreatePixmap(IntPtr display, ulong drawable, uint w, uint h, uint depth);
    [DllImport(libX11)] private static extern int XFreePixmap(IntPtr display, ulong pixmap);
    [DllImport(libX11)] private static extern IntPtr XCreateImage(IntPtr display, IntPtr visual, uint depth,
        int format, int offset, IntPtr data, uint width, uint height, int bitmapPad, int bytesPerLine);
    [DllImport(libX11)] private static extern int XDestroyImage(IntPtr image);
    [DllImport(libX11)] private static extern int XPutImage(IntPtr display, ulong drawable, ulong gc, IntPtr image,
        int srcX, int srcY, int destX, int destY, uint width, uint height);
    [DllImport(libX11)] private static extern int XCopyArea(IntPtr display, ulong src, ulong dst, ulong gc,
        int srcX, int srcY, uint w, uint h, int dstX, int dstY);
    [DllImport(libX11)] private static extern int XDestroyWindow(IntPtr display, ulong window);
    [DllImport(libX11)] private static extern IntPtr XOpenIM(IntPtr display, IntPtr db, IntPtr resName, IntPtr resClass);
    [DllImport(libX11)] private static extern int XCloseIM(IntPtr im);
    [DllImport(libX11)] private static extern IntPtr XCreateIC(IntPtr im, nint k0, nint v0, nint k1, nint v1, nint k2, nint v2, nint end);
    [DllImport(libX11)] private static extern int XDestroyIC(IntPtr ic);
    [DllImport(libX11)] private static extern int XSetICFocus(IntPtr ic);
    [DllImport(libX11)] private static extern int XUnsetICFocus(IntPtr ic);
    [DllImport(libX11, EntryPoint = "XSetErrorHandler")] private static extern IntPtr XSetErrorHandler(IntPtr handler);

    private delegate int XkbKeycodeToKeysymDelegate(IntPtr display, uint keycode, int group, int level);
    private static readonly XkbKeycodeToKeysymDelegate? _xkbKeycodeToKeysym = ResolveXkb();

    private static XkbKeycodeToKeysymDelegate? ResolveXkb()
    {
        try
        {
            var lib = NativeLibrary.Load(libX11);
            if (NativeLibrary.TryGetExport(lib, "XkbKeycodeToKeysym", out var addr))
                return Marshal.GetDelegateForFunctionPointer<XkbKeycodeToKeysymDelegate>(addr);
        }
        catch { }
        return null;
    }

    [UnmanagedCallersOnly]
    private static int StaticErrorHandler(IntPtr display, IntPtr eventErrorEvent) => 0;
}
