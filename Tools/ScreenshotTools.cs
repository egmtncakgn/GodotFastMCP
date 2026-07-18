using System.ComponentModel;
using ModelContextProtocol.Server;

[McpServerToolType]
public class ScreenshotTools(GodotBridge bridge)
{
    [McpServerTool(Name = "screenshot_viewport")]
    [Description("Editor viewport'tan PNG ekran görüntüsü alır (base64 döner). mode: 'auto' (görünür olanı seç), '2d' (ana editor), '3d' (3D viewport).")]
    public async Task<string> Viewport(
        [Description("Viewport modu: auto/2d/3d (varsayılan auto)")] string? mode = null)
    {
        try
        {
            var result = await bridge.SendAsync("screenshot_viewport", new() { ["mode"] = mode });
            return result.Success ? result.FormatResult("{}") : $"[GodotMCP Hata] {result.FormatError()}";
        }
        catch (TimeoutException) { return "[GodotMCP] Godot yanıt vermedi."; }
        catch (Exception ex) { return $"[GodotMCP] Beklenmedik hata: {ex.Message}"; }
    }

    [McpServerTool(Name = "screenshot_game")]
    [Description("Oyun çalışıyorsa game window'dan PNG ekran görüntüsü alır.")]
    public async Task<string> Game()
    {
        try
        {
            var result = await bridge.SendAsync("screenshot_game");
            return result.Success ? result.FormatResult("{}") : $"[GodotMCP Hata] {result.FormatError()}";
        }
        catch (TimeoutException) { return "[GodotMCP] Godot yanıt vermedi."; }
        catch (Exception ex) { return $"[GodotMCP] Beklenmedik hata: {ex.Message}"; }
    }
}
