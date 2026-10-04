using Acrux.Core.Css.Rules;
using Acrux.Core.Dom;
using Acrux.Core.Dom.Animations;
using Acrux.Core.Performance;

namespace Acrux.Core.Layout;

/// <summary>
/// Coordinates the full rendering pipeline: style resolution → animation → layout → pre-paint → paint.
/// Implements the document lifecycle: StyleRecalc → Layout → PrePaint → Paint.
/// </summary>
public class RenderingPipeline
{
    private readonly System.Diagnostics.Stopwatch _stopwatch = System.Diagnostics.Stopwatch.StartNew();
    public CssAnimationEngine AnimationEngine { get; } = new();
    public PrePaintTreeWalk PrePaintWalk { get; } = new();
    public PaintInvalidator Invalidator { get; } = new();
    public List<StyleRuleKeyframes> KeyframeRules { get; } = new();

    public double CurrentTimeMs { get; set; }

    /// <summary>
    /// Run the CSS animation + transition update for the whole tree. The caller
    /// is responsible for having run style resolution first: the engine compares
    /// the cascaded computed values to detect transition start conditions, so
    /// feeding it its own previous output would suppress every transition.
    /// </summary>
    public AnimationUpdateResult UpdateAnimations(Element root)
    {
        if (root == null) return AnimationUpdateResult.Idle;
        var result = AnimationEngine.Update(root, KeyframeRules);
        if (result.HasActiveAnimations && root.ComputedStyle != null)
            InvalidateDirtyElements(root);
        return result;
    }

    /// <summary>Run the full rendering pipeline for a document.</summary>
    public void ProcessDocument(Document document, float viewportWidth, float viewportHeight, float dpiScale = 1.0f)
    {
        // 1. Style Resolution (already done by StyleComputer)

        // 2. Layout (already done by LayoutEngine)

        // 3. Build Paint Property Trees
        var root = document.DocumentElement ?? document.Body;
        if (root != null)
            PrePaintWalk.Walk(root);

        // 4. Apply CSS Animations & Transitions
        UpdateAnimations(root);
    }

    private void InvalidateDirtyElements(Element element)
    {
        var reason = Invalidator.ComputeReason(element, false, false);
        if (reason != PaintInvalidationReason.None)
            Invalidator.InvalidatePaint(element);

        foreach (var child in element.Children)
        {
            if (child is Element childEl)
                InvalidateDirtyElements(childEl);
        }
    }

    /// <summary>Collect @keyframes rules from stylesheets for the animation engine.</summary>
    public void CollectKeyframes(List<StyleSheetContents> sheets)
    {
        KeyframeRules.Clear();
        foreach (var sheet in sheets)
            CollectKeyframesFromRules(sheet.ChildRules);
    }

    private void CollectKeyframesFromRules(List<StyleRuleBase> rules)
    {
        foreach (var rule in rules)
        {
            if (rule is StyleRuleKeyframes kf)
                KeyframeRules.Add(kf);
            if (rule is StyleRuleGroup group)
                CollectKeyframesFromRules(group.ChildRules);
        }
    }
}
