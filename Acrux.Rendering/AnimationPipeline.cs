using Acrux.Core.Css;
using Acrux.Core.Dom;
using Acrux.Core.Dom.Animations;

namespace Acrux.Rendering;

/// <summary>
/// Drives the animation + transition update for a document that has already had
/// style resolution run, using the same ordering the interactive shell uses:
/// style -&gt; animations -&gt; layout.
///
/// It lives outside the engine so the interactive shell and the headless capture
/// path cannot drift apart: both animate, lay out and repaint in this order, and
/// both re-run style resolution first so the engine always observes cascaded
/// values rather than its own previous output.
/// </summary>
public static class AnimationPipeline
{
    /// <summary>Milliseconds per simulated frame when stepping a virtual clock.</summary>
    public const double FrameIntervalMs = 1000.0 / 60.0;

    /// <summary>
    /// Run one animation tick at <paramref name="timeMs"/>. The caller must have
    /// re-run style resolution for the tick's values to be the cascaded ones.
    /// </summary>
    public static AnimationUpdateResult Step(
        CssAnimationEngine engine,
        Element root,
        StyleComputer? styleComputer,
        double timeMs)
    {
        engine.Timeline.SetCurrentTime(timeMs);
        return engine.Update(root, styleComputer?.CollectKeyframeRules(root.OwnerDocument));
    }

    /// <summary>
    /// Fast-forward the timeline from 0 to <paramref name="timeMs"/> in
    /// frame-sized steps, re-resolving style between each so transitions observe
    /// a real before/after sequence instead of a single jump.
    /// </summary>
    public static AnimationUpdateResult Advance(
        CssAnimationEngine engine,
        Element root,
        StyleComputer? styleComputer,
        double timeMs,
        Action? onTick = null)
    {
        var result = AnimationUpdateResult.Idle;
        double step = FrameIntervalMs;
        for (double t = step; t < timeMs; t += step)
        {
            result = Step(engine, root, styleComputer, t);
            onTick?.Invoke();
        }
        return Step(engine, root, styleComputer, timeMs);
    }
}
