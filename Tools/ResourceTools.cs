using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;

[McpServerToolType]
public class ResourceTools(GodotBridge bridge)
{
    [McpServerTool(Name = "resource_get_data")]
    [Description(".tres/.res dosyasının içeriğini (property'lerini) okur.")]
    public async Task<string> GetData(
        [Description("Resource dosyası yolu")] string path)
    {
        try
        {
            var result = await bridge.SendAsync("resource_get_data", new() { ["path"] = path });
            return result.Success ? result.FormatResult("{}") : $"[GodotMCP Hata] {result.FormatError()}";
        }
        catch (TimeoutException) { return "[GodotMCP] Godot yanıt vermedi."; }
        catch (Exception ex) { return $"[GodotMCP] Beklenmedik hata: {ex.Message}"; }
    }

    [McpServerTool(Name = "resource_modify")]
    [Description("Bir resource'un property'lerini değiştirir.")]
    public async Task<string> Modify(
        [Description("Resource dosyası yolu")] string path,
        [Description("Değiştirilecek property'ler (JSON dict)")] string propertiesJson)
    {
        try
        {
            var props = JsonSerializer.Deserialize<Dictionary<string, object?>>(propertiesJson);
            var result = await bridge.SendAsync("resource_modify", new() { ["path"] = path, ["properties"] = props });
            return result.Success ? "Resource güncellendi." : $"[GodotMCP Hata] {result.FormatError()}";
        }
        catch (TimeoutException) { return "[GodotMCP] Godot yanıt vermedi."; }
        catch (Exception ex) { return $"[GodotMCP] Beklenmedik hata: {ex.Message}"; }
    }

    [McpServerTool(Name = "resource_create")]
    [Description("Yeni bir resource dosyası oluşturur.")]
    public async Task<string> Create(
        [Description("Oluşturulacak resource yolu")] string path,
        [Description("Resource tipi, ör: Resource, Texture2D")] string type,
        [Description("Başlangıç property'leri (JSON dict, opsiyonel)")] string? propertiesJson = null)
    {
        try
        {
            Dictionary<string, object?>? props = null;
            if (!string.IsNullOrEmpty(propertiesJson))
                props = JsonSerializer.Deserialize<Dictionary<string, object?>>(propertiesJson);
            var result = await bridge.SendAsync("resource_create", new() { ["path"] = path, ["type"] = type, ["properties"] = props });
            return result.Success ? $"Resource oluşturuldu: {path}" : $"[GodotMCP Hata] {result.FormatError()}";
        }
        catch (TimeoutException) { return "[GodotMCP] Godot yanıt vermedi."; }
        catch (Exception ex) { return $"[GodotMCP] Beklenmedik hata: {ex.Message}"; }
    }

    [McpServerTool(Name = "resource_delete")]
    [Description("Bir resource dosyasını siler.")]
    public async Task<string> Delete(
        [Description("Silinecek resource yolu")] string path)
    {
        try
        {
            var result = await bridge.SendAsync("resource_delete", new() { ["path"] = path });
            return result.Success ? "Resource silindi." : $"[GodotMCP Hata] {result.FormatError()}";
        }
        catch (TimeoutException) { return "[GodotMCP] Godot yanıt vermedi."; }
        catch (Exception ex) { return $"[GodotMCP] Beklenmedik hata: {ex.Message}"; }
    }
}
