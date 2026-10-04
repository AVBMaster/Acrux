using System.Text.Json.Serialization;

namespace Acrux.Rendering;

[JsonSerializable(typeof(RenderingSettingsConfig.ConfigData))]
internal partial class RenderingSettingsJsonContext : JsonSerializerContext
{
}
