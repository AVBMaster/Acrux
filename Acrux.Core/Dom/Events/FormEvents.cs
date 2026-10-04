namespace Acrux.Core.Dom;

public class SubmitEvent : Event
{
    public HtmlElement? Submitter { get; }

    public SubmitEvent(string type, SubmitEventInit? init = null) : base(type, init)
    {
        Submitter = init?.Submitter;
    }
}

public class SubmitEventInit : EventInit
{
    public HtmlElement? Submitter { get; set; }
}

public class FormDataEvent : Event
{
    public Html.FormData FormData { get; }

    public FormDataEvent(string type, FormDataEventInit? init = null) : base(type, init)
    {
        FormData = init?.FormData ?? new Html.FormData();
    }
}

public class FormDataEventInit : EventInit
{
    public Html.FormData FormData { get; set; } = new();
}

public class ToggleEvent : Event
{
    public string OldState { get; }
    public string NewState { get; }

    public ToggleEvent(string type, ToggleEventInit? init = null) : base(type, init)
    {
        OldState = init?.OldState ?? "";
        NewState = init?.NewState ?? "";
    }
}

public class ToggleEventInit : EventInit
{
    public string OldState { get; set; } = "";
    public string NewState { get; set; } = "";
}
