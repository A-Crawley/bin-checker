using System.Text.Json.Serialization;

namespace binChecker;

public class LightState
{
    [JsonPropertyName("on")] public bool On { get; set; }
    [JsonPropertyName("hue")] public int Hue { get; set; }
    [JsonPropertyName("bri")] public int Brightness { get; set; }
}