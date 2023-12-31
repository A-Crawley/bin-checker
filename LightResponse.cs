using System.Text.Json.Serialization;

namespace binChecker;

public class LightResponse
{
    [JsonPropertyName("state")] public LightState State { get; set; } = new();
}