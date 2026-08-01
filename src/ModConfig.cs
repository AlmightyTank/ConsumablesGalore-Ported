using System.Text.Json.Serialization;

namespace ConsumablesGalore;

/// <summary>
/// Mirrors config/config.json. Controls verbose logging while items load.
/// </summary>
public record ModConfig
{
    [JsonPropertyName("debug")]
    public bool Debug { get; set; }

    [JsonPropertyName("realDebug")]
    public bool RealDebug { get; set; }
}
