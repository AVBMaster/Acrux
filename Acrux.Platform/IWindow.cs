using Acrux.Core;
using Acrux.Platform;

namespace Acrux.Platform;

public interface IWindow : IDisposable
{
    int Width { get; }
    int Height { get; }

    Action<char>? OnChar { get; set; }
    Action<char>? OnImeChar { get; set; }
    Func<char, Key, bool>? OnKeyDownWithChar { get; set; }
    Action<Key>? OnKeyDown { get; set; }
    Action<Key>? OnKeyUp { get; set; }
    Action<float, float>? OnMouseMove { get; set; }
    Action<float, float, bool>? OnMouseClick { get; set; }
    /// <summary>
    /// 滚动事件参数: deltaX (水平), deltaY (垂直)
    /// </summary>
    Action<double, double>? OnMouseWheel { get; set; }
    Action<float>? OnDpiChanged { get; set; }
    Action? OnSetFocus { get; set; }
    Action? OnKillFocus { get; set; }

    IImeHandler? ImeHandler { get; }

    void SetImeTarget(IImeSupport? target);
    void UpdateImeCompositionWindow();

    (int width, int height) GetClientSize();
    float TargetFrameTimeMs { get; set; }
    void Run(Action<double> onFrame);
    void Render(byte[] pixels, int width, int height);
    void Close();
    bool PumpPendingMessage();

    /// <summary>
    /// 获取底层平台窗口句柄 (Windows: HWND, Linux: Display/Drawable, Mac: CGWindowID).
    /// </summary>
    IntPtr? GetNativeHandle() => null;

    /// <summary>
    /// True while an interactive resize/move drag is in flight (Windows: between
    /// WM_ENTERSIZEMOVE and WM_EXITSIZEMOVE). The renderer uses this to switch to
    /// a cheap whole-page direct draw during the drag instead of re-rasterizing
    /// the tile cache on every resize tick.
    /// </summary>
    bool IsInSizeMove => false;
}