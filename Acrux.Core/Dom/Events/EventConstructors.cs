namespace Acrux.Core.Dom;

// UIEvent
public class UiEvent : Event
{
    public WindowProxy? View { get; }
    public int Detail { get; }

    public UiEvent(string type, UiEventInit? init = null) : base(type, init)
    {
        View = init?.View;
        Detail = init?.Detail ?? 0;
    }
}

public class UiEventInit : EventInit
{
    public WindowProxy? View { get; set; }
    public int Detail { get; set; }
}

// Placeholder for Window (BOM integration point)
public class WindowProxy { }

// MouseEvent
public class MouseEvent : UiEvent
{
    public int ScreenX { get; }
    public int ScreenY { get; }
    public int ClientX { get; }
    public int ClientY { get; }
    public bool CtrlKey { get; }
    public bool ShiftKey { get; }
    public bool AltKey { get; }
    public bool MetaKey { get; }
    public int Button { get; }
    public int Buttons { get; }
    public EventTarget? RelatedTarget { get; }

    public MouseEvent(string type, MouseEventInit? init = null) : base(type, init)
    {
        ScreenX = init?.ScreenX ?? 0;
        ScreenY = init?.ScreenY ?? 0;
        ClientX = init?.ClientX ?? 0;
        ClientY = init?.ClientY ?? 0;
        CtrlKey = init?.CtrlKey ?? false;
        ShiftKey = init?.ShiftKey ?? false;
        AltKey = init?.AltKey ?? false;
        MetaKey = init?.MetaKey ?? false;
        Button = init?.Button ?? 0;
        Buttons = init?.Buttons ?? 0;
        RelatedTarget = init?.RelatedTarget;
    }

    public void InitMouseEvent(string type, bool bubbles, bool cancelable, WindowProxy? view,
        int detail, int screenX, int screenY, int clientX, int clientY,
        bool ctrlKey, bool altKey, bool shiftKey, bool metaKey,
        int button, EventTarget? relatedTarget)
    {
        InitEvent(type, bubbles, cancelable);
    }
}

public class MouseEventInit : UiEventInit
{
    public int ScreenX { get; set; }
    public int ScreenY { get; set; }
    public int ClientX { get; set; }
    public int ClientY { get; set; }
    public bool CtrlKey { get; set; }
    public bool ShiftKey { get; set; }
    public bool AltKey { get; set; }
    public bool MetaKey { get; set; }
    public int Button { get; set; }
    public int Buttons { get; set; }
    public EventTarget? RelatedTarget { get; set; }
}

// WheelEvent
public class WheelEvent : MouseEvent
{
    public double DeltaX { get; }
    public double DeltaY { get; }
    public double DeltaZ { get; }
    public int DeltaMode { get; }

    public const int DomDeltaPixel = 0;
    public const int DomDeltaLine = 1;
    public const int DomDeltaPage = 2;

    public WheelEvent(string type, WheelEventInit? init = null) : base(type, init)
    {
        DeltaX = init?.DeltaX ?? 0;
        DeltaY = init?.DeltaY ?? 0;
        DeltaZ = init?.DeltaZ ?? 0;
        DeltaMode = init?.DeltaMode ?? 0;
    }
}

public class WheelEventInit : MouseEventInit
{
    public double DeltaX { get; set; }
    public double DeltaY { get; set; }
    public double DeltaZ { get; set; }
    public int DeltaMode { get; set; }
}

// KeyboardEvent
public class KeyboardEvent : UiEvent
{
    public string Key { get; }
    public string Code { get; }
    public string? Location { get; }
    public bool CtrlKey { get; }
    public bool ShiftKey { get; }
    public bool AltKey { get; }
    public bool MetaKey { get; }
    public bool Repeat { get; }
    public bool IsComposing { get; }
    public int KeyCode { get; }
    public int Which { get; }

    public const int DomKeyLocationStandard = 0;
    public const int DomKeyLocationLeft = 1;
    public const int DomKeyLocationRight = 2;
    public const int DomKeyLocationNumpad = 3;

    public KeyboardEvent(string type, KeyboardEventInit? init = null) : base(type, init)
    {
        Key = init?.Key ?? "";
        Code = init?.Code ?? "";
        Location = init?.Location;
        CtrlKey = init?.CtrlKey ?? false;
        ShiftKey = init?.ShiftKey ?? false;
        AltKey = init?.AltKey ?? false;
        MetaKey = init?.MetaKey ?? false;
        Repeat = init?.Repeat ?? false;
        IsComposing = init?.IsComposing ?? false;
        KeyCode = init?.KeyCode ?? 0;
        Which = init?.Which ?? 0;
    }
}

public class KeyboardEventInit : UiEventInit
{
    public string Key { get; set; } = "";
    public string Code { get; set; } = "";
    public string? Location { get; set; }
    public bool CtrlKey { get; set; }
    public bool ShiftKey { get; set; }
    public bool AltKey { get; set; }
    public bool MetaKey { get; set; }
    public bool Repeat { get; set; }
    public bool IsComposing { get; set; }
    public int KeyCode { get; set; }
    public int Which { get; set; }
}

// FocusEvent
public class FocusEvent : UiEvent
{
    public EventTarget? RelatedTarget { get; }

    public FocusEvent(string type, FocusEventInit? init = null) : base(type, init)
    {
        RelatedTarget = init?.RelatedTarget;
    }
}

