using Acrux.Core.Dom.Html;

namespace Acrux.Core.Layout;

public class LayoutProgress : LayoutBlockFlow
{
    private double _position;
    private DateTime _animationStartTime;
    private bool _animating;

    public LayoutProgress(HTMLProgressElement element) : base(element)
    {
    }

    public double GetPosition() => _position;

    public double AnimationProgress()
    {
        if (!_animating) return 0;
        var elapsed = (DateTime.UtcNow - _animationStartTime).TotalSeconds;
        return Math.Min(elapsed / 2.0, 1.0);
    }

    public bool IsDeterminate() => Node is HTMLProgressElement { Position: >= 0 };

    public override void UpdateFromElement()
    {
        if (Node is HTMLProgressElement progress)
        {
            _position = progress.Position;
        }
    }

    public HTMLProgressElement? ProgressElement() => Node as HTMLProgressElement;

    public override string GetName() => "LayoutNGProgress";

    public override void WillBeDestroyed()
    {
    }

    public override bool IsProgress => true;

    public bool IsAnimating() => _animating;

    public bool IsAnimationTimerActive() => _animating;

    private void AnimationTimerFired()
    {
    }

    private void UpdateAnimationState()
    {
    }
}