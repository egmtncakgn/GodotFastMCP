# GÖREV: Godot 4 MCP Server — Tam C# Implementasyonu

## Bağlam ve Amaç

Godot 4 editor'unu OpenCode (ve diğer MCP-uyumlu istemcilere) bağlayan, **tamamen C# ile yazılmış**
bir MCP (Model Context Protocol) sistemi inşa etmeni istiyorum.

Sistem **iki bileşenden** oluşur:

```
OpenCode (MCP Client)
    │  stdio / JSON-RPC 2.0
    ▼
[BİLEŞEN 1] GodotMcpServer/   ← .NET 8 Console App (C# MCP Server)
    │  WebSocket  ws://localhost:6505
    ▼
[BİLEŞEN 2] addons/godot_mcp/  ← Godot 4 EditorPlugin (GDScript)
    │  EditorInterface API
    ▼
Godot 4 Editor
```

Mevcut en iyi referans projelerin mimarisini incele:
- https://github.com/mkdevkit/godot-mcp  (WebSocket köprü mimarisi)
- https://github.com/IvanMurzak/Godot-MCP  (C# addon yapısı)
- https://github.com/tomyud1/godot-mcp  (42 araç, tool kategorileri)

---

## BÖLÜM 1 — C# MCP Server (GodotMcpServer/)

### 1.1 Proje Yapısı

```
GodotMcpServer/
├── GodotMcpServer.csproj
├── Program.cs
├── GodotBridge.cs          ← WebSocket bağlantı yöneticisi
├── Protocol/
│   ├── GodotRequest.cs     ← Godot'a gönderilen JSON mesaj modeli
│   └── GodotResponse.cs    ← Godot'tan gelen JSON yanıt modeli
└── Tools/
    ├── SceneTools.cs
    ├── NodeTools.cs
    ├── ScriptTools.cs
    ├── EditorTools.cs
    ├── FileSystemTools.cs
    ├── ResourceTools.cs
    ├── ConsoleTools.cs
    └── ScreenshotTools.cs
```

### 1.2 GodotMcpServer.csproj

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <AssemblyName>GodotMcpServer</AssemblyName>
  </PropertyGroup>

  <ItemGroup>
    <!-- Resmi Microsoft/Anthropic C# MCP SDK — v1.0 sonrası stable, --prerelease GEREKMEZ -->
    <PackageReference Include="ModelContextProtocol" Version="1.2.0" />
    <PackageReference Include="Microsoft.Extensions.Hosting" Version="8.0.1" />
    <PackageReference Include="Microsoft.Extensions.Logging.Console" Version="8.0.1" />
    <!-- WebSocket için System.Net.WebSockets built-in, ama client helper için: -->
    <PackageReference Include="Websocket.Client" Version="5.1.2" />
    <PackageReference Include="System.Text.Json" Version="8.0.5" />
  </ItemGroup>
</Project>
```

### 1.3 Program.cs

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

var builder = Host.CreateApplicationBuilder(args);

// Tüm logları stderr'e yönlendir (stdout MCP protokolü için ayrılmış)
builder.Logging.AddConsole(opts =>
    opts.LogToStandardErrorThreshold = LogLevel.Trace);

// Godot WebSocket köprüsü — singleton olarak kayıt
builder.Services.AddSingleton<GodotBridge>();

// MCP server: stdio transport + assembly'deki tüm [McpServerToolType] sınıflarını tara
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

var host = builder.Build();

// Başlangıçta Godot'a bağlanmayı dene (non-fatal, araçlar çağrıldıkça yeniden dener)
var bridge = host.Services.GetRequiredService<GodotBridge>();
_ = bridge.ConnectAsync(CancellationToken.None);

await host.RunAsync();
```

### 1.4 Protocol/GodotRequest.cs

```csharp
using System.Text.Json.Serialization;

/// <summary>
/// Godot EditorPlugin'e WebSocket üzerinden gönderilen JSON-RPC benzeri mesaj.
/// </summary>
public class GodotRequest
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("command")]
    public required string Command { get; init; }

    [JsonPropertyName("params")]
    public Dictionary<string, object?>? Params { get; init; }
}
```

### 1.5 Protocol/GodotResponse.cs

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// Godot EditorPlugin'den gelen JSON yanıt.
/// </summary>
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
}
```

### 1.6 GodotBridge.cs

WebSocket bağlantı yöneticisi. Thread-safe request/response eşleştirmesi için
`ConcurrentDictionary<string, TaskCompletionSource<GodotResponse>>` kullan.

```csharp
using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Websocket.Client;

