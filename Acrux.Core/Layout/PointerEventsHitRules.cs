namespace Acrux.Core.Layout;

/// <summary>
/// Hit-testing rules for the pointer-events CSS property.
/// Mirrors PointerEventsHitRules in pointer_events_hit_rules.h.
/// </summary>
public class PointerEventsHitRules
{
    public enum EHitTesting
    {
        SvgImageHitTesting,
        SvgGeometryHitTesting,
        SvgTextHitTesting,
    }

    public bool RequireVisible { get; set; }
    public bool RequireFill { get; set; }
    public bool RequireStroke { get; set; }
    public bool CanHitStroke { get; set; }
    public bool CanHitFill { get; set; }
    public bool CanHitBoundingBox { get; set; }

    public PointerEventsHitRules(EHitTesting hitTesting, HitTestRequest request, string pointerEvents)
    {
        // Simplified rules for the common CSS pointer-events values.
        // The full SVG rules are not implemented (no SVG support).
        switch (pointerEvents)
        {
            case "none":
                RequireVisible = false;
                RequireFill = false;
                RequireStroke = false;
                CanHitFill = false;
                CanHitStroke = false;
                CanHitBoundingBox = false;
                break;
            case "all":
            case "auto":
            default:
                RequireVisible = true;
                RequireFill = true;
                RequireStroke = true;
                CanHitFill = true;
                CanHitStroke = true;
                CanHitBoundingBox = true;
                break;
        }
    }
}