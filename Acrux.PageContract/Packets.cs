namespace Acrux.PageContract;

/// <summary>Axis-aligned rectangle in CSS pixels, viewport-relative.</summary>
public readonly record struct PageRect(float X, float Y, float Width, float Height);

public enum PointerPhase : byte
{
    Down,
    Up,
    Move,
}

/// <summary>Modifier key state, packed to travel with an input event.</summary>
[Flags]
public enum ModifierKeys : int
{
    None = 0,
    Shift = 1 << 0,
    Control = 1 << 1,
    Alt = 1 << 2,
    Meta = 1 << 3,
}

/// <summary>One pointer sample. Coordinates are CSS pixels inside the page viewport.</summary>
public readonly record struct PointerPacket(
    float X, float Y, PointerPhase Phase,
    int Buttons = 0, ModifierKeys Modifiers = ModifierKeys.None, int ClickCount = 1);

/// <summary>Wheel delta plus the pointer position it happened over.</summary>
public readonly record struct WheelPacket(
    float DeltaX, float DeltaY, float X, float Y,
    ModifierKeys Modifiers = ModifierKeys.None);

/// <summary>Raw key press. <paramref name="CharCode"/> is the legacy DOM keyCode the engine's key handlers read.</summary>
public readonly record struct KeyPacket(
    ushort CharCode, ushort Key, bool IsRepeat, ModifierKeys Modifiers = ModifierKeys.None);

public enum ImePhase : byte
{
    /// <summary>Composition string changed; the page shows it underlined at the caret.</summary>
    Update,
    /// <summary>Composition finished; insert <paramref name="Text"/> and fire input.</summary>
    Commit,
    /// <summary>Composition abandoned.</summary>
    Cancel,
}

/// <summary>Input-method composition traffic, delivered at pointer-independent times.</summary>
public readonly record struct ImePacket(
    ImePhase Phase, string Text = "", int SelStart = 0, int SelLength = 0);