public class GodotBridge : IAsyncDisposable
{
    private const string DefaultUrl = "ws://localhost:6505";
    private readonly ILogger<GodotBridge> _logger;
    private WebsocketClient? _client;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<GodotResponse>> _pending = new();
    private readonly SemaphoreSlim _connectLock = new(1, 1);
    private bool _connected;

    public GodotBridge(ILogger<GodotBridge> logger) => _logger = logger;

    public bool IsConnected => _connected && (_client?.IsRunning ?? false);

    /// <summary>
    /// Godot plugin WebSocket sunucusuna bağlan.
    /// Bağlantı kesilirse otomatik yeniden bağlan.
    /// </summary>
    public async Task ConnectAsync(CancellationToken ct)
    {
        await _connectLock.WaitAsync(ct);
        try
        {
            if (IsConnected) return;

            var url = new Uri(Environment.GetEnvironmentVariable("GODOT_MCP_WS_URL") ?? DefaultUrl);
            _logger.LogInformation("Godot'a bağlanılıyor: {Url}", url);

            _client = new WebsocketClient(url)
            {
                ReconnectTimeout = TimeSpan.FromSeconds(10),
                ErrorReconnectTimeout = TimeSpan.FromSeconds(5),
                IsReconnectionEnabled = true
            };

            _client.MessageReceived.Subscribe(msg =>
            {
                if (msg.Text is null) return;
                try
                {
                    var response = JsonSerializer.Deserialize<GodotResponse>(msg.Text);
                    if (response?.Id is not null && _pending.TryRemove(response.Id, out var tcs))
                        tcs.TrySetResult(response);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "WebSocket mesajı ayrıştırılamadı: {Msg}", msg.Text);
                }
            });

            _client.DisconnectionHappened.Subscribe(info =>
            {
                _connected = false;
                _logger.LogWarning("Godot bağlantısı kesildi: {Type}", info.Type);
                // Bekleyen tüm istekleri iptal et
                foreach (var (_, tcs) in _pending)
                    tcs.TrySetException(new InvalidOperationException("Godot bağlantısı kesildi."));
                _pending.Clear();
            });

            _client.ReconnectionHappened.Subscribe(info =>
            {
                _connected = true;
                _logger.LogInformation("Godot'a yeniden bağlanıldı: {Type}", info.Type);
            });

            await _client.Start();
            _connected = true;
            _logger.LogInformation("Godot'a başarıyla bağlanıldı.");
        }
        catch (Exception ex)
        {
            _connected = false;
            _logger.LogWarning("Godot bağlantısı kurulamadı (Godot açık değil olabilir): {Message}", ex.Message);
        }
        finally
        {
            _connectLock.Release();
        }
    }

    /// <summary>
    /// Godot'a komut gönder, yanıtı bekle.
    /// Bağlı değilse önce bağlanmayı dener.
    /// </summary>
    public async Task<GodotResponse> SendAsync(
        string command,
        Dictionary<string, object?>? parameters = null,
        int timeoutSeconds = 30,
        CancellationToken ct = default)
    {
        if (!IsConnected)
        {
            await ConnectAsync(ct);
            if (!IsConnected)
                return new GodotResponse
                {
                    Success = false,
                    Error = "Godot Editor'e bağlanılamadı. Godot'un açık olduğundan ve " +
                             "GodotMCP eklentisinin etkin olduğundan emin olun."
                };
        }

        var request = new GodotRequest { Command = command, Params = parameters };
        var json = JsonSerializer.Serialize(request);
        var tcs = new TaskCompletionSource<GodotResponse>(TaskCreationOptions.RunContinuationsAsynchronously);

        _pending[request.Id] = tcs;

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        timeoutCts.Token.Register(() =>
        {
            if (_pending.TryRemove(request.Id, out var t))
                t.TrySetException(new TimeoutException($"Godot yanıt vermedi ({timeoutSeconds}s)."));
        });

        _client!.Send(json);

        return await tcs.Task;
    }

    public async ValueTask DisposeAsync()
    {
        if (_client is not null)
            await _client.Stop(System.Net.WebSockets.WebSocketCloseStatus.NormalClosure, "MCP kapanıyor");
    }
}
```

---

## BÖLÜM 2 — MCP Tools (C# Tool Sınıfları)

Her tool sınıfı şu pattern'ı izler:

```csharp
using System.ComponentModel;
using ModelContextProtocol.Server;

