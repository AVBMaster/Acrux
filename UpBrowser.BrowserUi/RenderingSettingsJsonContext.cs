using System.Text.Json.Serialization;

namespace UpBrowser.Rendering;

[JsonSerializable(typeof(RenderingSettingsConfig.ConfigData))]
internal partial class RenderingSettingsJsonContext : JsonSerializerContext
{
}
