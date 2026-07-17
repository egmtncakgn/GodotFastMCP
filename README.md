# GodotFastMCP

**Godot 4 editorünü AI asistanlarına (MCP uyumlu istemcilere) bağlayan, tamamen C# ile yazılmış MCP (Model Context Protocol) köprüsü.**

Bridge AI assistants to the Godot 4 editor via the Model Context Protocol — 52 tools for scenes, nodes, scripts, resources, filesystem, console logs, screenshots and live editor control.

```
┌──────────────────┐   stdio / JSON-RPC    ┌────────────────────┐   WebSocket    ┌──────────────────────┐
│  MCP Client      │ ◄──────────────────►  │  GodotMcpServer    │ ◄────────────► │  Godot 4 Editor      │
│  (opencode,      │                       │  (.NET 8, C#)      │  127.0.0.1     │  (addons/godot_mcp   │
│   Claude, Cursor)│                       │  52 MCP tools      │  46300-46400   │   EditorPlugin)      │
└──────────────────┘                       └────────────────────┘                └──────────────────────┘
```

---

## 🚀 Hızlı Kurulum (Son Kullanıcı)

### 1) Server'ı indirin

[Releases](https://github.com/egmtncakgn/GodotFastMCP/releases) sayfasından platformunuza uygun paketi indirin (.NET kurulu olması **gerekmez**, self-contained):

| Platform | Paket |
|---|---|
| Windows x64 | `GodotMcpServer-win-x64.zip` |
| Linux x64 / arm64 | `GodotMcpServer-linux-x64.zip` / `GodotMcpServer-linux-arm64.zip` |
| macOS Intel / Apple Silicon | `GodotMcpServer-osx-x64.zip` / `GodotMcpServer-osx-arm64.zip` |

Zip'i bir yere açın (örn. `C:\Tools\GodotMCP\`).

### 2) MCP istemcinizi yapılandırın

**opencode** (`opencode.json`):

```json
{
  "mcp": {
    "godot": {
      "type": "local",
      "command": ["C:\\Tools\\GodotMCP\\GodotMcpServer.exe"],
      "enabled": true
    }
  }
}
```

**Claude Desktop** (`claude_desktop_config.json`):

```json
{
  "mcpServers": {
    "godot": {
      "command": "C:\\Tools\\GodotMCP\\GodotMcpServer.exe"
    }
  }
}
```

### 3) Godot eklentisini kurun (ilk sefer, bir kez)

İndirdiğiniz paketteki `addons/godot_mcp` klasörünü Godot projenizin `addons/` dizinine kopyalayın ve
**Project → Project Settings → Plugins**'ten **GodotMCP**'yi etkinleştirin.

> **Bundan sonrası otomatik:** Server her bağlandığında eklenti versiyonunu kontrol eder,
> eskiyse **canlı olarak günceller** (dosyalar diske yazılır + reimport). Manuel kopyalama bir kez yapılır.

### 4) Kullanın

Godot editörü açıkken AI asistanınıza yazın:

- *"res://scenes/Main.tscn için yeni bir sahne oluştur, root CharacterBody2D olsun"*
- *"Player node'unun tüm property'lerini göster"*
- *"res://scripts/player.gd dosyasını oku ve double jump ekle"*
- *"Oyunu çalıştır, ekran görüntüsü al"*
- *"Son 20 editor log satırını göster"*

---

## 🔄 Güncelleme Sistemi

İki bağımsız, otomatik güncelleme kanalı vardır:

### Addon (Godot eklentisi) güncelleme
Server, Godot'a **her bağlandığında** eklenti versiyonunu kontrol eder (`get_version`).
Server'ın gömülü addon'u daha yeniyse dosyaları WebSocket üzerinden canlı push'lar —
Godot açıkken bile güncellenir. `plugin.gd`/`plugin.cfg` değiştiyse editor restart'ı istenir.

İlgili tool'lar: `update_addon_push`, `sync_addon`

### Server (kendini) güncelleme
Server, açılışta arka planda GitHub Releases'i kontrol eder ve yeni sürüm varsa log'lar.
MCP tool'ları üzerinden de yönetilebilir:

| Tool | Açıklama |
|---|---|
| `server_version` | Çalışan sürüm, platform, kurulum dizini |
| `server_check_update` | Yeni sürüm var mı? Sürüm notlarıyla bildirir |
| `server_self_update` | Yeni sürümü indirir, uygular, server'ı yeni sürümle yeniden başlatır |

Self-update akışı: zip indirilir → staging'e açılır → ayrık bir watcher script server kapandıktan
sonra dosyaları değiştirir → bir sonraki MCP oturumunda yeni sürüm çalışır.

---

## 🛠️ Tool Kategorileri (52 tool)

| Kategori | Tool'lar |
|---|---|
| **Sahne** | `scene_open`, `scene_save`, `scene_create`, `scene_list_opened`, `scene_get_data`, `scene_close` |
| **Node** | `node_find`, `node_create`, `node_get/set_property(ies)`, `node_delete`, `node_reparent`, `node_duplicate`, `node_rename`, `node_instance_scene` |
| **Script** | `script_read`, `script_create`, `script_update`, `script_delete`, `script_attach_to_node`, `script_get_errors` |
| **Editor** | `editor_play/stop/pause`, `editor_get_state`, `editor_selection_get/set`, `editor_get/set_project_setting(s)`, `editor_get_project_path` |
| **Dosya Sistemi** | `filesystem_list`, `filesystem_read_file`, `filesystem_write_file`, `filesystem_delete`, `filesystem_move`, `filesystem_reimport`, `filesystem_search` |
| **Resource** | `resource_get_data`, `resource_modify`, `resource_create`, `resource_delete` |
| **Konsol** | `console_get_logs`, `console_get_errors`, `console_clear_logs` |
| **Ekran Görüntüsü** | `screenshot_viewport`, `screenshot_game` |
| **Güncelleme** | `update_addon_push`, `sync_addon`, `server_version`, `server_check_update`, `server_self_update` |

---

## 🧩 Mimari Notlar (Dayanıklılık)

- **Dinamik port koordinasyonu:** Eklenti 46300-46400 aralığında çakışmasız port seçer, lock dosyasına yazar; server aynı dosyadan okur. Birden fazla Godot projesi açıkken bile çakışma olmaz.
- **Otomatik yeniden bağlanma:** Exponential backoff (200ms → 30s) + 15s health-check ping.
- **Tek instance garantisi:** Global mutex — aynı makinede iki server çakışmaz.
- **Godot kapalıyken:** Server istemciyi kilitlemez; arka planda bağlanmaya devam eder, tool'lar anlamlı hata mesajı döner.
- **Bağlantı doğrulama:** WebSocket handshake sonrası `ping/pong` ile gerçek Godot eklentisi doğrulanır (yanlış servise bağlanmaz).

---

## 👨‍💻 Kaynak Koddan Çalıştırma (Geliştirici)

```bash
git clone https://github.com/egmtncakgn/GodotFastMCP.git
cd GodotFastMCP
dotnet publish GodotMcpServer.csproj -c Release -o publish
```

MCP istemcinizde `publish/GodotMcpServer.exe` (Unix'te `publish/GodotMcpServer`) yolunu kullanın.

> Geliştirici ortamında (`.git` tespit edilirse) `server_self_update` devre dışıdır — `git pull` kullanın.

### Yeni sürüm yayınlama (maintainer)

1. `GodotMcpServer.csproj` içindeki `<Version>` değerini artırın
2. `addons/godot_mcp/plugin.cfg` içindeki `version` değerini eşitleyin
3. Tag push'layın:

```bash
git tag v1.3.0 && git push origin v1.3.0
```

Release workflow'u 5 platform için paket üretir ve GitHub Release oluşturur.

---

## Gereksinimler

- **Godot 4.3+** (`WebSocketPeer.accept_stream` API'si için)
- Son kullanıcılar için .NET gerekmez (self-contained paketler); kaynaktan derlemek için **.NET 8 SDK**

## Lisans

[MIT](LICENSE)