[McpServerToolType]
public class SceneTools(GodotBridge bridge)
{
    [McpServerTool("scene_open")]
    [Description("Godot Editor'de belirtilen sahneyi açar. path parametresi res:// ile başlamalıdır.")]
    public async Task<string> OpenScene(
        [Description("Açılacak sahnenin proje-içi yolu. Örnek: res://scenes/Main.tscn")]
        string path)
    {
        var result = await bridge.SendAsync("scene_open", new() { ["path"] = path });
        return result.Success
            ? $"Sahne açıldı: {path}"
            : $"Hata: {result.Error}";
    }
    // ... diğer araçlar
}
```

### 2.1 Tools/SceneTools.cs — Sahne Yönetimi

Şu araçları implement et:

| Tool Adı | Komut | Parametreler | Açıklama |
|---|---|---|---|
| `scene_open` | `scene_open` | `path: string` | Sahneyi editor'de aç |
| `scene_save` | `scene_save` | `path?: string` | Aktif sahneyi kaydet (path yoksa mevcut konuma) |
| `scene_create` | `scene_create` | `path: string`, `root_node_type: string` | Yeni .tscn oluştur |
| `scene_list_opened` | `scene_list_opened` | — | Açık sahnelerin listesi |
| `scene_get_data` | `scene_get_data` | `path?: string` | Sahne ağacı yapısını al |
| `scene_close` | `scene_close` | `path?: string` | Sahneyi kapat |

### 2.2 Tools/NodeTools.cs — Node İşlemleri

| Tool Adı | Komut | Parametreler | Açıklama |
|---|---|---|---|
| `node_find` | `node_find` | `name?: string`, `type?: string`, `path?: string` | Node bul (birden fazla kriter) |
| `node_create` | `node_create` | `type: string`, `name: string`, `parent_path?: string` | Yeni node oluştur |
| `node_get_properties` | `node_get_properties` | `node_path: string` | Node'un tüm property'lerini getir |
| `node_set_property` | `node_set_property` | `node_path: string`, `property: string`, `value: object` | Tek property ayarla |
| `node_set_properties` | `node_set_properties` | `node_path: string`, `properties: dict` | Çok sayıda property toplu ayarla |
| `node_delete` | `node_delete` | `node_path: string` | Node'u sil |
| `node_reparent` | `node_reparent` | `node_path: string`, `new_parent_path: string` | Node'u taşı |
| `node_duplicate` | `node_duplicate` | `node_path: string`, `new_name?: string` | Node'u kopyala |
| `node_rename` | `node_rename` | `node_path: string`, `new_name: string` | Node'u yeniden adlandır |
| `node_instance_scene` | `node_instance_scene` | `scene_path: string`, `parent_path?: string`, `name?: string` | Packed scene'i sahnelere yerleştir |

### 2.3 Tools/ScriptTools.cs — Script İşlemleri

| Tool Adı | Komut | Parametreler | Açıklama |
|---|---|---|---|
| `script_read` | `script_read` | `path: string` | Script içeriğini oku (.gd veya .cs) |
| `script_create` | `script_create` | `path: string`, `content: string`, `language?: string` | Yeni script oluştur |
| `script_update` | `script_update` | `path: string`, `content: string` | Script içeriğini güncelle |
| `script_delete` | `script_delete` | `path: string` | Script dosyasını sil |
| `script_attach_to_node` | `script_attach_to_node` | `node_path: string`, `script_path: string` | Script'i node'a bağla |
| `script_get_errors` | `script_get_errors` | `path?: string` | Aktif script hatalarını getir |

### 2.4 Tools/EditorTools.cs — Editor Kontrolü

| Tool Adı | Komut | Parametreler | Açıklama |
|---|---|---|---|
| `editor_play` | `editor_play` | — | Oyunu başlat (F5 eşdeğeri) |
| `editor_stop` | `editor_stop` | — | Oyunu durdur |
| `editor_pause` | `editor_pause` | — | Oyunu duraklat/devam ettir |
| `editor_get_state` | `editor_get_state` | — | Editor durumu: playing/paused/stopped |
| `editor_selection_get` | `editor_selection_get` | — | Seçili node listesi |
| `editor_selection_set` | `editor_selection_set` | `node_paths: string[]` | Node'ları seç |
| `editor_get_project_settings` | `editor_get_project_settings` | `keys?: string[]` | Proje ayarlarını oku |
| `editor_set_project_setting` | `editor_set_project_setting` | `key: string`, `value: object` | Proje ayarı yaz |
| `editor_get_project_path` | `editor_get_project_path` | — | project.godot dizini |

### 2.5 Tools/FileSystemTools.cs — Dosya Sistemi

| Tool Adı | Komut | Parametreler | Açıklama |
|---|---|---|---|
| `filesystem_list` | `filesystem_list` | `path?: string`, `recursive?: bool` | res:// ağacını listele |
| `filesystem_read_file` | `filesystem_read_file` | `path: string` | Dosya içeriğini oku (metin) |
| `filesystem_write_file` | `filesystem_write_file` | `path: string`, `content: string` | Dosya yaz/oluştur |
| `filesystem_delete` | `filesystem_delete` | `path: string` | Dosya veya dizin sil |
| `filesystem_move` | `filesystem_move` | `from: string`, `to: string` | Dosya taşı/yeniden adlandır |
| `filesystem_reimport` | `filesystem_reimport` | `path?: string` | Asset'i yeniden içe aktar |
| `filesystem_search` | `filesystem_search` | `pattern: string`, `path?: string`, `type?: string` | Dosya/kaynak ara |

### 2.6 Tools/ResourceTools.cs — Kaynak Yönetimi

| Tool Adı | Komut | Parametreler | Açıklama |
|---|---|---|---|
| `resource_get_data` | `resource_get_data` | `path: string` | .tres/.res içeriğini oku |
| `resource_modify` | `resource_modify` | `path: string`, `properties: dict` | Resource property'lerini değiştir |
| `resource_create` | `resource_create` | `path: string`, `type: string`, `properties?: dict` | Yeni resource oluştur |
| `resource_delete` | `resource_delete` | `path: string` | Resource sil |

### 2.7 Tools/ConsoleTools.cs — Konsol/Log

| Tool Adı | Komut | Parametreler | Açıklama |
|---|---|---|---|
| `console_get_logs` | `console_get_logs` | `count?: int`, `level?: string` | Editor log'larını getir |
| `console_clear_logs` | `console_clear_logs` | — | Log önbelleğini temizle |
| `console_get_errors` | `console_get_errors` | — | Sadece hata/uyarı logları |

### 2.8 Tools/ScreenshotTools.cs — Ekran Görüntüsü

| Tool Adı | Komut | Parametreler | Açıklama |
|---|---|---|---|
| `screenshot_viewport` | `screenshot_viewport` | — | Editor viewport'tan PNG al (base64 döner) |
| `screenshot_game` | `screenshot_game` | — | Oyun çalışıyorsa game window'dan PNG al |

Screenshot tool'ları için dönen değer:
```json
{ "image_base64": "iVBORw0KGgo...", "width": 1280, "height": 720 }
```

---

## BÖLÜM 3 — Godot EditorPlugin (GDScript)

Bu bileşen Godot 4 projesine `addons/godot_mcp/` altında kurulur.

### 3.1 Dosya Yapısı

```
addons/godot_mcp/
├── plugin.cfg
├── plugin.gd              ← EditorPlugin ana sınıfı
├── mcp_server.gd          ← WebSocket sunucu
├── log_collector.gd       ← print/push_error yakaları
├── command_dispatcher.gd  ← komut → fonksiyon yönlendirici
└── commands/
    ├── cmd_scene.gd
    ├── cmd_node.gd
    ├── cmd_script.gd
    ├── cmd_editor.gd
    ├── cmd_filesystem.gd
    ├── cmd_resource.gd
    └── cmd_screenshot.gd
