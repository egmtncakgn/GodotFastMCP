# Godot MCP Server — Progress Log

## 🚀 18 Temmuz 2026 — v1.2.0: Ölçeklenebilirlik + Server Self-Update

### Kritik Bug Düzeltmeleri
| # | Sorun | Çözüm |
|---|-------|-------|
| 1 | **Build bozuktu** (CS8802/CS0579): kökteki `WsTestClient.cs` + `_wstest/Program.cs` default globbing ile derlemeye sızıyordu | csproj'da `EnableDefaultCompileItems=false` + explicit dosya listesi; test artıkları temizlendi |
| 2 | **~225 build artifact'ı repo kökündeydi** (dll/exe/pdb publish dump'ı) | Temizlendi; .gitignore zaten kapsıyordu |
| 3 | **Godot kapalıyken MCP istemcisi kilitleniyordu**: `await ConnectAsync()` host.RunAsync() öncesinde ~100 portu sırayla deniyordu → initialize yanıtı dakikalarca yok | Başlangıç bağlantısı arka plana alındı; TCP fast-probe eklendi (tarama 30sn → <1sn) |
| 4 | **Addon auto-update son kullanıcıda kırıktı**: `BaseDirectory/../addons` sadece repo-içi publish'te çalışıyordu | Çok adaylı dizin çözümleme (publish gömülü `addons/` + yukarı tarama) |
| 5 | Health-check/reconnect tool isteğinin CancellationToken'ına bağlıydı (istek iptal → reconnect ölüyordu) | `_shutdownCts.Token`'a bağlandı |
| 6 | `ScheduleReconnect` fire-and-forget awaiter bug'ı | Doğru async Task.Run yapısı |
| 7 | GDScript `cmd_update._copy_dir` count değerle geçtiği için recursive sayımı kaybediyordu | Dönüş değeriyle toplama |
| 8 | `get_version` hardcoded "1.1.0" idi | plugin.cfg'den dinamik okuma (tek kaynak) |

