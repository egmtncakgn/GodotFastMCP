using System.ComponentModel;
using ModelContextProtocol.Server;

[McpServerToolType]
public class ScreenshotTools(GodotBridge bridge)
{
    [McpServerTool(Name = "screenshot_viewport")]
    [Description("Editor viewport'tan PNG ekran görüntüsü alır (base64 döner).")]
    public async Task<string> Viewport()
    {
        try
        {
            var result = await bridge.SendAsync("screenshot_viewport");
            return result.Success ? result.Result.ToString() ?? "{}" : $"[GodotMCP Hata] {result.Error}";
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
            return result.Success ? result.Result.ToString() ?? "{}" : $"[GodotMCP Hata] {result.Error}";
        }
        catch (TimeoutException) { return "[GodotMCP] Godot yanıt vermedi."; }
        catch (Exception ex) { return $"[GodotMCP] Beklenmedik hata: {ex.Message}"; }
    }
}