```

### 3.2 plugin.cfg

```ini
[plugin]
name="GodotMCP"
description="MCP (Model Context Protocol) bridge for AI-assisted development"
author="Senin İsmin"
version="1.0.0"
script="plugin.gd"
```

### 3.3 plugin.gd (Ana EditorPlugin)

```gdscript
@tool
extends EditorPlugin

var mcp_server: McpServer
var log_collector: LogCollector

func _enter_tree() -> void:
    log_collector = LogCollector.new()
    add_child(log_collector)
    
    mcp_server = McpServer.new(get_editor_interface(), log_collector)
    add_child(mcp_server)
    mcp_server.start(6505)
    
    push_warning("[GodotMCP] Plugin yüklendi, WebSocket port 6505'te dinliyor.")

func _exit_tree() -> void:
    if mcp_server:
        mcp_server.stop()
    if log_collector:
        log_collector.queue_free()
```

### 3.4 mcp_server.gd (WebSocket Sunucu)

Godot 4'ün `WebSocketServer` / `TCPServer` → `WebSocketPeer` akışını kullan.
Her gelen JSON mesajını `command_dispatcher.gd`'ye ilet, yanıtı geri gönder.

**ÖNEMLİ:** Tüm EditorInterface çağrıları ana thread'de yapılmalıdır.
`call_deferred()` veya direkt GDScript ana döngüsünden çağır.

```gdscript
@tool
extends Node

