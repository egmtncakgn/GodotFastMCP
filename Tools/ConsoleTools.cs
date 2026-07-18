using System.ComponentModel;
using ModelContextProtocol.Server;

[McpServerToolType]
public class ConsoleTools(GodotBridge bridge)
{
    [McpServerTool(Name = "console_get_logs")]
    [Description("Godot editor log'larını döndürür.")]
    public async Task<string> GetLogs(
        [Description("Kaç log satırı alınacak (varsayılan 50)")] int count = 50,
        [Description("Log seviyesi filtresi: info/warning/error (opsiyonel)")] string? level = null)
    {
        try
        {
            var result = await bridge.SendAsync("console_get_logs", new() { ["count"] = count, ["level"] = level });
            return result.Success ? result.FormatResult("[]") : $"[GodotMCP Hata] {result.FormatError()}";
        }
        catch (TimeoutException) { return "[GodotMCP] Godot yanıt vermedi."; }
        catch (Exception ex) { return $"[GodotMCP] Beklenmedik hata: {ex.Message}"; }
    }

    [McpServerTool(Name = "console_clear_logs")]
    [Description("Log önbelleğini temizler.")]
    public async Task<string> ClearLogs()
    {
        try
        {
            var result = await bridge.SendAsync("console_clear_logs");
            return result.Success ? "Loglar temizlendi." : $"[GodotMCP Hata] {result.FormatError()}";
        }
        catch (TimeoutException) { return "[GodotMCP] Godot yanıt vermedi."; }
        catch (Exception ex) { return $"[GodotMCP] Beklenmedik hata: {ex.Message}"; }
    }

    [McpServerTool(Name = "console_get_errors")]
    [Description("Sadece hata ve uyarı seviyesindeki logları döndürür.")]
    public async Task<string> GetErrors()
    {
        try
        {
            var result = await bridge.SendAsync("console_get_errors");
            return result.Success ? result.FormatResult("[]") : $"[GodotMCP Hata] {result.FormatError()}";
        }
        catch (TimeoutException) { return "[GodotMCP] Godot yanıt vermedi."; }
        catch (Exception ex) { return $"[GodotMCP] Beklenmedik hata: {ex.Message}"; }
    }
}
