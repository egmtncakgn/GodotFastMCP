using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;

[McpServerToolType]
public class FileSystemTools(GodotBridge bridge)
{
    [McpServerTool(Name = "filesystem_list")]
    [Description("res:// altındaki dosya/dizin ağacını listeler.")]
    public async Task<string> List(
        [Description("Listelenecek dizin (varsayılan res://)")] string? path = null,
        [Description("Alt dizinleri de tara")] bool recursive = false)
    {
        try
        {
            var result = await bridge.SendAsync("filesystem_list", new() { ["path"] = path, ["recursive"] = recursive });
            return result.Success ? result.FormatResult("[]") : $"[GodotMCP Hata] {result.FormatError()}";
        }
        catch (TimeoutException) { return "[GodotMCP] Godot yanıt vermedi."; }
        catch (Exception ex) { return $"[GodotMCP] Beklenmedik hata: {ex.Message}"; }
    }

    [McpServerTool(Name = "filesystem_read_file")]
    [Description("Bir dosyanın metin içeriğini okur.")]
    public async Task<string> ReadFile(
        [Description("Dosya yolu, ör: res://data/config.json")] string path)
    {
        try
        {
            var result = await bridge.SendAsync("filesystem_read_file", new() { ["path"] = path });
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

    [McpServerTool(Name = "filesystem_write_file")]
    [Description("Bir dosyaya metin yazar (yoksa oluşturur).")]
    public async Task<string> WriteFile(
        [Description("Dosya yolu")] string path,
        [Description("Yazılacak içerik")] string content)
    {
        try
        {
            var result = await bridge.SendAsync("filesystem_write_file", new() { ["path"] = path, ["content"] = content });
            return result.Success ? $"Dosya yazıldı: {path}" : $"[GodotMCP Hata] {result.FormatError()}";
        }
        catch (TimeoutException) { return "[GodotMCP] Godot yanıt vermedi."; }
        catch (Exception ex) { return $"[GodotMCP] Beklenmedik hata: {ex.Message}"; }
    }

    [McpServerTool(Name = "filesystem_delete")]
    [Description("Bir dosyayı veya dizini siler.")]
    public async Task<string> Delete(
        [Description("Silinecek dosya/dizin yolu")] string path)
    {
        try
        {
            var result = await bridge.SendAsync("filesystem_delete", new() { ["path"] = path });
            return result.Success ? "Silindi." : $"[GodotMCP Hata] {result.FormatError()}";
        }
        catch (TimeoutException) { return "[GodotMCP] Godot yanıt vermedi."; }
        catch (Exception ex) { return $"[GodotMCP] Beklenmedik hata: {ex.Message}"; }
    }

    [McpServerTool(Name = "filesystem_move")]
    [Description("Bir dosyayı taşır veya yeniden adlandırır.")]
    public async Task<string> Move(
        [Description("Kaynak yol")] string from,
        [Description("Hedef yol")] string to)
    {
        try
        {
            var result = await bridge.SendAsync("filesystem_move", new() { ["from"] = from, ["to"] = to });
            return result.Success ? "Taşındı." : $"[GodotMCP Hata] {result.FormatError()}";
        }
        catch (TimeoutException) { return "[GodotMCP] Godot yanıt vermedi."; }
        catch (Exception ex) { return $"[GodotMCP] Beklenmedik hata: {ex.Message}"; }
    }

    [McpServerTool(Name = "filesystem_reimport")]
    [Description("Bir asset'i yeniden içe aktarır.")]
    public async Task<string> Reimport(
        [Description("Yeniden içe aktarılacak dosya yolu (opsiyonel, boşsa hepsi)")] string? path = null)
    {
        try
        {
            var result = await bridge.SendAsync("filesystem_reimport", new() { ["path"] = path });
            return result.Success ? "Yeniden içe aktarıldı." : $"[GodotMCP Hata] {result.FormatError()}";
        }
        catch (TimeoutException) { return "[GodotMCP] Godot yanıt vermedi."; }
        catch (Exception ex) { return $"[GodotMCP] Beklenmedik hata: {ex.Message}"; }
    }

    [McpServerTool(Name = "filesystem_search")]
    [Description("Dosya/resource arar. Glob pattern destekler.")]
    public async Task<string> Search(
        [Description("Arama pattern'i, ör: *.tscn")] string pattern,
        [Description("Aranacak dizin (varsayılan res://)")] string? path = null,
        [Description("Dosya uzantısı filtresi, ör: .gd")] string? type = null)
    {
        try
        {
            var result = await bridge.SendAsync("filesystem_search", new() { ["pattern"] = pattern, ["path"] = path, ["type"] = type });
            return result.Success ? result.FormatResult("[]") : $"[GodotMCP Hata] {result.FormatError()}";
        }
        catch (TimeoutException) { return "[GodotMCP] Godot yanıt vermedi."; }
        catch (Exception ex) { return $"[GodotMCP] Beklenmedik hata: {ex.Message}"; }
    }
}