class_name McpServer

const PORT_DEFAULT = 6505

var _tcp_server: TCPServer
var _peer: WebSocketPeer
var _dispatcher: CommandDispatcher
var _port: int

func start(port: int = PORT_DEFAULT) -> void:
    _port = port
    _dispatcher = CommandDispatcher.new(editor_interface, log_collector)
    add_child(_dispatcher)
    
    _tcp_server = TCPServer.new()
    if _tcp_server.listen(_port) != OK:
        push_error("[GodotMCP] Port %d dinlenemiyor!" % _port)
        return
    push_warning("[GodotMCP] WebSocket sunucusu port %d'de başlatıldı." % _port)

func _process(_delta: float) -> void:
    # Yeni bağlantı kontrolü
    if _tcp_server and _tcp_server.is_connection_available():
        var stream = _tcp_server.take_connection()
        _peer = WebSocketPeer.new()
        _peer.accept_stream(stream)
    
    if not _peer:
        return
    
    _peer.poll()
    
    var state = _peer.get_ready_state()
    if state == WebSocketPeer.STATE_OPEN:
        while _peer.get_available_packet_count() > 0:
            var data = _peer.get_packet().get_string_from_utf8()
            _handle_message(data)
    elif state in [WebSocketPeer.STATE_CLOSING, WebSocketPeer.STATE_CLOSED]:
        _peer = null

func _handle_message(json_text: String) -> void:
    var parsed = JSON.parse_string(json_text)
    if not parsed:
        push_error("[GodotMCP] JSON ayrıştırma hatası: " + json_text)
        return
    
    var request_id: String = parsed.get("id", "")
    var command: String = parsed.get("command", "")
    var params: Dictionary = parsed.get("params", {})
    
    # Komutu dispatcher'a ilet
    var result = await _dispatcher.dispatch(command, params)
    
    # Yanıtı gönder
    var response = {
        "id": request_id,
        "success": result.get("success", false),
        "result": result.get("result", null),
        "error": result.get("error", null)
    }
    
    if _peer and _peer.get_ready_state() == WebSocketPeer.STATE_OPEN:
        _peer.send_text(JSON.stringify(response))

func stop() -> void:
    if _peer:
        _peer.close()
    if _tcp_server:
        _tcp_server.stop()
    push_warning("[GodotMCP] Sunucu durduruldu.")

var editor_interface: EditorInterface
var log_collector: LogCollector

func _init(ei: EditorInterface, lc: LogCollector) -> void:
    editor_interface = ei
    log_collector = lc
```

### 3.5 command_dispatcher.gd

```gdscript
@tool
extends Node
class_name CommandDispatcher

var _ei: EditorInterface
var _lc: LogCollector
var _handlers: Dictionary = {}

func _init(ei: EditorInterface, lc: LogCollector) -> void:
    _ei = ei
    _lc = lc

