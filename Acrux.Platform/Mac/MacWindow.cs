using System.Runtime.InteropServices;
using Acrux.Core;

namespace Acrux.Platform.Mac;

public class MacWindow : IWindow
{
    private bool _disposed;
    private int _width;
    private int _height;
    private string _title;
    private Action<double>? _onFrame;
    private bool _running;
    private byte[]? _pixelBuffer;

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
    public IImeHandler? ImeHandler => null;
    public float TargetFrameTimeMs { get; set; } = 16f;

    public MacWindow(int width, int height, string title)
    {
        _width = width;
        _height = height;
        _title = title;
    }

    public (int width, int height) GetClientSize() => (_width, _height);
    public void SetImeTarget(IImeSupport? target) { }
    public void UpdateImeCompositionWindow() { }

    public void Run(Action<double> onFrame)
    {
        _onFrame = onFrame;
        _running = true;

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        double lastFrameTime = 0;
        const double frameInterval = 1.0 / 60.0;

        Console.WriteLine($"[macOS] Window '{_title}' created ({_width}x{_height}) - polling mode");

        while (_running)
        {
            var elapsed = stopwatch.Elapsed.TotalSeconds;
            if (elapsed - lastFrameTime >= frameInterval)
            {
                lastFrameTime = elapsed;
                onFrame(elapsed);
            }
            else
            {
                Thread.Sleep(1);
            }
        }
    }

    public void Render(byte[] pixels, int width, int height)
    {
        _pixelBuffer = pixels;
        _width = width;
        _height = height;
    }

    public void Close()
    {
        _running = false;
    }

    public bool PumpPendingMessage()
    {
        return false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        Close();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
