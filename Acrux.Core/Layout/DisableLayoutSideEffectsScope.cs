namespace Acrux.Core.Layout;

/// <summary>
/// RAII scope that disables layout side effects (updating the layout object
/// tree, paint properties, etc.). Used e.g. when computing MinMax after layout.
/// Mirrors DisableLayoutSideEffectsScope in disable_layout_side_effects_scope.h.
/// </summary>
public sealed class DisableLayoutSideEffectsScope : IDisposable
{
    private static int _count;

    public DisableLayoutSideEffectsScope()
    {
        _count++;
    }

    public static bool IsDisabled => _count > 0;

    public void Dispose()
    {
        _count--;
    }
}