func _ready() -> void:
    # Tüm komut handler'larını kayıt et
    var scene_cmd = preload("res://addons/godot_mcp/commands/cmd_scene.gd").new(_ei)
    var node_cmd  = preload("res://addons/godot_mcp/commands/cmd_node.gd").new(_ei)
    var script_cmd = preload("res://addons/godot_mcp/commands/cmd_script.gd").new(_ei)
    var editor_cmd = preload("res://addons/godot_mcp/commands/cmd_editor.gd").new(_ei)
    var fs_cmd = preload("res://addons/godot_mcp/commands/cmd_filesystem.gd").new(_ei)
    var res_cmd = preload("res://addons/godot_mcp/commands/cmd_resource.gd").new(_ei)
    var screenshot_cmd = preload("res://addons/godot_mcp/commands/cmd_screenshot.gd").new(_ei)
    
    # Sahne komutları
    _handlers["scene_open"]         = scene_cmd.open_scene
    _handlers["scene_save"]         = scene_cmd.save_scene
    _handlers["scene_create"]       = scene_cmd.create_scene
    _handlers["scene_list_opened"]  = scene_cmd.list_opened
    _handlers["scene_get_data"]     = scene_cmd.get_data
    _handlers["scene_close"]        = scene_cmd.close_scene
    
    # Node komutları
    _handlers["node_find"]              = node_cmd.find_node
    _handlers["node_create"]            = node_cmd.create_node
    _handlers["node_get_properties"]    = node_cmd.get_properties
    _handlers["node_set_property"]      = node_cmd.set_property
    _handlers["node_set_properties"]    = node_cmd.set_properties
    _handlers["node_delete"]            = node_cmd.delete_node
    _handlers["node_reparent"]          = node_cmd.reparent_node
    _handlers["node_duplicate"]         = node_cmd.duplicate_node
    _handlers["node_rename"]            = node_cmd.rename_node
    _handlers["node_instance_scene"]    = node_cmd.instance_scene
    
    # Script komutları
    _handlers["script_read"]            = script_cmd.read_script
    _handlers["script_create"]          = script_cmd.create_script
    _handlers["script_update"]          = script_cmd.update_script
    _handlers["script_delete"]          = script_cmd.delete_script
    _handlers["script_attach_to_node"]  = script_cmd.attach_to_node
    _handlers["script_get_errors"]      = script_cmd.get_errors
    
    # Editor komutları
    _handlers["editor_play"]                    = editor_cmd.play
    _handlers["editor_stop"]                    = editor_cmd.stop
    _handlers["editor_pause"]                   = editor_cmd.pause
    _handlers["editor_get_state"]               = editor_cmd.get_state
    _handlers["editor_selection_get"]           = editor_cmd.selection_get
    _handlers["editor_selection_set"]           = editor_cmd.selection_set
    _handlers["editor_get_project_settings"]    = editor_cmd.get_project_settings
    _handlers["editor_set_project_setting"]     = editor_cmd.set_project_setting
    _handlers["editor_get_project_path"]        = editor_cmd.get_project_path
    
    # Dosya sistemi
    _handlers["filesystem_list"]        = fs_cmd.list_files
    _handlers["filesystem_read_file"]   = fs_cmd.read_file
    _handlers["filesystem_write_file"]  = fs_cmd.write_file
    _handlers["filesystem_delete"]      = fs_cmd.delete_file
    _handlers["filesystem_move"]        = fs_cmd.move_file
    _handlers["filesystem_reimport"]    = fs_cmd.reimport
    _handlers["filesystem_search"]      = fs_cmd.search_files
    
    # Resource
    _handlers["resource_get_data"]  = res_cmd.get_data
    _handlers["resource_modify"]    = res_cmd.modify
    _handlers["resource_create"]    = res_cmd.create_resource
    _handlers["resource_delete"]    = res_cmd.delete_resource
    
    # Konsol
    _handlers["console_get_logs"]   = func(p): return _lc.get_logs(p)
    _handlers["console_clear_logs"] = func(p): _lc.clear(); return {"success": true}
    _handlers["console_get_errors"] = func(p): return _lc.get_errors(p)
    
    # Screenshot
    _handlers["screenshot_viewport"] = screenshot_cmd.capture_viewport
    _handlers["screenshot_game"]     = screenshot_cmd.capture_game

func dispatch(command: String, params: Dictionary) -> Dictionary:
    if not _handlers.has(command):
        return {"success": false, "error": "Bilinmeyen komut: " + command}
    
    var handler = _handlers[command]
    var result = await handler.call(params)
    return result
```

### 3.6 commands/cmd_scene.gd (Örnek Komut Implementasyonu)

Her komut şu pattern'ı izler:

```gdscript
@tool
extends RefCounted

var _ei: EditorInterface

func _init(ei: EditorInterface) -> void:
    _ei = ei

func open_scene(params: Dictionary) -> Dictionary:
    var path: String = params.get("path", "")
    if path.is_empty():
        return {"success": false, "error": "path parametresi gerekli."}
    if not FileAccess.file_exists(path):
        return {"success": false, "error": "Dosya bulunamadı: " + path}
    
    _ei.open_scene_from_path(path)
    return {"success": true, "result": {"path": path, "message": "Sahne açıldı."}}

func save_scene(params: Dictionary) -> Dictionary:
    var scene = _ei.get_edited_scene_root()
    if not scene:
        return {"success": false, "error": "Açık sahne yok."}
    _ei.save_scene()
    return {"success": true, "result": {"message": "Sahne kaydedildi."}}

func create_scene(params: Dictionary) -> Dictionary:
    var path: String = params.get("path", "")
    var root_type: String = params.get("root_node_type", "Node2D")
    if path.is_empty():
        return {"success": false, "error": "path gerekli."}
    
    var packed = PackedScene.new()
    var root = ClassDB.instantiate(root_type)
    if not root:
        return {"success": false, "error": "Geçersiz node tipi: " + root_type}
    root.name = path.get_file().get_basename()
    packed.pack(root)
    
    var err = ResourceSaver.save(packed, path)
    if err != OK:
        return {"success": false, "error": "Sahne kaydedilemedi. Hata kodu: " + str(err)}
    
    _ei.open_scene_from_path(path)
    return {"success": true, "result": {"path": path}}

