using System.Runtime.InteropServices;
using System.Text;

namespace Acrux.Platform.Windows;

public static class NativeWindow
{
    public delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct WNDCLASSW
    {
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public POINT pt;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    public const uint WM_DESTROY = 0x0002;
    public const uint WM_PAINT = 0x000F;
    public const uint WM_QUIT = 0x0012;
    public const uint WM_SIZE = 0x0005;
    public const uint WM_CLOSE = 0x0010;
    public const uint WM_ERASEBKGND = 0x0014;
    public const uint WM_NCPAINT = 0x00B;
    public const uint WM_ENTERSIZEMOVE = 0x0231;
    public const uint WM_EXITSIZEMOVE = 0x0232;
    public const uint WM_KEYDOWN = 0x0100;
    public const uint WM_KEYUP = 0x0101;
    public const uint WM_CHAR = 0x0102;
    public const uint WM_MOUSEMOVE = 0x0200;
    public const uint WM_LBUTTONDOWN = 0x0201;
    public const uint WM_LBUTTONUP = 0x0202;
    public const uint WM_RBUTTONDOWN = 0x0204;
    public const uint WM_RBUTTONUP = 0x0205;
    public const uint WM_MOUSEWHEEL = 0x020A;
    public const uint WM_IME_STARTCOMPOSITION = 0x010D;
    public const uint WM_IME_ENDCOMPOSITION = 0x010E;
    public const uint WM_IME_COMPOSITION = 0x010F;
    public const uint WM_IME_NOTIFY = 0x0282;
    public const uint WM_IME_CHAR = 0x0286;

    public const uint WM_SETFOCUS = 0x0007;
    public const uint WM_KILLFOCUS = 0x0008;

    public const uint WM_CAPTURECHANGED = 0x0215;
    public const uint WM_INPUTLANGCHANGEREQUEST = 0x0050;
    public const uint WM_INPUTLANGCHANGE = 0x0051;

    public const int CW_USEDEFAULT = -1;

    public const int SW_SHOWNORMAL = 1;

    public const uint WS_OVERLAPPED = 0x00000000;
    public const uint WS_CAPTION = 0x00C00000;
    public const uint WS_SYSMENU = 0x00080000;
    public const uint WS_MINIMIZEBOX = 0x00020000;
    public const uint WS_MAXIMIZEBOX = 0x00010000;
    public const uint WS_OVERLAPPEDWINDOW = 0x00CF0000;
    public const uint WS_VISIBLE = 0x10000000;

    public const uint CS_HREDRAW = 0x0001;
    public const uint CS_VREDRAW = 0x0002;
    public const uint CS_OWNDC = 0x0020;

    public static readonly IntPtr HINSTANCE_ZERO = IntPtr.Zero;
    public static readonly IntPtr HWND_DESKTOP = IntPtr.Zero;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr LoadCursorW(IntPtr hInstance, IntPtr lpCursorName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern ushort RegisterClassW(ref WNDCLASSW lpWndClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr CreateWindowExW(
        uint dwExStyle,
        string lpClassName,
        string lpWindowName,
        uint dwStyle,
        int X,
        int Y,
        int nWidth,
        int nHeight,
        IntPtr hWndParent,
        IntPtr hMenu,
        IntPtr hInstance,
        IntPtr lpParam);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    public static extern bool UpdateWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern int GetMessageW(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    public static extern bool PeekMessageW(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

    public const uint QS_ALLINPUT = 0x000004FF;

    [DllImport("user32.dll")]
    public static extern uint MsgWaitForMultipleObjects(uint nCount, IntPtr[]? pHandles, bool fWaitAll, uint dwMilliseconds, uint dwWakeMask);

    [DllImport("user32.dll")]
    public static extern int TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    public static extern IntPtr DispatchMessageW(ref MSG lpMsg);

    [DllImport("user32.dll")]
    public static extern void PostQuitMessage(int nExitCode);

    [DllImport("user32.dll")]
    public static extern IntPtr DefWindowProcW(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr GetModuleHandleW(string? lpModuleName);

    [DllImport("user32.dll")]
    public static extern int GetSystemMetrics(int nIndex);

    public const int SM_CXSCREEN = 0;
    public const int SM_CYSCREEN = 1;

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool GetMonitorInfoW(IntPtr hMonitor, ref MONITORINFO lpmi);

    public const uint MONITOR_DEFAULTTONEAREST = 2;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct MONITORINFO
    {
        public uint cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;

        public static MONITORINFO Create()
        {
            var mi = new MONITORINFO();
            mi.cbSize = (uint)Marshal.SizeOf(mi);
            return mi;
        }
    }

    public static IntPtr IDC_ARROW = new IntPtr(32512);
    public static IntPtr HINSTANCE_CURRENT = IntPtr.Zero;

    private static ushort _classRegistered = 0;
    private static string _className = "AcruxWindowClass";

    public static ushort GetOrRegisterClass(WndProc wndProc, IntPtr hInstance)
    {
        if (_classRegistered != 0) return _classRegistered;

        IntPtr wndProcPtr = Marshal.GetFunctionPointerForDelegate(wndProc);

        var wc = new WNDCLASSW
        {
            style = CS_HREDRAW | CS_VREDRAW | CS_OWNDC,
            lpfnWndProc = wndProcPtr,
            cbClsExtra = 0,
            cbWndExtra = 0,
            hInstance = hInstance,
            hIcon = IntPtr.Zero,
            hCursor = LoadCursorW(HINSTANCE_CURRENT, IDC_ARROW),
            hbrBackground = IntPtr.Zero,
            lpszMenuName = null,
            lpszClassName = _className
        };

        _classRegistered = RegisterClassW(ref wc);
        return _classRegistered;
    }

    public static string GetClassName() => _className;

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFO
    {
        public BITMAPINFOHEADER bmiHeader;
        public int bmiColors;
    }

    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int nWidth, int nHeight);

    [DllImport("gdi32.dll")]
    public static extern IntPtr SelectObject(IntPtr hdc, IntPtr hObject);

    // The trailing iStretchMode is required: omitting it leaves the value
    // undefined on the stack, and a HALFTONE (4) resolution makes GDI colour-
    // halftone-interpolate every pixel on every frame — including a 1:1 copy —
    // which softens the whole frame and shifts edge colours.
    [DllImport("gdi32.dll")]
    public static extern bool StretchDIBits(
        IntPtr hdc,
        int XDest,
        int YDest,
        int nDestWidth,
        int nDestHeight,
        int XSrc,
        int YSrc,
        int nSrcWidth,
        int nSrcHeight,
        byte[] lpBits,
        ref BITMAPINFO lpBitsInfo,
        uint usage,
        uint rop,
        int iStretchMode);

    [DllImport("gdi32.dll")]
    public static extern bool SetDIBitsToDevice(
        IntPtr hdc,
        int XDest,
        int YDest,
        int nWidth,
        int nHeight,
        int XSrc,
        int YSrc,
        int nSrcStart,
        int nSrcLines,
        byte[] lpBits,
        ref BITMAPINFO lpBitsInfo,
        uint usage);

    [DllImport("gdi32.dll")]
    public static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    public static extern bool DeleteObject(IntPtr hObject);

    [DllImport("gdi32.dll", EntryPoint = "CreateDIBSection", CallingConvention = CallingConvention.StdCall)]
    private static extern IntPtr CreateDIBSectionInternal(IntPtr hdc, ref BITMAPINFO pbmi, uint usage, out IntPtr ppvBits, IntPtr hSection, uint offset);

    public static IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFO pbmi, uint usage, out IntPtr ppvBits, IntPtr hSection, uint offset)
    {
        return CreateDIBSectionInternal(hdc, ref pbmi, usage, out ppvBits, hSection, offset);
    }

    [DllImport("gdi32.dll")]
    public static extern bool BitBlt(IntPtr hdcDest, int xDest, int yDest, int width, int height, IntPtr hdcSrc, int xSrc, int ySrc, uint rop);

    // Used only by the WM_PAINT fallback blit: while a resize drag is in flight
    // the last composed frame is stretched over the (possibly resized) client so
    // the exposed region shows scaled content instead of the unpainted black.
    [DllImport("gdi32.dll")]
    public static extern bool StretchBlt(IntPtr hdcDest, int xDest, int yDest, int wDest, int hDest,
        IntPtr hdcSrc, int xSrc, int ySrc, int wSrc, int hSrc, uint rop);

    [DllImport("gdi32.dll")]
    public static extern int SetStretchBltMode(IntPtr hdc, int mode);

    [DllImport("gdi32.dll")]
    public static extern bool SetBrushOrgEx(IntPtr hdc, int xOrg, int yOrg, IntPtr lpPoint);

    // StretchBlt halftone mode: the highest-quality scale. Acceptable here because
    // it only runs transiently during a resize drag; the crisp frame replaces it on
    // WM_EXITSIZEMOVE.
    public const int HALFTONE = 4;

    [DllImport("user32.dll")]
    public static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    public static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("user32.dll")]
    public static extern IntPtr SetCapture(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool ReleaseCapture();

    public const uint DIB_RGB_COLORS = 0;
    public const uint SRCCOPY = 0x00CC0020;

    // GDI stretch modes. BLACKONWHITE is a pixel-for-pixel copy (no filtering);
    // COLORONCOLOR preserves colour while interpolating, so a genuine scale-up
    // does not wash the palette. HALFTONE (4) and COLORONWHITE (2) are the modes
    // that produce visible blur on text.
    public const int BLACKONWHITE = 0;
    public const int COLORONCOLOR = 1;

    [DllImport("user32.dll")]
    public static extern bool InvalidateRect(IntPtr hWnd, IntPtr lpRect, bool bErase);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    public static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

    [DllImport("user32.dll")]
    public static extern IntPtr BeginPaint(IntPtr hWnd, out PAINTSTRUCT lpPaint);

    [DllImport("user32.dll")]
    public static extern bool EndPaint(IntPtr hWnd, ref PAINTSTRUCT lpPaint);

    [StructLayout(LayoutKind.Sequential)]
    public struct PAINTSTRUCT
    {
        public IntPtr hdc;
        public int fErase;
        public RECT rcPaint;
        public int fRestore;
        public int fIncUpdate;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public byte[] rgbReserved;
    }
}