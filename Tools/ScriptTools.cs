using System.ComponentModel;
using ModelContextProtocol.Server;

[McpServerToolType]
public class ScriptTools(GodotBridge bridge)
{
    [McpServerTool(Name = "script_read")]
    [Description("Bir script dosyasının içeriğini okur (.gd veya .cs).")]
    public async Task<string> ReadScript(
        [Description("Script dosyası yolu, ör: res://scripts/player.gd")] string path)
    {
        try
        {
            var result = await bridge.SendAsync("script_read", new() { ["path"] = path });
            if (!result.Success) return $"[GodotMCP Hata] {result.FormatError()}";
            if (result.Result is System.Text.Json.JsonElement je &&
                je.ValueKind == System.Text.Json.JsonValueKind.Object &&
                je.TryGetProperty("content", out var content))
                return content.GetString() ?? "";
            return result.FormatResult("");
        }
        catch (TimeoutException) { return "[GodotMCP] Godot yanıt vermedi."; }
        catch (Exception ex) { return $"[GodotMCP] Beklenmedik hata: {ex.Message}"; }
    }

    [McpServerTool(Name = "script_create")]
    [Description("Yeni bir script dosyası oluşturur.")]
    public async Task<string> CreateScript(
        [Description("Script dosyası yolu")] string path,
        [Description("Script içeriği")] string content,
        [Description("Dil: gd veya cs (varsayılan gd)")] string? language = null)
    {
        try
        {
            var result = await bridge.SendAsync("script_create", new() { ["path"] = path, ["content"] = content, ["language"] = language });
            return result.Success ? $"Script oluşturuldu: {path}" : $"[GodotMCP Hata] {result.FormatError()}";
        }
        catch (TimeoutException) { return "[GodotMCP] Godot yanıt vermedi."; }
        catch (Exception ex) { return $"[GodotMCP] Beklenmedik hata: {ex.Message}"; }
    }

    [McpServerTool(Name = "script_update")]
    [Description("Mevcut bir script dosyasının içeriğini günceller.")]
    public async Task<string> UpdateScript(
        [Description("Script dosyası yolu")] string path,
        [Description("Yeni script içeriği")] string content)
    {
        try
        {
            var result = await bridge.SendAsync("script_update", new() { ["path"] = path, ["content"] = content });
            return result.Success ? "Script güncellendi." : $"[GodotMCP Hata] {result.FormatError()}";
        }
        catch (TimeoutException) { return "[GodotMCP] Godot yanıt vermedi."; }
        catch (Exception ex) { return $"[GodotMCP] Beklenmedik hata: {ex.Message}"; }
    }

    [McpServerTool(Name = "script_delete")]
    [Description("Bir script dosyasını siler.")]
    public async Task<string> DeleteScript(
        [Description("Silinecek script yolu")] string path)
    {
        try
        {
            var result = await bridge.SendAsync("script_delete", new() { ["path"] = path });
            return result.Success ? "Script silindi." : $"[GodotMCP Hata] {result.FormatError()}";
        }
        catch (TimeoutException) { return "[GodotMCP] Godot yanıt vermedi."; }
        catch (Exception ex) { return $"[GodotMCP] Beklenmedik hata: {ex.Message}"; }
    }

    [McpServerTool(Name = "script_attach_to_node")]
    [Description("Bir script'i bir node'a bağlar (extends Script).")]
    public async Task<string> AttachToNode(
        [Description("Hedef node yolu")] string nodePath,
        [Description("Bağlanacak script yolu")] string scriptPath)
    {
        try
        {
            var result = await bridge.SendAsync("script_attach_to_node", new() { ["node_path"] = nodePath, ["script_path"] = scriptPath });
            return result.Success ? "Script node'a bağlandı." : $"[GodotMCP Hata] {result.FormatError()}";
        }
        catch (TimeoutException) { return "[GodotMCP] Godot yanıt vermedi."; }
        catch (Exception ex) { return $"[GodotMCP] Beklenmedik hata: {ex.Message}"; }
    }

    [McpServerTool(Name = "script_get_errors")]
    [Description("Aktif script'teki hata/uyarıları döndürür. Not: Godot API'si sadece aktif script'i destekler; path verilirse aktif script'le eşleşmesi doğrulanır.")]
    public async Task<string> GetErrors(
        [Description("Script yolu (opsiyonel; aktif script'ten farklıysa hata döner)")] string? path = null)
    {
        try
        {
            var result = await bridge.SendAsync("script_get_errors", new() { ["path"] = path });
            return result.Success ? result.FormatResult("[]") : $"[GodotMCP Hata] {result.FormatError()}";
        }
        catch (TimeoutException) { return "[GodotMCP] Godot yanıt vermedi."; }
        catch (Exception ex) { return $"[GodotMCP] Beklenmedik hata: {ex.Message}"; }
    }
}
