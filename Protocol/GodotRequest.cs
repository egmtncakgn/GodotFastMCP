using System.Text.Json.Serialization;

public class GodotRequest
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("command")]
    public required string Command { get; init; }

    [JsonPropertyName("params")]
    public Dictionary<string, object?>? Params { get; init; }
}
