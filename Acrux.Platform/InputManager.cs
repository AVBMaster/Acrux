namespace Acrux.Platform;

public class InputManager
{
    private readonly List<KeyEvent> _keyEvents = new();
    private readonly List<MouseButtonEvent> _mouseButtonEvents = new();
    private readonly List<MouseMoveEvent> _mouseMoveEvents = new();
    private readonly List<ScrollEvent> _scrollEvents = new();

    public IReadOnlyList<KeyEvent> KeyEvents => _keyEvents;
    public IReadOnlyList<MouseButtonEvent> MouseButtonEvents => _mouseButtonEvents;
    public IReadOnlyList<MouseMoveEvent> MouseMoveEvents => _mouseMoveEvents;
    public IReadOnlyList<ScrollEvent> ScrollEvents => _scrollEvents;

    public void HandleKeyEvent(Key key, KeyState state)
    {
        _keyEvents.Add(new KeyEvent(key, state));
    }

    public void HandleMouseButtonEvent(MouseButton button, ButtonState state)
    {
        _mouseButtonEvents.Add(new MouseButtonEvent(button, state));
    }

    public void HandleMouseMove(double x, double y)
    {
        _mouseMoveEvents.Add(new MouseMoveEvent(x, y));
    }

    public void HandleScroll(double offsetX, double offsetY)
    {
        _scrollEvents.Add(new ScrollEvent(offsetX, offsetY));
    }

    public void ProcessEvents()
    {
        _keyEvents.Clear();
        _mouseButtonEvents.Clear();
        _mouseMoveEvents.Clear();
        _scrollEvents.Clear();
    }
}

public enum KeyState { Down, Up }
public enum ButtonState { Down, Up }

public enum Key
{
    Unknown = 0,
    A = 65, B = 66, C = 67, D = 68, E = 69, F = 70, G = 71, H = 72,
    I = 73, J = 74, K = 75, L = 76, M = 77, N = 78, O = 79, P = 80,
    Q = 81, R = 82, S = 83, T = 84, U = 85, V = 86, W = 87, X = 88,
    Y = 89, Z = 90,
    Zero = 48, One = 49, Two = 50, Three = 51, Four = 52,
    Five = 53, Six = 54, Seven = 55, Eight = 56, Nine = 57,
    Minus = 189, Equal = 187,
    LeftBracket = 219, RightBracket = 221,
    Semicolon = 186, Quote = 222,
    Comma = 188, Period = 190, Slash = 191,
    Backslash = 220, Backquote = 192,
    F1 = 112, F2 = 113, F3 = 114, F4 = 115, F5 = 116, F6 = 117,
    F7 = 118, F8 = 119, F9 = 120, F10 = 121, F11 = 122, F12 = 123,
    F13 = 124, F14 = 125, F15 = 126, F16 = 127, F17 = 128,
    F18 = 129, F19 = 130, F20 = 131,
    Escape = 27,
    Space = 32,
    Enter = 13,
    Tab = 9,
    Backspace = 8,
    Delete = 46,
    Insert = 45,
    Left = 37, Up = 38, Right = 39, Down = 40,
    Home = 36,
    End = 35,
    PageUp = 33,
    PageDown = 34,
    Shift = 16,
    Ctrl = 17,
    Alt = 18,
    LShift = 160,
    RShift = 161,
    LCtrl = 162,
    RCtrl = 163,
    LAlt = 164,
    RAlt = 165,
    LCmd = 166,
    RCmd = 167,
    CapsLock = 168,
    Fn = 169,
    Mute = 170,
    VolumeUp = 171,
    VolumeDown = 172,
    KeypadClear = 173,
    KeypadEnter = 174,
    KeypadDecimal = 175,
    KeypadMultiply = 176,
    KeypadPlus = 177,
    KeypadMinus = 178,
    KeypadDivide = 179,
    KeypadEqual = 180,
}

public enum MouseButton
{
    Left = 0,
    Right = 1,
    Middle = 2
}

public record KeyEvent(Key Key, KeyState State);
public record MouseButtonEvent(MouseButton Button, ButtonState State);
public record MouseMoveEvent(double X, double Y);
public record ScrollEvent(double OffsetX, double OffsetY);