func list_opened(params: Dictionary) -> Dictionary:
    var scenes = []
    for i in _ei.get_open_scenes().size():
        scenes.append(_ei.get_open_scenes()[i].get_path())
    return {"success": true, "result": {"scenes": scenes}}

func get_data(params: Dictionary) -> Dictionary:
    var scene = _ei.get_edited_scene_root()
    if not scene:
        return {"success": false, "error": "Açık sahne yok."}
    return {"success": true, "result": _node_to_dict(scene)}

func _node_to_dict(node: Node, depth: int = 0) -> Dictionary:
    if depth > 10:  # Sonsuz özyinelemeyi önle
        return {}
    var data = {
        "name": node.name,
        "type": node.get_class(),
        "path": str(node.get_path()),
        "children": []
    }
    for child in node.get_children():
        data["children"].append(_node_to_dict(child, depth + 1))
    return data

func close_scene(params: Dictionary) -> Dictionary:
    _ei.close_scene()
    return {"success": true, "result": {"message": "Sahne kapatıldı."}}
```

Aynı pattern'la tüm diğer cmd_*.gd dosyalarını implement et.

### 3.7 log_collector.gd

```gdscript
@tool
extends Node
class_name LogCollector

const MAX_LOGS = 500

var _logs: Array[Dictionary] = []

# Godot'un çıktı log'larını yakalamak için EditorPlugin ile entegre ol
func collect(message: String, level: String = "info") -> void:
    _logs.append({
        "timestamp": Time.get_unix_time_from_system(),
        "level": level,
        "message": message
    })
    if _logs.size() > MAX_LOGS:
        _logs.pop_front()

func get_logs(params: Dictionary) -> Dictionary:
    var count: int = params.get("count", 50)
    var level: String = params.get("level", "")
    
    var filtered = _logs
    if not level.is_empty():
        filtered = _logs.filter(func(l): return l["level"] == level)
    
    var result = filtered.slice(max(0, filtered.size() - count), filtered.size())
    return {"success": true, "result": {"logs": result, "total": _logs.size()}}

func get_errors(params: Dictionary) -> Dictionary:
    var errors = _logs.filter(func(l): return l["level"] in ["error", "warning"])
    return {"success": true, "result": {"errors": errors}}

func clear() -> void:
    _logs.clear()
```

---

## BÖLÜM 4 — OpenCode Konfigürasyonu

### 4.1 ~/.config/opencode/opencode.json veya proje kökündeki opencode.json

```json
{
  "$schema": "https://opencode.ai/config.json",
  "mcp": {
    "godot": {
      "type": "local",
      "command": ["dotnet", "run", "--project", "/path/to/GodotMcpServer/GodotMcpServer.csproj"],
      "enabled": true,
      "environment": {
        "GODOT_MCP_WS_URL": "ws://localhost:6505"
      }
    }
  }
}
```

Production build için (`dotnet publish` sonrası):
```json
{
  "mcp": {
    "godot": {
      "type": "local",
      "command": ["/path/to/publish/GodotMcpServer"],
      "enabled": true
    }
  }
}
```

---

## BÖLÜM 5 — Hata Yönetimi ve Edge Case'ler

Her MCP tool şu durumları ele almalı:

1. **Godot bağlı değil**: Bridge zaten hata mesajı döner, tool bunu kullanıcıya iletir.
2. **Zaman aşımı**: `GodotBridge.SendAsync` TimeoutException atar → tool catch eder, anlamlı hata döner.
3. **Geçersiz parametreler**: Tool seviyesinde null/boş kontrol → erken hata döner.
4. **Godot-side hatalar**: Godot plugin `"success": false, "error": "..."` döner → tool bunu iletir.

```csharp
// Tüm tool'larda try-catch wrapper pattern:
try
{
    var result = await bridge.SendAsync(command, params);
    return result.Success
        ? FormatSuccess(result)
        : $"[GodotMCP Hata] {result.Error}";
}
catch (TimeoutException)
{
    return "[GodotMCP] Godot yanıt vermedi. Sahne veya komut çok karmaşık olabilir.";
}
catch (Exception ex)
{
    return $"[GodotMCP] Beklenmedik hata: {ex.Message}";
}
```

---

## BÖLÜM 6 — İmplementasyon Sırası

Şu sırada implement et:

1. **[ÖNCE]** `GodotMcpServer.csproj` + `Program.cs` + `GodotBridge.cs` + Protocol modelleri
2. **[SONRA]** Godot plugin: `plugin.cfg` + `plugin.gd` + `mcp_server.gd` + `command_dispatcher.gd` + `log_collector.gd`
3. **[SONRA]** Komut dosyaları: önce `cmd_scene.gd`, `cmd_node.gd`, `cmd_script.gd` (en sık kullanılanlar)
4. **[SONRA]** `cmd_editor.gd`, `cmd_filesystem.gd`, `cmd_resource.gd`, `cmd_screenshot.gd`
5. **[SONRA]** C# tool sınıfları: `SceneTools.cs`, `NodeTools.cs`, `ScriptTools.cs`
6. **[SONRA]** Kalan C# tool'lar: `EditorTools.cs`, `FileSystemTools.cs`, `ResourceTools.cs`, `ConsoleTools.cs`, `ScreenshotTools.cs`
7. **[SON]** `opencode.json` örneği + README.md

---

## BÖLÜM 7 — Test Senaryoları

İmplementasyon bittikten sonra şunları test et:

```bash
# 1. MCP server'ı başlat (MCP Inspector ile test)
npx @modelcontextprotocol/inspector dotnet run --project GodotMcpServer/

