using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;

[McpServerToolType]
public class NodeTools(GodotBridge bridge)
{
    [McpServerTool(Name = "node_find")]
    [Description("Sahnede node arar. İsim, tip veya yol ile filtreleyebilir.")]
    public async Task<string> FindNode(
        [Description("Node ismi (opsiyonel)")] string? name = null,
        [Description("Node tipi (opsiyonel)")] string? type = null,
        [Description("Node yolu (opsiyonel)")] string? path = null)
    {
        try
        {
            var result = await bridge.SendAsync("node_find", new() { ["name"] = name, ["type"] = type, ["path"] = path });
            return result.Success ? result.Result.ToString() ?? "[]" : $"[GodotMCP Hata] {result.Error}";
        }
        catch (TimeoutException) { return "[GodotMCP] Godot yanıt vermedi."; }
        catch (Exception ex) { return $"[GodotMCP] Beklenmedik hata: {ex.Message}"; }
    }

    [McpServerTool(Name = "node_create")]
    [Description("Sahnede yeni bir node oluşturur.")]
    public async Task<string> CreateNode(
        [Description("Node tipi, ör: CharacterBody2D")] string type,
        [Description("Node ismi")] string name,
        [Description("Üst node yolu (opsiyonel, verilmezse sahne kökü)")] string? parentPath = null)
    {
        try
        {
            var result = await bridge.SendAsync("node_create", new() { ["type"] = type, ["name"] = name, ["parent_path"] = parentPath });
            return result.Success ? $"Node oluşturuldu: {name}" : $"[GodotMCP Hata] {result.Error}";
        }
        catch (TimeoutException) { return "[GodotMCP] Godot yanıt vermedi."; }
        catch (Exception ex) { return $"[GodotMCP] Beklenmedik hata: {ex.Message}"; }
    }

    [McpServerTool(Name = "node_get_properties")]
    [Description("Bir node'un tüm editor property'lerini döndürür.")]
    public async Task<string> GetProperties(
        [Description("Node yolu, ör: res://Player.tscn::Player")] string nodePath)
    {
        try
        {
            var result = await bridge.SendAsync("node_get_properties", new() { ["node_path"] = nodePath });
            return result.Success ? result.Result.ToString() ?? "{}" : $"[GodotMCP Hata] {result.Error}";
        }
        catch (TimeoutException) { return "[GodotMCP] Godot yanıt vermedi."; }
        catch (Exception ex) { return $"[GodotMCP] Beklenmedik hata: {ex.Message}"; }
    }

    [McpServerTool(Name = "node_set_property")]
    [Description("Bir node'da tek bir property ayarlar.")]
    public async Task<string> SetProperty(
        [Description("Node yolu")] string nodePath,
        [Description("Property ismi")] string property,
        [Description("Property değeri")] object value)
    {
        try
        {
            var result = await bridge.SendAsync("node_set_property", new() { ["node_path"] = nodePath, ["property"] = property, ["value"] = value });
            return result.Success ? $"Property ayarlandı: {property}" : $"[GodotMCP Hata] {result.Error}";
        }
        catch (TimeoutException) { return "[GodotMCP] Godot yanıt vermedi."; }
        catch (Exception ex) { return $"[GodotMCP] Beklenmedik hata: {ex.Message}"; }
    }

    [McpServerTool(Name = "node_set_properties")]
    [Description("Bir node'da birden fazla property'yi toplu olarak ayarlar.")]
    public async Task<string> SetProperties(
        [Description("Node yolu")] string nodePath,
        [Description("Property dictionary'si (JSON)")] string propertiesJson)
    {
        try
        {
            var props = JsonSerializer.Deserialize<Dictionary<string, object?>>(propertiesJson);
            var result = await bridge.SendAsync("node_set_properties", new() { ["node_path"] = nodePath, ["properties"] = props });
            return result.Success ? "Property'ler ayarlandı." : $"[GodotMCP Hata] {result.Error}";
        }
        catch (TimeoutException) { return "[GodotMCP] Godot yanıt vermedi."; }
        catch (Exception ex) { return $"[GodotMCP] Beklenmedik hata: {ex.Message}"; }
    }

