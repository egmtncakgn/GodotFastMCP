using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;

[McpServerToolType]
public class EditorTools(GodotBridge bridge)
{
    [McpServerTool(Name = "editor_play")]
    [Description("Oyunu editor içinde başlatır (F5 eşdeğeri).")]
    public async Task<string> Play()
    {
        try
        {
            var result = await bridge.SendAsync("editor_play");
            return result.Success ? "Oyun başlatıldı." : $"[GodotMCP Hata] {result.FormatError()}";
        }
        catch (TimeoutException) { return "[GodotMCP] Godot yanıt vermedi."; }
        catch (Exception ex) { return $"[GodotMCP] Beklenmedik hata: {ex.Message}"; }
    }

    [McpServerTool(Name = "editor_stop")]
    [Description("Çalışan oyunu durdurur.")]
    public async Task<string> Stop()
    {
        try
        {
            var result = await bridge.SendAsync("editor_stop");
            return result.Success ? "Oyun durduruldu." : $"[GodotMCP Hata] {result.FormatError()}";
        }
        catch (TimeoutException) { return "[GodotMCP] Godot yanıt vermedi."; }
        catch (Exception ex) { return $"[GodotMCP] Beklenmedik hata: {ex.Message}"; }
    }

    [McpServerTool(Name = "editor_pause")]
    [Description("Oyunu duraklatır veya devam ettirir.")]
    public async Task<string> Pause()
    {
        try
        {
            var result = await bridge.SendAsync("editor_pause");
            return result.Success ? "Oyun duraklatma durumu değiştirildi." : $"[GodotMCP Hata] {result.FormatError()}";
        }
        catch (TimeoutException) { return "[GodotMCP] Godot yanıt vermedi."; }
        catch (Exception ex) { return $"[GodotMCP] Beklenmedik hata: {ex.Message}"; }
    }

    [McpServerTool(Name = "editor_get_state")]
    [Description("Editor'ın mevcut durumunu döndürür: playing/paused/stopped.")]
    public async Task<string> GetState()
    {
        try
        {
            var result = await bridge.SendAsync("editor_get_state");
            return result.Success ? result.FormatResult("{}") : $"[GodotMCP Hata] {result.FormatError()}";
        }
        catch (TimeoutException) { return "[GodotMCP] Godot yanıt vermedi."; }
        catch (Exception ex) { return $"[GodotMCP] Beklenmedik hata: {ex.Message}"; }
    }

    [McpServerTool(Name = "editor_selection_get")]
    [Description("Editor'da seçili olan node'ların listesini döndürür.")]
    public async Task<string> SelectionGet()
    {
        try
        {
            var result = await bridge.SendAsync("editor_selection_get");
            return result.Success ? result.FormatResult("[]") : $"[GodotMCP Hata] {result.FormatError()}";
        }
        catch (TimeoutException) { return "[GodotMCP] Godot yanıt vermedi."; }
        catch (Exception ex) { return $"[GodotMCP] Beklenmedik hata: {ex.Message}"; }
    }

    [McpServerTool(Name = "editor_selection_set")]
    [Description("Editor'da belirtilen node'ları seçer.")]
    public async Task<string> SelectionSet(
        [Description("Seçilecek node yollarının JSON array'i")] string nodePathsJson)
    {
        try
        {
            var paths = JsonSerializer.Deserialize<string[]>(nodePathsJson);
            var result = await bridge.SendAsync("editor_selection_set", new() { ["node_paths"] = paths });
            return result.Success ? "Seçim güncellendi." : $"[GodotMCP Hata] {result.FormatError()}";
        }
        catch (TimeoutException) { return "[GodotMCP] Godot yanıt vermedi."; }
        catch (Exception ex) { return $"[GodotMCP] Beklenmedik hata: {ex.Message}"; }
    }

    [McpServerTool(Name = "editor_get_project_settings")]
    [Description("Proje ayarlarını okur. Anahtar listesi verilirse sadece onları döndürür.")]
    public async Task<string> GetProjectSettings(
        [Description("Anahtar listesi JSON array (opsiyonel)")] string? keysJson = null)
    {
        try
        {
            string[]? keys = null;
            if (!string.IsNullOrEmpty(keysJson))
                keys = JsonSerializer.Deserialize<string[]>(keysJson);
            var result = await bridge.SendAsync("editor_get_project_settings", new() { ["keys"] = keys });
            return result.Success ? result.FormatResult("{}") : $"[GodotMCP Hata] {result.FormatError()}";
        }
        catch (TimeoutException) { return "[GodotMCP] Godot yanıt vermedi."; }
        catch (Exception ex) { return $"[GodotMCP] Beklenmedik hata: {ex.Message}"; }
    }

    [McpServerTool(Name = "editor_set_project_setting")]
    [Description("Bir proje ayarını yazar.")]
    public async Task<string> SetProjectSetting(
        [Description("Ayar anahtarı")] string key,
        [Description("Ayar değeri (JSON)")] string valueJson)
    {
        try
        {
            var value = JsonSerializer.Deserialize<object>(valueJson);
            var result = await bridge.SendAsync("editor_set_project_setting", new() { ["key"] = key, ["value"] = value });
            return result.Success ? "Proje ayarı güncellendi." : $"[GodotMCP Hata] {result.FormatError()}";
        }
        catch (TimeoutException) { return "[GodotMCP] Godot yanıt vermedi."; }
        catch (Exception ex) { return $"[GodotMCP] Beklenmedik hata: {ex.Message}"; }
    }

    [McpServerTool(Name = "editor_get_project_path")]
    [Description("project.godot dosyasının bulunduğu dizini döndürür.")]
    public async Task<string> GetProjectPath()
    {
        try
        {
            var result = await bridge.SendAsync("editor_get_project_path");
            return result.Success ? result.FormatResult("") : $"[GodotMCP Hata] {result.FormatError()}";
        }
        catch (TimeoutException) { return "[GodotMCP] Godot yanıt vermedi."; }
        catch (Exception ex) { return $"[GodotMCP] Beklenmedik hata: {ex.Message}"; }
    }
}