public class FocusEventInit : UiEventInit
{
    public EventTarget? RelatedTarget { get; set; }
}

// InputEvent
public class InputEvent : UiEvent
{
    public string? Data { get; }
    public string? InputType { get; }
    public bool IsComposing { get; }

    public InputEvent(string type, InputEventInit? init = null) : base(type, init)
    {
        Data = init?.Data;
        InputType = init?.InputType;
        IsComposing = init?.IsComposing ?? false;
    }
}

public class InputEventInit : UiEventInit
{
    public string? Data { get; set; }
    public string? InputType { get; set; }
    public bool IsComposing { get; set; }
}

// CompositionEvent
public class CompositionEvent : UiEvent
{
    public string? Data { get; }

    public CompositionEvent(string type, CompositionEventInit? init = null) : base(type, init)
    {
        Data = init?.Data;
    }
}

public class CompositionEventInit : UiEventInit
{
    public string? Data { get; set; }
}

// PointerEvent
public class PointerEvent : MouseEvent
{
    public int PointerId { get; }
    public double Width { get; }
    public double Height { get; }
    public double Pressure { get; }
    public double TangentialPressure { get; }
    public double TiltX { get; }
    public double TiltY { get; }
    public double Twist { get; }
    public string? PointerType { get; }
    public bool IsPrimary { get; }

    public PointerEvent(string type, PointerEventInit? init = null) : base(type, init)
    {
        PointerId = init?.PointerId ?? 0;
        Width = init?.Width ?? 1;
        Height = init?.Height ?? 1;
        Pressure = init?.Pressure ?? 0;
        TangentialPressure = init?.TangentialPressure ?? 0;
        TiltX = init?.TiltX ?? 0;
        TiltY = init?.TiltY ?? 0;
        Twist = init?.Twist ?? 0;
        PointerType = init?.PointerType ?? "";
        IsPrimary = init?.IsPrimary ?? false;
    }
}

public class PointerEventInit : MouseEventInit
{
    public int PointerId { get; set; }
    public double Width { get; set; } = 1;
    public double Height { get; set; } = 1;
    public double Pressure { get; set; }
    public double TangentialPressure { get; set; }
    public double TiltX { get; set; }
    public double TiltY { get; set; }
    public double Twist { get; set; }
    public string? PointerType { get; set; }
    public bool IsPrimary { get; set; }
}

// ClipboardEvent
public class ClipboardEvent : Event
{
    public DataTransfer? ClipboardData { get; }

    public ClipboardEvent(string type, ClipboardEventInit? init = null) : base(type, init)
    {
        ClipboardData = init?.ClipboardData;
    }
}

public class ClipboardEventInit : EventInit
{
    public DataTransfer? ClipboardData { get; set; }
}

public class DataTransfer { }

// DragEvent
public class DragEvent : MouseEvent
{
    public DataTransfer? DataTransfer { get; }

    public DragEvent(string type, DragEventInit? init = null) : base(type, init)
    {
        DataTransfer = init?.DataTransfer;
    }
}

public class DragEventInit : MouseEventInit
{
    public DataTransfer? DataTransfer { get; set; }
}

// TouchEvent
public class TouchEvent : UiEvent
{
    public TouchList? Touches { get; }
    public TouchList? TargetTouches { get; }
    public TouchList? ChangedTouches { get; }
    public bool CtrlKey { get; }
    public bool ShiftKey { get; }
    public bool AltKey { get; }
    public bool MetaKey { get; }

    public TouchEvent(string type, TouchEventInit? init = null) : base(type, init)
    {
        Touches = init?.Touches;
        TargetTouches = init?.TargetTouches;
        ChangedTouches = init?.ChangedTouches;
        CtrlKey = init?.CtrlKey ?? false;
        ShiftKey = init?.ShiftKey ?? false;
        AltKey = init?.AltKey ?? false;
        MetaKey = init?.MetaKey ?? false;
    }
}

public class TouchEventInit : UiEventInit
{
    public TouchList? Touches { get; set; }
    public TouchList? TargetTouches { get; set; }
    public TouchList? ChangedTouches { get; set; }
    public bool CtrlKey { get; set; }
    public bool ShiftKey { get; set; }
    public bool AltKey { get; set; }
    public bool MetaKey { get; set; }
}

public class Touch
{
    public int Identifier { get; }
    public EventTarget? Target { get; }
    public double ClientX { get; }
    public double ClientY { get; }
    public double ScreenX { get; }
    public double ScreenY { get; }
    public double PageX { get; }
    public double PageY { get; }
    public double RadiusX { get; }
    public double RadiusY { get; }
    public double RotationAngle { get; }
    public double Force { get; }

    public Touch(int identifier, EventTarget? target, double clientX, double clientY, double screenX, double screenY, double pageX, double pageY, double radiusX = 0, double radiusY = 0, double rotationAngle = 0, double force = 0)
    {
        Identifier = identifier;
        Target = target;
        ClientX = clientX;
        ClientY = clientY;
        ScreenX = screenX;
        ScreenY = screenY;
        PageX = pageX;
        PageY = pageY;
        RadiusX = radiusX;
        RadiusY = radiusY;
        RotationAngle = rotationAngle;
        Force = force;
    }
}

public class TouchList : List<Touch>
{
    public TouchList() { }
    public TouchList(IEnumerable<Touch> touches) : base(touches) { }
}
