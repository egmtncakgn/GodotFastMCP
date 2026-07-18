using System.ComponentModel;
using ModelContextProtocol.Server;

[McpServerToolType]
public class SceneTools(GodotBridge bridge)
{
    [McpServerTool(Name = "scene_open")]
    [Description("Godot Editor'de belirtilen sahneyi açar. path parametresi res:// ile başlamalıdır.")]
    public async Task<string> OpenScene(
        [Description("Açılacak sahnenin proje-içi yolu. Örnek: res://scenes/Main.tscn")]
        string path)
    {
        try
        {
            var result = await bridge.SendAsync("scene_open", new() { ["path"] = path });
            return result.Success
                ? $"Sahne açıldı: {path}"
                : $"[GodotMCP Hata] {result.FormatError()}";
        }
        catch (TimeoutException)
        {
            return "[GodotMCP] Godot yanıt vermedi. Sahne veya komut çok karmaşık olabilir.";
        }
        catch (Exception ex)
        {
            return $"[GodotMCP] Beklenmedik hata: {ex.Message}";
        }
    }

    [McpServerTool(Name = "scene_save")]
    [Description("Aktif sahneyi kaydeder. path verilirse o konuma kaydeder.")]
    public async Task<string> SaveScene(
        [Description("Kaydedilecek yol (opsiyonel). Verilmezse mevcut konum kullanılır.")]
        string? path = null)
    {
        try
        {
            var result = await bridge.SendAsync("scene_save", new() { ["path"] = path });
            return result.Success ? "Sahne kaydedildi." : $"[GodotMCP Hata] {result.FormatError()}";
        }
        catch (TimeoutException)
        {
            return "[GodotMCP] Godot yanıt vermedi.";
        }
        catch (Exception ex)
        {
            return $"[GodotMCP] Beklenmedik hata: {ex.Message}";
        }
    }

    [McpServerTool(Name = "scene_create")]
    [Description("Yeni bir .tscn sahne dosyası oluşturur.")]
    public async Task<string> CreateScene(
        [Description("Oluşturulacak sahnenin yolu. Örnek: res://scenes/New.tscn")] string path,
        [Description("Kök node tipi. Varsayılan: Node2D")] string rootNodeType = "Node2D")
    {
        try
        {
            var result = await bridge.SendAsync("scene_create", new() { ["path"] = path, ["root_node_type"] = rootNodeType });
            return result.Success ? $"Sahne oluşturuldu: {path}" : $"[GodotMCP Hata] {result.FormatError()}";
        }
        catch (TimeoutException)
        {
            return "[GodotMCP] Godot yanıt vermedi.";
        }
        catch (Exception ex)
        {
            return $"[GodotMCP] Beklenmedik hata: {ex.Message}";
        }
    }

    [McpServerTool(Name = "scene_list_opened")]
    [Description("Editor'de açık olan tüm sahnelerin listesini döndürür.")]
    public async Task<string> ListOpened()
    {
        try
        {
            var result = await bridge.SendAsync("scene_list_opened");
            return result.Success ? result.FormatResult("[]") : $"[GodotMCP Hata] {result.FormatError()}";
        }
        catch (TimeoutException)
        {
            return "[GodotMCP] Godot yanıt vermedi.";
        }
        catch (Exception ex)
        {
            return $"[GodotMCP] Beklenmedik hata: {ex.Message}";
        }
    }

    [McpServerTool(Name = "scene_get_data")]
    [Description("Sahne node ağacı yapısını döndürür. path verilirse sahne editor'de açılmadan diskten okunur; verilmezse aktif sahne kullanılır.")]
    public async Task<string> GetData(
        [Description("Verisi alınacak sahne yolu (opsiyonel, ör: res://scenes/Main.tscn)")] string? path = null,
        [Description("Maksimum derinlik (varsayılan 10)")] int? maxDepth = null)
    {
        try
        {
            var result = await bridge.SendAsync("scene_get_data", new()
            {
                ["path"] = path,
                ["max_depth"] = maxDepth
            });
            return result.Success ? result.FormatResult("{}") : $"[GodotMCP Hata] {result.FormatError()}";
        }
        catch (TimeoutException)
        {
            return "[GodotMCP] Godot yanıt vermedi.";
        }
        catch (Exception ex)
        {
            return $"[GodotMCP] Beklenmedik hata: {ex.Message}";
        }
    }

    [McpServerTool(Name = "scene_close")]
    [Description("Aktif sahneyi kapatır. Not: Godot API'si sadece aktif sahneyi kapatabilir; path verilirse aktif sahneyle eşleşmesi doğrulanır.")]
    public async Task<string> CloseScene(
        [Description("Kapatılacak sahne yolu (opsiyonel; aktif sahneden farklıysa hata döner)")] string? path = null)
    {
        try
        {
            var result = await bridge.SendAsync("scene_close", new() { ["path"] = path });
            return result.Success ? "Sahne kapatıldı." : $"[GodotMCP Hata] {result.FormatError()}";
        }
        catch (TimeoutException)
        {
            return "[GodotMCP] Godot yanıt vermedi.";
        }
        catch (Exception ex)
        {
            return $"[GodotMCP] Beklenmedik hata: {ex.Message}";
        }
    }
}
