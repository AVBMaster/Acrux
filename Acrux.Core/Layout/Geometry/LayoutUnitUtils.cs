namespace Acrux.Core.Layout.Geometry;

/// <summary>
/// Helpers for scalar layout values. The engine maps its layout unit directly
/// onto C# float per the project convention, so these helpers mirror the
/// layout-unit static API used by the inline layout engine.
/// </summary>
public static class LayoutUnitUtils
{
    /// <summary>LayoutUnit::Max() — indefinite / unbounded size.</summary>
    public const float IndefiniteSize = float.MaxValue;

    /// <summary>LayoutUnit::NearlyMax().</summary>
    public static float NearlyMax() => float.MaxValue / 2f;

    /// <summary>A small epsilon for LayoutUnit arithmetic (LayoutUnit::Epsilon()).</summary>
    public const float Epsilon = 0.0001f;

    /// <summary>LayoutUnit::AddEpsilon().</summary>
    public static float AddEpsilon(float value) => value == float.MaxValue ? value : value + Epsilon;

    /// <summary>LayoutUnit::ClampNegativeToZero().</summary>
    public static float ClampNegativeToZero(float value) => value < 0 ? 0 : value;

    /// <summary>Clamp to [0, LayoutUnit::NearlyMax()], as done by UpdateAvailableWidth().</summary>
    public static float ClampToMaxSize(float value) => Math.Min(Math.Max(0, value), NearlyMax());

    public static bool IsIndefinite(float value) => value >= float.MaxValue || value == float.MaxValue;
}