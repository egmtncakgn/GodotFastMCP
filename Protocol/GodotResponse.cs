using System.Text.Json;
using System.Text.Json.Serialization;

public class GodotResponse
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("success")]
    public bool Success { get; init; }

    [JsonPropertyName("result")]
    public JsonElement? Result { get; init; }

    [JsonPropertyName("error")]
    public string? Error { get; init; }

    /// <summary>
    /// Sorun 9 fix: Godot tarafı boş/null error döndürdüğünde kullanıcıya
    /// anlamlı bir mesaj gösterir ("[GodotMCP Hata] " gibi boş çıktı yerine).
    /// </summary>
    public string FormatError() => string.IsNullOrWhiteSpace(Error)
        ? "addon boş hata döndü (Godot logları için console_get_errors çağırın)"
        : Error!;

    /// <summary>
    /// Sorun 29 fix: Result'ı tutarlı JSON string'e çevirir.
    /// Nullable JsonElement null iken ToString() BOŞ STRING döndürür (null değil!)
    /// → eski "?? \"{}\"" kalıbı hiç devreye girmiyor, boş yanıt gidiyordu.
    /// GetRawText() her ValueKind için geçerli JSON üretir.
    /// </summary>
    public string FormatResult(string fallback = "{}") =>
        Result is { } je && je.ValueKind is not JsonValueKind.Undefined and not JsonValueKind.Null
            ? je.GetRawText()
            : fallback;
}
