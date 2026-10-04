namespace Acrux.Input;

public static class InputMethod
{
    public static event EventHandler? CompositionStarted;
    public static event EventHandler? CompositionEnded;

    public static void StartComposition()
    {
        CompositionStarted?.Invoke(null, EventArgs.Empty);
    }

    public static void EndComposition(string? result = null)
    {
        CompositionEnded?.Invoke(null, EventArgs.Empty);
    }
}