    [McpServerTool(Name = "node_delete")]
    [Description("Bir node'u sahneden siler.")]
    public async Task<string> DeleteNode(
        [Description("Silinecek node yolu")] string nodePath)
    {
        try
        {
            var result = await bridge.SendAsync("node_delete", new() { ["node_path"] = nodePath });
            return result.Success ? "Node silindi." : $"[GodotMCP Hata] {result.Error}";
        }
        catch (TimeoutException) { return "[GodotMCP] Godot yanıt vermedi."; }
        catch (Exception ex) { return $"[GodotMCP] Beklenmedik hata: {ex.Message}"; }
    }

    [McpServerTool(Name = "node_reparent")]
    [Description("Bir node'u farklı bir üst node'a taşır.")]
    public async Task<string> ReparentNode(
        [Description("Taşınacak node yolu")] string nodePath,
        [Description("Yeni üst node yolu")] string newParentPath)
    {
        try
        {
            var result = await bridge.SendAsync("node_reparent", new() { ["node_path"] = nodePath, ["new_parent_path"] = newParentPath });
            return result.Success ? "Node taşındı." : $"[GodotMCP Hata] {result.Error}";
        }
        catch (TimeoutException) { return "[GodotMCP] Godot yanıt vermedi."; }
        catch (Exception ex) { return $"[GodotMCP] Beklenmedik hata: {ex.Message}"; }
    }

    [McpServerTool(Name = "node_duplicate")]
    [Description("Bir node'u kopyalar.")]
    public async Task<string> DuplicateNode(
        [Description("Kopyalanacak node yolu")] string nodePath,
        [Description("Yeni node ismi (opsiyonel)")] string? newName = null)
    {
        try
        {
            var result = await bridge.SendAsync("node_duplicate", new() { ["node_path"] = nodePath, ["new_name"] = newName });
            return result.Success ? "Node kopyalandı." : $"[GodotMCP Hata] {result.Error}";
        }
        catch (TimeoutException) { return "[GodotMCP] Godot yanıt vermedi."; }
        catch (Exception ex) { return $"[GodotMCP] Beklenmedik hata: {ex.Message}"; }
    }

    [McpServerTool(Name = "node_rename")]
    [Description("Bir node'u yeniden adlandırır.")]
    public async Task<string> RenameNode(
        [Description("Node yolu")] string nodePath,
        [Description("Yeni isim")] string newName)
    {
        try
        {
            var result = await bridge.SendAsync("node_rename", new() { ["node_path"] = nodePath, ["new_name"] = newName });
            return result.Success ? $"Node yeniden adlandırıldı: {newName}" : $"[GodotMCP Hata] {result.Error}";
        }
        catch (TimeoutException) { return "[GodotMCP] Godot yanıt vermedi."; }
        catch (Exception ex) { return $"[GodotMCP] Beklenmedik hata: {ex.Message}"; }
    }

    [McpServerTool(Name = "node_instance_scene")]
    [Description("Bir PackedScene'i sahneye instance eder (yerleştirir).")]
    public async Task<string> InstanceScene(
        [Description("Instance edilecek .tscn yolu")] string scenePath,
        [Description("Üst node yolu (opsiyonel)")] string? parentPath = null,
        [Description("Instance ismi (opsiyonel)")] string? name = null)
    {
        try
        {
            var result = await bridge.SendAsync("node_instance_scene", new() { ["scene_path"] = scenePath, ["parent_path"] = parentPath, ["name"] = name });
            return result.Success ? "Sahne instance edildi." : $"[GodotMCP Hata] {result.Error}";
        }
        catch (TimeoutException) { return "[GodotMCP] Godot yanıt vermedi."; }
        catch (Exception ex) { return $"[GodotMCP] Beklenmedik hata: {ex.Message}"; }
    }
}
