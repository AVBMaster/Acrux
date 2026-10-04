using Acrux.Core.Dom;
using Acrux.Core.Dom.Html;
using Acrux.Core.Layout.Geometry;

namespace Acrux.Core.Layout;

public class LayoutEmbeddedObject : LayoutEmbeddedContent
{
    public enum PluginAvailability
    {
        Available,
        Missing,
        BlockedByContentSecurityPolicy
    }

    private PluginAvailability _pluginAvailability = PluginAvailability.Available;
    private string _unavailablePluginReplacementText = string.Empty;

    public LayoutEmbeddedObject(HtmlElement? element) : base(element)
    {
    }

    public void SetPluginAvailability(PluginAvailability availability)
    {
        _pluginAvailability = availability;
    }

    public bool ShowsUnavailablePluginIndicator() => _pluginAvailability != PluginAvailability.Available;

    public override string GetName() => "LayoutEmbeddedObject";

    public string UnavailablePluginReplacementText => _unavailablePluginReplacementText;

    public override void PaintReplaced(PaintInfo? paintInfo, PhysicalOffset paintOffset)
    {
    }

    public override void UpdateAfterLayout()
    {
    }

    public override bool IsEmbeddedObject => true;

    public override void ComputeIntrinsicSizingInfo(IntrinsicSizingInfo? info)
    {
    }
}