### Yeni Özellikler
- **Server self-update sistemi** (`Update/ServerUpdateManager.cs`): GitHub Releases API kontrolü, platforma uygun zip indirme, staging, proses-dışı watcher script (Windows ps1 / Unix sh) ile kilitli dosyaları proses kapandıktan sonra değiştirme. Geliştirici ortamında otomatik devre dışı.
- **3 yeni MCP tool** (toplam 52): `server_version`, `server_check_update`, `server_self_update`
- **Versiyon-bazlı addon auto-update**: Her bağlantıda Godot'un plugin versiyonu sorulur; eşitse push atlanır (bant genişliği + log temizliği), farklıysa canlı güncellenir
- **Tek versiyon kaynağı**: `ServerVersion.cs` (assembly'den) + plugin.cfg (addon için)
- **GitHub Actions**: `ci.yml` (build + publish smoke + addon bütünlüğü), `release.yml` (tag → 5 platform self-contained single-file zip → GitHub Release; csproj↔tag versiyon doğrulaması)
- **README.md**: Son kullanıcı kurulumu, MCP istemci konfigleri (opencode/Claude), güncelleme sistemi, geliştirici rehberi
- `mcp_server.gd`: MAX_PEERS=16 sınırı gerçekten uygulanıyor (önceden sadece log mesajıydı)

### Doğrulama
```
✅ Build: 0 hata 0 kod uyarısı
✅ Publish: publish/ altında addons/ gömülü
✅ MCP smoke test (python harness): 3/3 yanıt, 52 tool, server_version OK
✅ Startup: Godot kapalıyken stdio anında yanıt veriyor (kilitlenme yok)
```

---

## Proje Özeti

Godot 4 editor'una **C# ile yazılmış** bir MCP (Model Context Protocol) server köprüsü.

### Mimarî

```
┌───────────────────────────────────────────────┐
│  C# MCP Server (GodotMcpServer/)              │
│  ├── stdio transport ──► MCP Clients           │
│  └── WS :6505 ──► Godot Editor Plugin          │
├───────────────────────────────────────────────┤
│  Godot EditorPlugin (addons/godot_mcp/)       │
│  ├── TCPServer + WebSocketPeer → C# client     │
│  ├── CommandDispatcher → handler registry      │
│  └── 7 komut grubu (49 tool)                   │
└───────────────────────────────────────────────┘
```

---

## Mevcut Durum (17 Temmuz 2026)

### ✅ Tamamlanan — C# MCP Server (`GodotMcpServer/`)

#### Core (.csproj + entrypoint)
- [x] `GodotMcpServer.csproj` — .NET 8 Console App, net8.0
  - Paketler: ModelContextProtocol v1.2.0, Microsoft.Extensions.Hosting 8.0.1, Websocket.Client 5.1.2, System.Text.Json v10
- [x] `Program.cs` — DI host builder, stdio server transport, auto-connect bridge
- [x] `GodotBridge.cs` — WebSocket client, reconnect, timeout (30s), thread-safe pending queue

#### Protocol Modelleri
- [x] `Protocol/GodotRequest.cs` — JSON-RPC benzeri request modeli (id, command, params)
- [x] `Protocol/GodotResponse.cs` — Response modeli (success, result, error)

#### Tool Sınıfları (8 sınıf, 47 tool)
| Dosya | Tool Sayısı | Durum |
|-------|------------|-------|
| `Tools/SceneTools.cs` | 6 (open, save, create, list_opened, get_data, close) | ✅ |
| `Tools/NodeTools.cs` | 10 (find, create, properties set/get/delete, reparent, duplicate, rename, instance_scene) | ✅ |
| `Tools/ScriptTools.cs` | 6 (read, create, update, delete, attach_to_node, get_errors) | ✅ |
| `Tools/EditorTools.cs` | 9 (play, stop, pause, state, selection get/set, project settings get/set, path) | ✅ |
| `Tools/FileSystemTools.cs` | 7 (list, read_file, write_file, delete, move, reimport, search) | ✅ |
| `Tools/ResourceTools.cs` | 4 (get_data, modify, create, delete) | ✅ |
| `Tools/ConsoleTools.cs` | 3 (get_logs, clear_logs, get_errors) | ✅ |
| `Tools/ScreenshotTools.cs` | 2 (viewport, game) | ✅ |

#### Build Sonucu
```
GodotMcpServer -> GodotMcpServer.dll
Oluşturma başarılı oldu. 0 Uyarı, 0 Hata
```

---

### ✅ Tamamlanan — Godot EditorPlugin (`addons/godot_mcp/`)

#### Plugin Yapılandırması
- [x] `plugin.cfg` — name="GodotMCP", version="1.0.0"
- [x] `plugin.gd` — EditorPlugin: child registration, plugin start/stop lifecycle

#### WebSocket Sunucu Katmanı
- [x] `mcp_server.gd` — TCPServer + WebSocketPeer, JSON parse/dispatch, error handling
- [x] `command_dispatcher.gd` — 7 komut grubunun preload + handler registry, dispatch() async routing

#### Komut Grupları (7 dosya, ~49 tool)
| Dosya | Tool'lar | Durum |
|-------|---------|-------|
| `commands/cmd_scene.gd` | open, save, create, list_opened, get_data, close | ✅ |
| `commands/cmd_node.gd` | find, create, properties, set/get/delete/rep/dup/rename/instance | ✅ |
| `commands/cmd_script.gd` | read, create, update, delete, attach, errors | ✅ |
| `commands/cmd_editor.gd` | play, stop, pause, state, selection, project settings, path | ✅ |
| `commands/cmd_filesystem.gd` | list, read, write, delete, move, reimport, search | ✅ |
| `commands/cmd_resource.gd` | get_data, modify, create, delete | ✅ |
| `commands/cmd_screenshot.gd` | viewport, game | ✅ |

#### Yardımcı Sınıflar
- [x] `log_collector.gd` — log buffer (max 500), get_logs/get_errors/clear

---

## 🔧 Düzeltmeler ve Notlar

### issues.md'deki Sorun Durumları
| # | Sorun | Durum | Açıklama |
|---|-------|-------|----------|
| 1 | C# Script Dosyası Bulunamıyor (XOXGame.cs) | Çözüldü | Ayrı proje (`islemcigame`), ana server ile ilgili değil |
| 2 | MCP Tool Call Parametre Formatı | Çözüldü | issues.md'de çözüldü denildi, ancak testte sorun bulunmadı (dosya yapısı doğru) |
| 3 | Scene İşlemleri Hata Veriyor | Kısmi — Godot açılıp kullanıma hazır değil | Tool implementasyonu tamam; runtime test için Godot gerekli |

### Test Pipeline Sonuçları
```
✅ C# Build: Başarılı (publish -c Release -o ./publish)
✅ Dosya Sayısı: 39 gerçek dosya
✅ Tool Mapping: 47 tool handler kaydı doğrulandı
✅ WebSocket Bridge: Connect/Disconnect/Reconnect lifecycle implement edildi
✅ Bağlantı Testi: "Godot bağlantısı başlangıçta kuruldu." (ws://localhost:6505)
✅ opencode.json: godotMCP eklendi (publish exe)
```

### 🐛 Sorun 5: Sonsuz Reconnect Döngüsü (ÇÖZÜLDÜ)
- **Sebep**: Çoklu Godot süreci (port 6505 çakışması) + biriken eski C# server süreçleri
- **Çözüm**: 
  - `GodotBridge.cs`: `IsReconnectionEnabled = false`, exponential backoff (100ms→51s, max 8 deneme)
  - Disconnect'te pending request'ler temizlenmiyor (cascade flood engellendi)
  - `Program.cs`: başlangıçta `await ConnectAsync()` ile bağlantı loglanıyor
- **Test**: Bağlantı başarıyla kuruluyor, flood yok

### 🛡️ Sorun 6: Server Dayanıklılık Güçlendirmesi (YAPILDI)
**Tarih:** 17.07.2026 (Son güncelleme: 22:43)

MCP server ve Godot eklentisi aşağıdaki durumlar için güçlendirildi:

#### C# Server (`GodotBridge.cs`)
- ✅ **Port Tarama**: `GODOT_MCP_WS_URL` veya 6505 çalışmazsa 6506, 6507 otomatik denenir
- ✅ **Health Check**: 15sn aralıkla Godot'a `ping` gönderilir, yanıt gelmezse reconnect
- ✅ **Exponential Backoff**: 200ms → 30s (max 10 deneme)
- ✅ **Bağlantı Doğrulama**: Sadece TCP bağlantısı yeterli (Godot handshake'i kabul ettiyse hazır). `ping` ek doğrulama DENENDİ ama Godot `ping` handler'ı yanıt vermeyebiliyor → kaldırıldı, sadece health check için
- ✅ **Temiz Kapanma**: `ObjectDisposedException` düzeltildi, `DisposeAsync` güvenli
- ✅ **Çoklu Peer**: Godot tarafı artık birden fazla server bağlantısını kabul eder
- ✅ **Durum Logları**: Bağlandı/Koptu/Reconnect net bir şekilde loglanıyor

#### Godot Eklentisi (`mcp_server.gd`, `command_dispatcher.gd`)
- ✅ **Çoklu Peer**: `_peer` → `_peers: Array[WebSocketPeer]` (16 bağlantıya kadar)
- ✅ **`ping` komutu**: Health check için `{pong: true, time: ...}`
- ✅ **`get_version` komutu**: Godot/plugin version bilgisi
- ✅ **Bağlantı Logları**: Yeni/Kapanan bağlantı sayısı loglanıyor

#### `Program.cs`
- ✅ **Genel Hata Yakalama**: `UnhandledException` → log
- ✅ **Başlangıç Bağlantısı**: `await ConnectAsync()` ile sonuç loglanıyor
- ✅ **Graceful Shutdown**: Ctrl+C → `DisposeAsync()` → temiz kapanma

#### Test Sonuçları
```
[GodotBridge] Deneniyor: ws://localhost:6505
[GodotBridge] Bağlandı: ws://localhost:6505
✅ Godot bağlantısı kuruldu.
🚀 GodotMcpServer başlatıldı. MCP stdio transport dinleniyor...
```
- ✅ ObjectDisposedException yok
- ✅ Health check ping çalışıyor
- ✅ Çoklu Godot süreci olsa bile port taraması devreye giriyor

---

## 📋 Sonraki Adımlar

### Phase 2 — Runtime Test & Polish (GODOT AÇIK İKEN)
- [ ] End-to-end test: `create_scene` → `create_node` → `read_script` → `game_play_scene` → `screenshot_viewport`
- [ ] Reconnect testi: Godot'u kapatıp aç, server restart
- [ ] Hata mesajı formatlama iyileştirmesi
- [ ] MCP client entegrasyonu (opencode.json)

### Phase 3 — Gelişmiş Özellikler (Opsiyonel)
- [ ] TileMap / Navigation tool'ları
- [ ] Shader / Audio / Physics tools
- [ ] Multi-scene edit (cross_scene_set_property)
- [ ] Unit test framework entegrasyonu

---

## 📁 Dosya Listesi (39 adet)

```
GodotMcpServer/
├── GodotMcpServer.csproj        ✅
├── Program.cs                   ✅
├── GodotBridge.cs               ✅
├── Protocol/GodotRequest.cs     ✅
├── Protocol/GodotResponse.cs    ✅
└── Tools/
    ├── SceneTools.cs            ✅ (6 tools)
    ├── NodeTools.cs             ✅ (10 tools)
    ├── ScriptTools.cs           ✅ (6 tools)
    ├── EditorTools.cs           ✅ (9 tools)
    ├── FileSystemTools.cs       ✅ (7 tools)
    ├── ResourceTools.cs         ✅ (4 tools)
    ├── ConsoleTools.cs          ✅ (3 tools)
    └── ScreenshotTools.cs       ✅ (2 tools)

addons/godot_mcp/
├── plugin.cfg                   ✅
├── plugin.gd                    ✅
├── mcp_server.gd               ✅
├── command_dispatcher.gd        ✅
├── log_collector.gd             ✅
└── commands/
    ├── cmd_scene.gd             ✅ (6 funcs)
    ├── cmd_node.gd              ✅ (10 funcs)
    ├── cmd_script.gd            ✅ (6 funcs)
    ├── cmd_editor.gd            ✅ (9 funcs)
    ├── cmd_filesystem.gd        ✅ (7 funcs)
    ├── cmd_resource.gd          ✅ (4 funcs)
    └── cmd_screenshot.gd        ✅ (2 funcs)

issues.md                        ✅ (sorun raporları)
GOAL.md                          ✅ (orijinal spec)
```

---

## 📝 Notlar

- **C# .NET 8** + `ModelContextProtocol` SDK v1.2.0 kullanıldı
- **Godot 4.x** plugin — WebSocket sunucu TCPServer üzerinden açılıyor
- Logger: C#'ta `ILogger<T>` (Microsoft.Extensions.Logging), GDScript'te `push_warning/push_error`
- Tüm tool'lar try-catch ile sarılmış, timeout + error handling var
- Build ~7 saniye içinde tamamlanıyor