# 2. Godot 4'ü aç, eklentiyi etkinleştir, port 6505'in açıldığını doğrula

# 3. OpenCode'da test komutları:
# "Godot'ta yeni bir Node2D sahne oluştur: res://scenes/TestScene.tscn"
# "Sahnede Player adında bir CharacterBody2D oluştur"
# "res://scripts/player.gd dosyasını oku"
# "Editor'ın mevcut durumunu getir"
# "Son 20 log satırını göster"
```

---

## Önemli Notlar

1. **`stdout` SADECE MCP protokolü için**: `Console.WriteLine` KULLANMA, tüm loglar `stderr`'e gitmeli
2. **Thread safety Godot'ta**: Tüm `EditorInterface` çağrıları ana thread'de yapılmalı. GDScript'te
   `await get_tree().process_frame` veya `call_deferred()` kullan
3. **`res://` prefix'i**: Tüm Godot path'leri `res://` ile başlamalı, MCP tarafı da bunu zorlamalı
4. **WebSocket tek bağlantı**: Aynı anda yalnızca bir MCP client bağlanabilir (Godot editörü tekil)
5. **Godot 4.3+ gerekli**: `WebSocketPeer.accept_stream()` API'si 4.3'te stabil oldu
6. **C# SDK versiyonu**: `ModelContextProtocol` v1.2.0 kullan, `--prerelease` flag artık GEREKMİYOR

---

## Teslim Edilecekler (Checklist)

- [ ] `GodotMcpServer/GodotMcpServer.csproj`
- [ ] `GodotMcpServer/Program.cs`
- [ ] `GodotMcpServer/GodotBridge.cs`
- [ ] `GodotMcpServer/Protocol/GodotRequest.cs`
- [ ] `GodotMcpServer/Protocol/GodotResponse.cs`
- [ ] `GodotMcpServer/Tools/SceneTools.cs`
- [ ] `GodotMcpServer/Tools/NodeTools.cs`
- [ ] `GodotMcpServer/Tools/ScriptTools.cs`
- [ ] `GodotMcpServer/Tools/EditorTools.cs`
- [ ] `GodotMcpServer/Tools/FileSystemTools.cs`
- [ ] `GodotMcpServer/Tools/ResourceTools.cs`
- [ ] `GodotMcpServer/Tools/ConsoleTools.cs`
- [ ] `GodotMcpServer/Tools/ScreenshotTools.cs`
- [ ] `addons/godot_mcp/plugin.cfg`
- [ ] `addons/godot_mcp/plugin.gd`
- [ ] `addons/godot_mcp/mcp_server.gd`
- [ ] `addons/godot_mcp/command_dispatcher.gd`
- [ ] `addons/godot_mcp/log_collector.gd`
- [ ] `addons/godot_mcp/commands/cmd_scene.gd`
- [ ] `addons/godot_mcp/commands/cmd_node.gd`
- [ ] `addons/godot_mcp/commands/cmd_script.gd`
- [ ] `addons/godot_mcp/commands/cmd_editor.gd`
- [ ] `addons/godot_mcp/commands/cmd_filesystem.gd`
- [ ] `addons/godot_mcp/commands/cmd_resource.gd`
- [ ] `addons/godot_mcp/commands/cmd_screenshot.gd`
- [ ] `opencode.json` (örnek konfigürasyon)
- [ ] `README.md` (kurulum talimatları)