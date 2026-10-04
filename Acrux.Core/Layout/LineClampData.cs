namespace Acrux.Core.Layout;

/// <summary>
/// Line-clamp state for block containers. Mirrors LineClampData in
/// line_clamp_data.h.
/// </summary>
public struct LineClampData
{
    public enum ClampState
    {
        Disabled,
        ClampByLines,
        MeasureLinesUntilBfcOffset,
        DontTruncate,
    }

    public bool IsLineClampContext => CurrentState != ClampState.Disabled;

    public bool IsAtClampPoint => CurrentState == ClampState.ClampByLines && LinesUntilClamp == 1;

    public bool IsPastClampPoint => CurrentState == ClampState.ClampByLines && LinesUntilClamp <= 0;

    public bool ShouldHideForPaint => IsPastClampPoint;

    public int LinesUntilClampCount(bool showMeasuredLines = false)
    {
        if (CurrentState == ClampState.ClampByLines ||
            (showMeasuredLines && CurrentState == ClampState.MeasureLinesUntilBfcOffset))
            return LinesUntilClamp;
        return 0;
    }

    public float ClampBfcOffset;
    public int LinesUntilClamp;
    public ClampState CurrentState;
}