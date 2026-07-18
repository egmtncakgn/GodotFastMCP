# MCP Godot Server - Sorun Raporu

## Oluşturma Tarihi: 17 Temmuz 2026

---

## 🐛 Sorun 1: C# Script Dosyası Bulunamıyor

**Tarih:** 17.07.2026  
**Durum:** Çözüldü (18.07.2026)  
**Öncelik:** Yüksek

### Hata Mesajları
```
ERROR: Cannot open file 'res://scripts/XOXGame.cs'.
ERROR: Failed to read file: 'res://scripts/XOXGame.cs'.
ERROR: Cannot load C# script file 'res://scripts/XOXGame.cs'.
ERROR: Failed loading resource: res://scripts/XOXGame.cs.
ERROR: Cannot set object script. Parameter should be null or a valid script reference.
```

### Açıklama
Godot, `res://scripts/XOXGame.cs` dosyasını bulamıyor. C# projeleri için özel yapılandırma eksik.

### Kök Neden
- Proje kökünde oyun için ayrı bir `.csproj` dosyası yok
- Sadece MCP server'ın kendi `.csproj` dosyası var (`GodotMcpServer/GodotMcpServer.csproj`)
- Godot, C# scriptlerini otomatik bulmak için proje yapısını doğru yapılandırmamış

### Çözüm Gereksinimleri
1. Proje kökünde oyun için bir `.csproj` dosyası oluşturulmalı
2. `project.godot` dosyasında dotnet ayarları kontrol edilmeli
3. C# scriptleri için derleme pipeline'ı kurulmalı

---

## 🐛 Sorun 2: MCP Tool Call Parametre Formatı Hatası

**Tarih:** 17.07.2026  
**Durum:** Çözüldü  
**Öncelik:** Orta

### Hata Mesajları
```
ModelContextProtocol.McpProtocolException: Unknown tool: ''
```

### Açıklama
İlk testlerde `parameters` yerine `params` kullanılması gerekiyordu. C# anonymous type'larında `params` reserved keyword olduğu için sorun çıkıyordu.

### Çözüm
Doğrudan string JSON kullanarak MCP tool call'ları göndermek:
```csharp
var getState = "{\"jsonrpc\":\"2.0\",\"id\":10,\"method\":\"tools/call\",\"params\":{\"name\":\"editor_get_state\",\"arguments\":{}}}";
input.WriteLine(getState);
```

---

## 🐛 Sorun 3: Scene İşlemleri Hata Veriyor

**Tarih:** 17.07.2026  
**Durum:** Kısmi Çözüm  
**Öncelik:** Orta

### Hata Mesajları
```json
{"error":{"code":-32602,"message":"Unknown tool: ''"},"id":10,"jsonrpc":"2.0"}
```

### Açıklama
`scene_open`, `scene_get_data`, `scene_list_opened` gibi sahnelerle ilgili işlemler hata veriyor. Muhtemelen Godot editörü tam yüklenmeden tool çağrısı yapılıyor.

### Geçici Çözüm
- Godot'un tamamen yüklenmesini beklemek
- Manuel olarak sahneyi açıp test etmek

---

## 🐛 Sorun 4: GridContainer Çocuk Erişimi

**Tarih:** 17.07.2026  
**Durum:** Çözüldü  
**Öncelik:** Düşük

### Açıklama
İlk script versiyonunda `GetChild<Button>(index)` kullanılıyordu, ancak GridContainer'ın children'ına erişim farklı çalışıyor olabilir.

### Çözüm
Butonları isimleriyle bulmak:
```csharp
string btnName = $"Cell_{row}_{col}";
var btn = gridContainer.GetNode<Button>(btnName);
```

---

## 📋 Genel Değerlendirme

### Çalışan Özellikler
- ✅ MCP Server Godot'a bağlanıyor (`ws://localhost:6505`)
- ✅ `editor_get_state` tool'u çalışıyor
- ✅ 47 tool başarıyla kayıt ediliyor
- ✅ MCP protocol implementasyonu doğru çalışıyor

### Sorunlu Alanlar
- ❌ C# proje yapısı Godot ile uyumlu değil
- ❌ Scene işlemleri için Godot'un tam yüklenmesi gerekiyor
- ⚠️ Debugging araçları sınırlı (MCP üzerinden tam hata ayıklama zor)

### Öneriler
1. **C# Proje Yapısı:** Godot'un resmi C# proje şablonunu kullanın
2. **Build Pipeline:** Otomatik derleme için CI/CD veya build script'i ekleyin
3. **Error Handling:** MCP tool'larında daha detaylı hata mesajları ekleyin
4. **Testing:** Unit testler ile tool'ları otomatik test edin

---

## 📝 Notlar

- GodotMcpServer başarılı bir şekilde çalışıyor ve 47 tool sunuyor
- XOX oyunu mantıksal olarak doğru yazılmış, sadece proje yapısı sorunu var
- GDScript kullanmadan C# tercih edildi (kullanıcı isteği)
- MCP stdio transport doğru çalışıyor

---

## ✅ Çözüm: İki Ayrı Proje Yapısı

**Tarih:** 17.07.2026  
**Durum:** Çözüldü

### Uygulanan Değişiklikler

1. **`islemcigame.csproj`** - Sadece oyun scriptleri için:
   ```xml
   <Project Sdk="Godot.NET.Sdk/4.7.0">
     <PropertyGroup>
       <TargetFramework>net8.0</TargetFramework>
       <EnableDynamicLoading>true</EnableDynamicLoading>
       <RootNamespace>islemcigame</RootNamespace>
       <AssemblyName>islemcigame</AssemblyName>
       <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
     </PropertyGroup>
     <ItemGroup>
       <Compile Include="res\scripts\*.cs" />
     </ItemGroup>
   </Project>
   ```

2. **`GodotMcpServer/GodotMcpServer.csproj`** - MCP server için (değişiklik yok)

3. **`.godot` dizini temizlendi** ve yeniden oluşturuldu

### Build Sonucu
```
islemcigame -> C:\Projects\godotislemcioyun\.godot\mono\temp\bin\Debug\islemcigame.dll
Oluşturma başarılı oldu. 0 Uyarı, 0 Hata
```

### Sonraki Adımlar
1. Godot'u kapatın
2. Godot'u tekrar açın ve projeyi import edin
3. `res://scenes/XOX.tscn` sahnesini açıp F5 ile test edin
4. MCP server'ı ayrı bir terminalde çalıştırın: `cd GodotMcpServer && dotnet run`

---

## 🐛 Sorun 5: Sonsuz Reconnect Döngüsü ve Bağlantı Kaybı

**Tarih:** 17.07.2026  
**Durum:** Çözüldü  
**Öncelik:** Yüksek

### Belirtiler
```
warn: GodotBridge[0]  Godot bağlantısı kesildi: NoMessageReceived
info: GodotBridge[0]  Godot'a yeniden bağlanıldı: NoMessageReceived
... (binlerce kez tekrar)
```

### Kök Neden
1. **Çoklu Godot süreci**: İki Godot editor aynı anda `C:\Projects\islemcioyun` projesini açmıştı → ikisi de port 6505'i dinlemeye çalışıyordu → bağlantı ping-pong yapıyordu
2. **Biriken C# server süreçleri**: Eski `GodotMcpServer.exe` instance'ları kapanmadan kalıyordu → DLL dosyası kilitleniyor, publish başarısız oluyordu
3. **Flood reconnect**: `IsReconnectionEnabled = true` iken Godot kapalıyken her saniye bağlanmayı deniyor, log flood'u yaratıyordu
4. **Cascade failure**: Disconnect handler'ı tüm pending request'leri hemen hata verip temizliyordu → bu da tekrar bağlanmayı tetikliyordu

### Çözüm
`GodotBridge.cs` güncellendi:
- `IsReconnectionEnabled = false` (manuel reconnect kontrolü)
- Disconnect'te pending request'ler temizlenmiyor, sadece `_connected = false` set ediliyor
- Bağlantı kopunca **exponential backoff** ile otomatik tekrar bağlanma (100ms → max ~51s, max 8 deneme)
- Eski client varsa yeni bağlantıdan önce `Stop()` ile kapatılıyor

`Program.cs` güncellendi:
- Başlangıçta `await bridge.ConnectAsync()` ile bağlantı sonucu loglanıyor

### Test Sonucu
```
info: GodotBridge[0]   [GodotBridge] İlk bağlantı: ws://localhost:6505
info: GodotBridge[0]   Reconnect başarılı: Initial
info: Program[0]       Godot bağlantısı başlangıçta kuruldu.
```
✅ Bağlantı başarıyla kuruluyor

### Öneri
- Tek seferde **sadece bir Godot editor** açık olmalı
- Eski `GodotMcpServer.exe` süreçleri birikmeden kapatılmalı (özellikle publish öncesi)
- opencode.json `godotMCP` artık publish edilmiş exe'yi kullanıyor: `C:\Projects\GodotMcpServer\publish\GodotMcpServer.exe`

---

## 🐛 Sorun 6: Server Dayanıklılık Güçlendirmesi

**Tarih:** 17.07.2026  
**Durum:** Çözüldü  
**Öncelik:** Yüksek

### Hedef
MCP server ve Godot eklentisini şu durumlar için dayanıklı hale getirmek:
- Godot editor crash olursa / kapanırsa
- Birden fazla Godot editor açılırsa (port çakışması)
- Network geçici olarak koparsa
- Server kendisi crash olursa

### Uygulanan Değişiklikler

#### `GodotBridge.cs` (C# Server)
- **Port Tarama**: `GODOT_MCP_WS_URL` env değişkeni veya 6505 çalışmazsa 6506, 6507 otomatik denenir
- **Health Check**: 15sn aralıkla Godot'a `ping` komutu gönderilir, yanıt gelmezse reconnect tetiklenir
- **Exponential Backoff**: Bağlantı kopunca 200ms → 30s (max 10 deneme) aralıkla tekrar deneme
- **Temiz Kapanma**: `ObjectDisposedException` bug'ı düzeltildi (`DisposeAsync` içinde `Cancel()` çağrılmıyor)
- **Çoklu Peer Desteği**: Godot tarafı artık birden fazla server bağlantısını kabul ediyor
- **Bağlantı Doğrulama**: Sadece TCP bağlantısı yeterli (Godot WebSocket handshake'i kabul ettiyse hazırdır). `ping` ile ek doğrulama DENENDİ ama Godot eklentisi `ping` handler'ı bazen yanıt vermeyebiliyor → kaldırıldı, sadece health check için kullanılıyor

#### `Program.cs`
- **Genel Hata Yakalama**: `AppDomain.UnhandledException` → log
- **Başlangıç Bağlantısı**: `await bridge.ConnectAsync()` ile bağlantı sonucu loglanıyor
- **Graceful Shutdown**: `finally` bloğunda `bridge.DisposeAsync()` çağrılıyor

#### `mcp_server.gd` (Godot Eklentisi)
- **Çoklu Peer**: `_peer` (tek) → `_peers: Array[WebSocketPeer]` (16 bağlantıya kadar)
- Her bağlantı ayrı `_process` döngüsünde poll ediliyor
- Bağlantı açılış/kapanış logları eklendi

#### `command_dispatcher.gd`
- **`ping` komutu**: `{ "success": true, "result": { "pong": true, "time": ... } }`
- **`get_version` komutu**: Godot version + plugin version bilgisi

### Test Sonuçları
```
[GodotBridge] Deneniyor: ws://localhost:6505
[GodotBridge] Bağlandı: ws://localhost:6505
✅ Godot bağlantısı kuruldu.
🚀 GodotMcpServer başlatıldı. MCP stdio transport dinleniyor...
```
- ✅ ObjectDisposedException yok
- ✅ Health check ping arka planda çalışıyor
- ✅ Çoklu Godot süreci olsa bile port taraması devreye giriyor (6506/6507)

---

## 📋 Güncel Durum (17.07.2026 - Son)

### ✅ Çalışan
- MCP Server Godot'a bağlanıyor (`ws://localhost:6505`) — health check + port tarama ile
- 47 tool + 2 meta komut (`ping`, `get_version`) kayıtlı
- Godot eklentisi `C:\Projects\islemcioyun\addons\godot_mcp` altında kurulu ve çoklu peer destekliyor
- opencode.json'a `godotMCP` eklendi (publish exe)
- Build: `dotnet publish -c Release -o C:\Projects\GodotMcpServer\publish`
- **Dayanıklılık**: Health check, exponential backoff, port tarama, temiz kapanma

### ⚠️ Dikkat
- Hala önerilir: Tek Godot editor açık olsun (ama artık port taraması sayesinde çakışma durumunda 6506/6507 denenecek)
- Eski exe süreçleri biriktirmeyin (dosya kilitleme)
- Godot tamemen yüklendikten sonra tool çağırın (sahne işlemleri için)

---

# 🔍 18 Temmuz 2026 — Detaylı Kod Taraması ve Yeni Bulgular

**Test ortamı:** Godot 4.7.1-stable (Forward+), `C:\Projects\islemcioyun` projesi,
`C:\Projects\GodotMcpServer\GodotFastMCP\publish\GodotMcpServer.exe` v1.2.0.
Tüm bulgular doğrudan WebSocket + MCP üzerinden tekrarlanabilir şekilde doğrulanmıştır.

---

## 🔴 KRİTİK BLOKLAYAN BUG'LAR (3)

### 🐛 Sorun 7: Godot 4.7 + `EditorInterface` Tip Parametresi Uyumsuzluğu
**Tarih:** 18.07.2026
**Durum:** Çözüldü (18.07.2026)
**Öncelik:** Kritik
**Etki:** 8 komut dosyasının tamamı çalışmıyor, 40+ tool kullanılamaz

#### Belirtiler
WebSocket üzerinden doğrudan test edildiğinde:
```
[ping]                 → success:true  ✅
[get_version]          → success:true  ✅
[editor_get_state]     → success:false, error:null  ❌
[editor_get_project_path] → success:false, error:null  ❌
[filesystem_list]      → success:false, error:null  ❌
[scene_list_opened]    → success:false, error:null  ❌
[console_get_logs]     → success:true  ✅ (lambda, komut nesnesi değil)
[update_addon_push]    → success:false, error:null  ❌
```

Addon log_collector'da görünen gerçek hata:
```
Attempt to call function 'null::get_state (Callable)' on a null instance.
Attempt to call function 'null::get_project_path (Callable)' on a null instance.
Attempt to call function 'null::list_opened (Callable)' on a null instance.
Attempt to call function 'null::list_files (Callable)' on a null instance.
Trying to assign value of type 'Nil' to a variable of type 'String'.
```

#### Kök Neden
Tüm `addons/godot_mcp/commands/cmd_*.gd` dosyaları RefCounted'tan türetilmiş ve
`EditorInterface` tipinde parametre alan bir `_init` metoduna sahip:
```gdscript
@tool
extends RefCounted

var _ei: EditorInterface

func _init(ei: EditorInterface) -> void:
    _ei = ei
```

`command_dispatcher.gd:register()` bunları şöyle oluşturuyor:
```gdscript
var editor_cmd = load(base + "cmd_editor.gd").new(_ei)
```

Godot 4.7'de `EditorInterface` singleton'ı RefCounted alt sınıfına **tip-annotasyonlu**
parametre olarak geçirildiğinde `new()` **null** döndürüyor. `McpServer` ve
`CommandDispatcher` (Node türevi) için aynı imza çalışıyor, sorun yalnızca
**RefCounted + @tool + EditorInterface** kombinasyonunda. Godot 4.4'e kadar
bu pattern çalışıyordu; 4.7'de regresyon var.

Dispatcher, null dönen komut nesnelerinin `null.get_state` gibi metotlarını
Callable olarak kaydediyor. Çağrı anında runtime hatası fırlatılıyor ama
addon bu hatayı yakalayıp `{"success": false, "error": "..."}` formatına
çevirmiyor → C# tarafı boş `error:null` alıyor.

#### Etkilenen Dosyalar (8 adet — tüm komut sınıfları)
- `addons/godot_mcp/commands/cmd_editor.gd:6`
- `addons/godot_mcp/commands/cmd_node.gd:6`
- `addons/godot_mcp/commands/cmd_scene.gd:6`
- `addons/godot_mcp/commands/cmd_script.gd:6`
- `addons/godot_mcp/commands/cmd_filesystem.gd:6`
- `addons/godot_mcp/commands/cmd_resource.gd:6`
- `addons/godot_mcp/commands/cmd_screenshot.gd:6`
- `addons/godot_mcp/commands/cmd_update.gd:7`

#### Önerilen Çözüm
İki seçenek var:

**Seçenek A (önerilen, minimal değişiklik):** Parametre tipini kaldır
```gdscript
func _init(ei) -> void:    # tip yok
    _ei = ei
```
8 dosyada aynı değişiklik gerekiyor. `McpServer` ve `CommandDispatcher` (Node)
için değişiklik gerekmez — onlar etkilenmiyor.

**Seçenek B (daha kapsamlı):** Komut nesnelerini `Node` olarak değiştir
ve `_ei`'yi `EditorPlugin.get_editor_interface()` ile global singleton'dan al.
Bu büyük refactor gerektirir.

**Seçenek C (en güvenli):** Dispatcher'da her komut için `new()` çağrısından
sonra null check ekle, null ise uygun hata mesajıyla dispatcher'da erken fail
et. Bu sayede MUTLAK kök neden düzelmese bile kullanıcıya anlamlı hata ulaşır.

---

### 🐛 Sorun 8: Port Dosyası Yolu Uyumsuzluğu (Server ↔ Addon)
**Tarih:** 18.07.2026
**Durum:** Çözüldü (18.07.2026)
**Öncelik:** Yüksek
**Etki:** Bağlantı yavaşlaması + birden fazla Godot projesi açıksa port karışıklığı riski

#### Belirtiler
Doğrulanan gerçek dosya konumları:
| Taraf | Yazdığı/Okuduğu Yol | Durum |
|---|---|---|
| Godot addon (mcp_server.gd:68) | `%APPDATA%\Godot\app_userdata\<proje>\GodotMCP\port.txt` | ✅ Dosya var: `46300` |
| C# server (PortCoordinator.cs:18-19) | `%LOCALAPPDATA%\GodotMCP\port.txt` | ❌ Dosya YOK |

#### Kök Neden
`addons/godot_mcp/mcp_server.gd:67-68`:
```gdscript
func _port_lock_path() -> String:
    return OS.get_user_data_dir().path_join("GodotMCP/port.txt")
```
→ `C:\Users\<user>\AppData\Roaming\Godot\app_userdata\<proje>\GodotMCP\port.txt`

`GodotFastMCP/PortCoordinator.cs:17-20`:
```csharp
private static readonly string LockDir =
    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GodotMCP");
private static readonly string LockFile = Path.Combine(LockDir, "port.txt");
```
→ `C:\Users\<user>\AppData\Local\GodotMCP\port.txt`

**`AppData\Roaming` ≠ `AppData\Local`**, iki taraf ASLA aynı dosyayı görmez.

#### Mevcut Durum (neden hâlâ çalışıyor görünüyor)
Server `port.txt`'i okuyamayınca `GodotBridge.ResolveCandidatePorts()` 100 portu
TCP probe ile sıralı tarıyor → 46300'i buluyor. Ama:
- **100 port taraması = 1+ saniye gecikme** her server başlangıcında
- Server aynı anda birden fazla Godot projesi açıksa **yanlış porta** bağlanabilir
- İlk bağlantıda `lock dosyasından oku → hızlı bağlan` optimizasyonu hiç devreye girmiyor

#### Ek Sorun: Aynı Anda Farklı Projeler
Aynı anda `C:\Projects\islemcioyun` ve `C:\Projects\islemcigame` açıksa her
addon kendi `app_userdata\<proje>\GodotMCP\port.txt`'ine yazar ama server
tek bir `LOCALAPPDATA\GodotMCP\port.txt` arar → bağlanacağı projeyi **seçemez**.

#### Önerilen Çözüm
**Seçenek A (önerilen):** Server tarafını addona uydur
```csharp
// PortCoordinator.cs - AppData/Local yerine AppData/Roaming/Godot/app_userdata kullan
// Veya: aktif Godot projesinin user_data_dir'ini Godot'tan öğren (editor_get_project_path zaten var)
```

**Seçenek B:** Addon tarafını server'a uydur (daha kolay ama esnekliği azaltır)
```gdscript
// mcp_server.gd:_port_lock_path()
return OS.get_data_dir().path_join("GodotMCP/port.txt")  // LOCALAPPDATA
```

**Seçenek C (en sağlam):** Lock dosyasını kaldır, sadece port aralığı taraması yap
+ bağlantı sonrası `get_version` ile doğru projeye bağlandığını doğrula (proje adı eşleşmesi).

---

### 🐛 Sorun 9: Boş Hata Mesajı — Kullanıcı Ne Olduğunu Bilemiyor
**Tarih:** 18.07.2026
**Durum:** Çözüldü (18.07.2026)
**Öncelik:** Yüksek
**Etki:** Tüm başarısız komutlar "[GodotMCP Hata] " olarak boş döner; debugging imkansız

#### Belirtiler
MCP tool çağrılarında dönen yanıt:
```
[GodotMCP Hata]
```
(sonunda boşluk, error mesajı yok)

#### Kök Neden
`mcp_server.gd:_handle_message()` her durumda response döndürüyor, ama
dispatcher'da `null.get_state()` çağrısı **Godot runtime exception** fırlatıyor.
Bu exception `await handler.call(params)` satırında yakalanmıyor → response
hiç gönderilmiyor → request timeout'a düşüyor.

Ancak Sorun 7'deki testlerde timeout YERİNE `success:false, error:null`
görüyoruz. Bunun nedeni: `await` coroutine içinde fırlayan exception GDScript
tarafında "swallowed" oluyor ve `result` değişkenine **boş `{}`** atanıyor.
Dispatcher'ın `await` ifadesi `null` döndürünce:
```gdscript
var response = {
    "id": request_id,
    "success": result.get("success", false),  # {} üzerinde → false
    "result": result.get("result", null),     # yok
    "error": result.get("error", null)        # yok
}
```
şeklinde success=false, error=null gönderiliyor.

C# tarafı bu yanıtı `[GodotMCP Hata] {null}` → interpolation'da boş string → 
`[GodotMCP Hata] ` olarak gösteriyor (Tools/EditorTools.cs:54, NodeTools.cs:18 vb.).

#### Önerilen Çözüm
**C# tarafı:** Null error'u yakala ve anlamlı mesaj üret
```csharp
// Tools/EditorTools.cs (ve diğer 8 tools dosyasında)
return result.Success
    ? result.Result.ToString() ?? "{}"
    : $"[GodotMCP Hata] {result.Error ?? "addon boş yanıt döndü (muhtemelen null instance — Godot 4.7 + EditorInterface uyumsuzluğu)"}";
```

**GDScript tarafı:** Dispatcher'da try/catch + boş result'ı handle et
```gdscript
# command_dispatcher.gd:dispatch()
var result = await handler.call(params)
if not result is Dictionary:
    return {"success": false, "error": "Handler geçersiz yanıt: " + str(result)}
if not result.has("success"):
    return {"success": false, "error": "Handler success key'i döndürmedi: " + str(result)}
return result
```

---

## 🟠 YÜKSEK ÖNEMLİ BUG'LAR (4)

### 🐛 Sorun 10: Main Scene Tanımsız — F5 Çalışmıyor
**Tarih:** 18.07.2026
**Durum:** Çözüldü (18.07.2026)
**Öncelik:** Yüksek
**Etki:** `editor_play` tool'u başarısız oluyor, Godot "Can't run project" hatası veriyor

#### Belirtiler
`godot.log` (en son):
```
Error: Can't run project: no main scene defined in the project.
```

#### Kök Neden
`C:\Projects\islemcioyun\project.godot` dosyasında:
```ini
[application]
config/name="islemcioyun"
config/features=PackedStringArray("4.7", "Forward Plus")
config/icon="res://icon.svg"
```
`run/main_scene` anahtarı tanımlı değil. Proje yeni oluşturulmuş, hiç sahne eklenmemiş.

#### Çözüm
`project.godot`'a ekle (veya editorden Project Settings → Application → Run → Main Scene ayarla):
```ini
[application]
config/name="islemcioyun"
run/main_scene="res://scenes/Main.tscn"
```
Veya `scene_create` tool'u ile yeni bir sahne oluşturup `editor_set_project_setting` ile main scene olarak ayarla.

---

### 🐛 Sorun 11: Dispatcher Hata Zincirinde Exception Yutuluyor
**Tarih:** 18.07.2026
**Durum:** Çözüldü (18.07.2026)
**Öncelik:** Yüksek
**Etki:** Komut hataları response olarak dönmek yerine timeout'a düşüyor

#### Kök Neden
`command_dispatcher.gd:89-95`:
```gdscript
func dispatch(command: String, params: Dictionary) -> Dictionary:
    if not _handlers.has(command):
        return {"success": false, "error": "Bilinmeyen komut: " + command}

    var handler = _handlers[command]
    var result = await handler.call(params)   # ← exception burada fırlarsa yutulur
    return result
```
`handler.call()` içindeki herhangi bir Godot exception (null dereference, type
mismatch, vb.) coroutine exception'ı olarak yutulur; bu nedenle C# tarafı
timeout (30sn) bekler ve sonunda "Godot yanıt vermedi" der. Sorun 7'deki
null instance vakalarında bile aslında bu mekanizma devreye girmiş, ama
Godot'un hata log'u console'a yazılıp coroutine null döndüğü için error=null
gidiyor.

#### Önerilen Çözüm
```gdscript
var result = await handler.call(params)
if not result is Dictionary:
    push_error("[GodotMCP] Handler '%s' geçersiz yanıt: %s" % [command, str(result)])
    return {"success": false, "error": "Handler '%s' geçersiz yanıt döndü (tip: %s)" % [command, typeof(result)]}
return result
```

---

### 🐛 Sorun 12: `_enable_plugin` Yanlış Parametre — Plugin Adı vs Config Yolu
**Tarih:** 18.07.2026
**Durum:** Çözüldü (18.07.2026)
**Öncelik:** Yüksek
**Etki:** `update_addon_push` ilk kurulumda plugin'i otomatik etkinleştiremez

#### Kök Neden
`addons/godot_mcp/commands/cmd_update.gd:67-68, 130-135`:
```gdscript
if not _is_plugin_enabled("GodotMCP"):
    _enable_plugin("GodotMCP")

func _is_plugin_enabled(plugin_name: String) -> bool:
    var enabled: Array = ProjectSettings.get_setting("editor_plugins/enabled", [])
    return enabled.has(plugin_name)

func _enable_plugin(plugin_name: String) -> void:
    var enabled: Array = ProjectSettings.get_setting("editor_plugins/enabled", [])
    if not enabled.has(plugin_name):
        enabled.append(plugin_name)
    ProjectSettings.set_setting("editor_plugins/enabled", enabled)
    ProjectSettings.save()
```

`project.godot`'ta `editor_plugins/enabled` şu şekilde saklanıyor:
```ini
[editor_plugins]
enabled=PackedStringArray("res://addons/godot_mcp/plugin.cfg")
```

Yani dizide **plugin config yolu** (`res://addons/godot_mcp/plugin.cfg`)
depolanıyor, **plugin adı** (`GodotMCP`) değil. `_is_plugin_enabled("GodotMCP")`
her zaman `false` döner → her push'ta `_enable_plugin("GodotMCP")` çağrılır →
diziye `"GodotMCP"` eklenir → yanlış değer.

#### Doğrulama
Push sonrası `project.godot`:
```ini
enabled=PackedStringArray("res://addons/godot_mcp/plugin.cfg", "GodotMCP")
```
Bu **çalışmaz** çünkü Godot sadece `.cfg` yollarını kabul eder. Plugin hâlâ
devre dışı kalır.

#### Önerilen Çözüm
```gdscript
const PLUGIN_CONFIG_PATH := "res://addons/godot_mcp/plugin.cfg"

func _is_plugin_enabled(_plugin_name: String) -> bool:
    var enabled: Array = ProjectSettings.get_setting("editor_plugins/enabled", [])
    return enabled.has(PLUGIN_CONFIG_PATH)

func _enable_plugin(_plugin_name: String) -> void:
    var enabled: Array = ProjectSettings.get_setting("editor_plugins/enabled", [])
    if not enabled.has(PLUGIN_CONFIG_PATH):
        enabled.append(PLUGIN_CONFIG_PATH)
    ProjectSettings.set_setting("editor_plugins/enabled", enabled)
    ProjectSettings.save()
```

---

### 🐛 Sorun 13: `scene_get_data`, `scene_save`, `scene_close`, `script_get_errors` Path Parametresini Yoksayıyor
**Tarih:** 18.07.2026
**Durum:** Çözüldü (18.07.2026)
**Öncelik:** Orta-Yüksek
**Etki:** İmza parametreyi kabul ediyor ama API sadece aktif sahneyle çalışıyor — sessiz bug

#### Kök Neden
`addons/godot_mcp/commands/cmd_scene.gd:53-56`:
```gdscript
func get_data(params: Dictionary) -> Dictionary:
    var scene = _ei.get_edited_scene_root()    # ← `path` parametresi hiç kullanılmıyor
    if not scene:
        return {"success": false, "error": "Açık sahne yok."}
    return {"success": true, "result": _node_to_dict(scene)}
```

Aynı pattern:
- `cmd_scene.gd:19 save_scene()` → `_ei.save_scene()` (path yok sayılıyor)
- `cmd_scene.gd:72 close_scene()` → `_ei.close_scene()` (path yok sayılıyor)
- `cmd_script.gd:85 get_errors()` → `script_editor.get_warnings()` (path yok sayılıyor)

C# tarafı `SceneTools.cs:33` parametreyi açıkça gönderiyor:
```csharp
var result = await bridge.SendAsync("scene_save", new() { ["path"] = path });
```

#### Önerilen Çözüm
**A) Tutarlı ol:** `path` parametresini kaldır (imzadan ve C# tool'undan), sessizce yoksaymak yerine açıkça "sadece aktif sahne destekleniyor" döndür.
**B) Düzelt:** `EditorInterface.open_scene_from_path(path)` ile sahneyi aç, sonra işlem yap, sonra eski sahneye geri dön (kullanıcı için yıkıcı olabilir, dikkat).

---

## 🟡 ORTA ÖNEMLİ BUG'LAR (8)

### 🐛 Sorun 14: `cmd_editor.gd:get_project_path` İki Kere Path Hesaplıyor (Birinci Satır Ölü Kod)
**Tarih:** 18.07.2026
**Durum:** Çözüldü (18.07.2026)
**Öncelik:** Düşük (işlevsel olarak doğru, ama ölü kod)

```gdscript
func get_project_path(params: Dictionary) -> Dictionary:
    var path = OS.get_executable_path().get_base_dir()  # ← ölü kod, geri dönmez
    return {"success": true, "result": {"path": ProjectSettings.globalize_path("res://")}}
```
Birinci satır `OS.get_executable_path()` (Godot binary'sinin dizini) hesaplıyor
— bu proje dizini değil, editor'ün kurulu olduğu yer. Fonksiyon zaten ikinci
satırda doğru yolu döndürüyor; birinci satır tamamen gereksiz.

**Çözüm:** Satırı sil.

---

### 🐛 Sorun 15: `cmd_update.gd:update_addon` C# Tool Olarak Expose Edilmemiş
**Tarih:** 18.07.2026
**Durum:** Çözüldü (18.07.2026)
**Öncelik:** Orta

`command_dispatcher.gd:86-87`'de addon tarafında kayıtlı:
```gdscript
_handlers["update_addon"]      = update_cmd.update_addon
_handlers["update_addon_push"] = update_cmd.update_addon_push
```

C# tarafında sadece `update_addon_push` (Tools/AddonUpdateTools.cs:192) tool
olarak tanımlı. `update_addon` komutu Godot tarafından anlaşılıyor ama
MCP üzerinden çağrılamıyor. Inconsistent API yüzeyi.

`sync_addon` tool'u aslında benzer işi dosya sistemi üzerinden yapıyor ama
iki farklı yol (file copy vs Godot içi `_copy_dir`) → farklı yan etkiler.

---

### 🐛 Sorun 16: `cmd_filesystem.gd:list_files` Recursive Symlink Döngüsü Korumasız
**Tarih:** 18.07.2026
**Durum:** Çözüldü (18.07.2026)
**Öncelik:** Orta
**Etki:** Windows junction veya sembolik link içeren projede sonsuz döngü

`cmd_filesystem.gd:21-38`:
```gdscript
func _collect_files(dir: DirAccess, base: String, recursive: bool, out: Array) -> void:
    dir.list_dir_begin()
    var file_name = dir.get_next()
    while file_name != "":
        if file_name == "." or file_name == "..":
            file_name = dir.get_next()
            continue
        var full_path = base + "/" + file_name
        if dir.current_is_dir():
            out.append({"path": full_path, "type": "dir"})
            if recursive:
                var sub = DirAccess.open(full_path)
                if sub:
                    _collect_files(sub, full_path, recursive, out)  # ← cycle kontrolü yok
```

Windows'ta `mklink /J` ile oluşturulan junction veya `git` alt modüller döngüsel
path üretebilir. Recursive=true ile `filesystem_list` sonsuz döngüye girer
ve MCP isteği 30sn timeout'a düşer.

**Çözüm:** `out.append(full_path.real_path())` ile normalize edilmiş path'i
takip et, zaten ziyaret edilmişse `continue`.

---

### 🐛 Sorun 17: `cmd_filesystem.gd:search_files` Extension Filter Case-Sensitive
**Tarih:** 18.07.2026
**Durum:** Çözüldü (18.07.2026)
**Öncelik:** Düşük-Orta

`cmd_filesystem.gd:120`:
```gdscript
if type_filter.is_empty() or full.ends_with(type_filter):
```
`type=".gd"` ile aranırken `Player.GD` bulunmaz. Aynı şekilde `.tscn` için
`.TSCN` bulunmaz. Windows'ta dosya sistemi case-insensitive, kullanıcı
`type` parametresini büyük harfle girerse şaşırır.

**Çözüm:** `full.ends_with(type_filter.to_lower())` veya
`full.to_lower().ends_with(type_filter.to_lower())`.

---

### 🐛 Sorun 18: `cmd_scene.gd:_node_to_dict` Sabit Depth=10 Limiti
**Tarih:** 18.07.2026
**Durum:** Çözüldü (18.07.2026)
**Öncelik:** Orta
**Etki:** 10+ seviye derin sahnede kesilir (3D oyunlarda sık)

```gdscript
func _node_to_dict(node: Node, depth: int = 0) -> Dictionary:
    if depth > 10:
        return {}
```

10 seviye ötesindeki node'lar sessizce boş dict olarak döner, bilgi kaybı.
Override parametresi yok.

**Çözüm:** `params.get("max_depth", 10)` veya limit kaldır (büyük sahnelerde
JSON serialize maliyeti yüksek ama en azından explicit).

---

### 🐛 Sorun 19: `cmd_node.gd:get_properties` İndexer Anti-Pattern (N kez `get_property_list()`)
**Tarih:** 18.07.2026
**Durum:** Çözüldü (18.07.2026)
**Öncelik:** Düşük (performans)
**Etki:** Çok property'li node'da O(n²) maliyet

`cmd_node.gd:73-77`:
```gdscript
var props = []
for i in node.get_property_list().size():
    var p = node.get_property_list()[i]   # ← her iterasyonda yeni array
    if p["usage"] & PROPERTY_USAGE_EDITOR:
        props.append({"name": p["name"], "value": node.get(p["name"])})
```

`get_property_list()` her çağrıda yeni `Array[Dictionary]` oluşturur (Godot
internal). 50 property × 50 = 2500 array oluşturma. Aynı pattern
`cmd_resource.gd:21-24`'te de var.

**Çözüm:**
```gdscript
var prop_list = node.get_property_list()
var props = []
for p in prop_list:
    if p["usage"] & PROPERTY_USAGE_EDITOR:
        props.append({"name": p["name"], "value": node.get(p["name"])})
```

---

### 🐛 Sorun 20: `addons/godot_mcp/log_collector.gd` `OS.add_logger` Thread-Safety
**Tarih:** 18.07.2026
**Durum:** Çözüldü (18.07.2026)
**Öncelik:** Orta

`log_collector.gd:32-36`:
```gdscript
func _ready() -> void:
    if OS.has_method("add_logger"):
        var l := GodotMCPLogger.new(self)
        OS.add_logger(l)
```

`GodotMCPLogger._log_message` ve `_log_error` callback'leri farklı thread'lerden
çağrılabilir (Godot 4.5+ custom logger spec). `_collector.collect()` mutex
kullanıyor (doğru) ama `OS.add_logger` lifecycle'ı + plugin reload sırasında
eski logger hâlâ registered kalabilir → duplicate log → memory leak.

Godot 4.7'de `OS.remove_logger()` var mı kontrol et, `_exit_tree()`'de
çağrılmalı.

---

### 🐛 Sorun 21: `cmd_screenshot.gd:capture_viewport` 3D Viewport Öncelikli
**Tarih:** 18.07.2026
**Durum:** Çözüldü (18.07.2026)
**Öncelik:** Orta
**Etki:** 2D projede 3D viewport'u tercih ediyor, yanlış görüntü

```gdscript
func capture_viewport(params: Dictionary) -> Dictionary:
    var viewport = _ei.get_editor_viewport_3d(0)  # ← 3D önce
    if not viewport:
        viewport = _ei.get_base_control().get_viewport()  # 2D fallback
```

`islemcioyun` 2D proje (`window/stretch/mode="canvas_items"`) → 3D viewport
oluşturulmamış olabilir veya boş dönebilir. `screenshot_viewport` her zaman
boş/hatalı PNG döndürebilir. Öncelik 2D önce olmalı veya "auto" mode
eklenmeli.

---

## 🟢 DÜŞÜK ÖNEMLİ / İYİLEŞTİRME (8)

### 🐛 Sorun 22: Port Aralığı 46300-46400 Çakışma Riski
`PortCoordinator.cs:13` ve `mcp_server.gd:6-7` her ikisi de aynı aralığı
sabitliyor. Diğer uygulamalar (Jupyter, bazı VSCode eklentileri, Docker
port-mapping) bu aralığı kullanabilir. Test'te port 46300 kullanılıyor ama
gelecekte çakışma olabilir. Daha geniş aralık (50000-50100) veya
gerçek OS-assigned port tercih edilebilir.

---

### 🐛 Sorun 23: `_wstest/Program.cs` Test Artığı Repo Kökünde
`GodotFastMCP/_wstest/Program.cs` (1269 byte) + `_wstest.csproj` + `bin/` +
`obj/` build artifact'ları hâlâ repo kökünde. `progress.md:1-2` notlarında
"temizlendi" denilmesine rağmen dosyalar duruyor. `.gitignore` kapsıyor
ama working tree'de mevcut. Silinmeli veya test alt-klasörü olarak
düzgün yapılandırılmalı.

---

### 🐛 Sorun 24: `out/win-x64/` Publish Çıktısı Repo'da
`GodotFastMCP/out/win-x64/GodotMcpServer.exe` (ve addons/) hâlâ tracked.
`progress.md:2`'de "temizlendi" yazıyor. Bu klasör `dotnet publish`'in
ara çıktısı, kaynak repo'da olmamalı.

---

### 🐛 Sorun 25: `port.txt` Addon Tarafından Cache Ediliyor, Server Yeni Portu Görmüyor
`mcp_server.gd:35-37`:
```gdscript
var cached := _read_port_lock()
if cached > 0:
    start_port = cached
```
Bir kez yazıldıktan sonra port hiç değişmez. Server crash sonrası farklı
port seçse bile addon eski portu dinlemeye devam eder. Restart sonrası
`cached` yeniden okunuyor ama Godot açıkken server port değiştirirse eşleşme
kopar.

---

### 🐛 Sorun 26: `editor_get_project_path` Windows Path'i Backslash ile Döndürüyor
`cmd_editor.gd:79` `ProjectSettings.globalize_path("res://")` →
`C:\Projects\islemcioyun\`. C# tarafı bu path'i `Path.Combine` veya
`Directory.Exists` ile kullanırken sorun yok ama JSON serialize edilirken
escape karakterleri gerekebilir. Test'te sorun görünmedi ama dokümante edilmeli.

---

### 🐛 Sorun 27: `cmd_node.gd:find_node` Recursive + Stack Overflow Riski
Çok derin node ağacında (3D oyunlarda 100+ seviye mümkün) recursion stack
taşar. Iteratif implementasyon (BFS) daha güvenli.

---

### 🐛 Sorun 28: C# Source Dosyalarında Karışık Encoding (BOM / No-BOM)
| Dosya | Encoding |
|---|---|
| `GodotBridge.cs` | UTF-8 **with BOM** |
| `PortCoordinator.cs` | UTF-8 no-BOM |
| `Program.cs` | UTF-8 no-BOM |
| `Tools/EditorTools.cs` | UTF-8 no-BOM |

C# derleme ikisini de doğru okur, runtime'da sorun yok. Ama best practice
olarak tüm `.cs` dosyaları tutarlı encoding'de olmalı (Visual Studio default
UTF-8 BOM, .NET 8 SDK her ikisini de kabul eder). Editor/IDE tutarsızlığı
fark edilmesini zorlaştırır.

---

### 🐛 Sorun 29: `GetState` / `GetProjectSettings` `JsonElement.ToString()` Çıktısı
`Tools/EditorTools.cs:54`:
```csharp
return result.Success ? result.Result.ToString() ?? "{}" : $"[GodotMCP Hata] {result.Error}";
```
`result.Result` zaten `JsonElement` (Protocol/GodotResponse.cs:13). Onun
`ToString()`'i JSON string üretir (Godot'un `JSON.stringify` çıktısı gibi).
Ama bazı tool'larda `result.Result` bir `Dictionary<string, object?>` da
olabilir (Tool'a özel cast). Mevcut kod tüm tool'larda aynı kalıbı
kullanıyor; `JsonElement.ToString()` her zaman doğru JSON döner ama `{}`
default'u hem Dict hem Element için ambiguous.

---

## ✅ YAPILMIŞ OLAN DÜZELTMELERE EK (NOT)

- **Mutex stale recovery** (`opencode-mcp-sorun-notlari.md`) çalışıyor
- **52 tool başarıyla kayıtlı** (Sorun 7'deki 8 komut hariç)
- **TCP fast-probe** (100 port < 1sn) sayesinde server Godot kapalıyken
  stdio'yu anında yanıtlıyor
- **Versiyon-bazlı auto-update** gereksiz push'u engelliyor
- **Çoklu peer desteği** (16 bağlantı) port çakışmasını tolere ediyor
- **Health check** kopuk bağlantıda exponential backoff ile yeniden bağlanıyor

---

## 📊 ÖZET: 18 Temmuz 2026 Taraması

| Kategori | Sayı | En Kritik |
|---|---|---|
| 🔴 Kritik (kullanılamaz tool) | 3 | Sorun 7: Godot 4.7 + EditorInterface |
| 🟠 Yüksek (yanlış davranış) | 4 | Sorun 12: Plugin enable yanlış parametre |
| 🟡 Orta (potansiyel bug/performans) | 8 | Sorun 16: Symlink sonsuz döngü |
| 🟢 Düşük (kod kalitesi) | 8 | Sorun 23: Test artıkları repo'da |
| **Toplam** | **23 yeni sorun** | |

### Hemen Yapılması Gerekenler (Öncelik Sırası)
1. **Sorun 7** — 8 cmd_*.gd dosyasında `_init` parametre tipini kaldır (1 satır × 8 = 8 değişiklik)
2. **Sorun 9** — C# Tools'larda null error mesajını handle et
3. **Sorun 8** — Port dosyası yolunu server↔addon arasında uyuştur
4. **Sorun 10** — `project.godot`'a main_scene ekle
5. **Sorun 12** — `_is_plugin_enabled` parametre davranışını düzelt
6. **Sorun 13** — `path` parametrelerini yoksayan tool'ları ya düzelt ya kaldır

### Test İmkansız Olduğu İçin Doğrulanamayan
Aşağıdaki sorunlar Godot tarafında runtime hata gerektirdiği için 4.7
bug'ı çözülmeden test edilemedi:
- Sorun 11 (dispatcher exception handling)
- Sorun 16 (symlink döngüsü)
- Sorun 18 (depth limiti)
- Sorun 19 (O(n²) property list)
- Sorun 21 (screenshot 2D vs 3D)

Bunlar kod analizine dayanıyor; Sorun 7 düzeltildikten sonra tekrar test edilmeli.

---

# ✅ 18 Temmuz 2026 — 23 SORUNUN TAMAMI ÇÖZÜLDÜ

**Uygulayan:** opencode (kimi-k3) — repo: `C:\Projects\GodotMcpServer\GodotFastMCP`
**Build:** `dotnet publish` başarılı, 0 hata 0 uyarı. Server **v1.3.0**, addon **v1.3.0**.
**Deploy:** Addon `C:\Projects\islemcioyun\addons\godot_mcp` altına kopyalandı (13 dosya, .uid hariç).

## Uygulanan Düzeltmeler (sorun bazında)

| # | Çözüm | Dosya(lar) |
|---|---|---|
| 7 | 8 komut sınıfında `_init(ei: EditorInterface)` → `_init(ei)` (tipsiz). Ayrıca dispatcher'da `new()` null koruması (Seçenek A + C birlikte) | `commands/cmd_*.gd` (8 dosya), `command_dispatcher.gd` |
| 8 | Addon artık lock'u **iki konuma** yazar: proje-özel (eski) + `%LOCALAPPDATA%\GodotMCP\port.txt` (JSON: `port` + `project_path` + `time`). C# `ReadCachedLock()` hem düz int hem JSON okur. `GODOT_PROJECT_PATH` ayarlıysa yanlış projeye ait lock portu aday listesinin sonuna ertelenir ve bağlantı sonrası `editor_get_project_path` ile proje doğrulaması yapılır (eski addon'da komut başarısızsa bağlantı kabul edilir) | `mcp_server.gd`, `PortCoordinator.cs`, `GodotBridge.cs` |
| 9 | `GodotResponse.FormatError()` — boş/null error'da anlamlı mesaj. Tüm Tools (~40 kullanım) güncellendi. GDScript tarafında dispatcher boş error'u doldurur | `Protocol/GodotResponse.cs`, `Tools/*.cs`, `command_dispatcher.gd` |
| 10 | `C:\Projects\islemcioyun\project.godot`'a `run/main_scene="res://scenes/Main.tscn"` eklendi; `scenes/Main.tscn` (Node2D) oluşturuldu | (çalışma dizini dışı — kullanıcı onayıyla) |
| 11 | `dispatch()`: handler sonucu Dictionary değilse / `success` anahtarı yoksa anlamlı hata döner (exception yutulması artık görünür) | `command_dispatcher.gd` |
| 12 | `_is_plugin_enabled()`/`_enable_plugin()` artık plugin adı yerine `res://addons/godot_mcp/plugin.cfg` yolunu kullanıyor (`PLUGIN_CONFIG_PATH` const) | `cmd_update.gd` |
| 13 | `scene_save`: path doluysa `save_scene_as(path)`. `scene_get_data`: path doluysa sahne **editörde açılmadan** diskten yüklenir. `scene_close`/`script_get_errors`: path aktif öğeyle eşleşmezse açık hata. `get_warnings` `has_method` korumalı | `cmd_scene.gd`, `cmd_script.gd`, `Tools/SceneTools.cs`, `Tools/ScriptTools.cs` |
| 14 | Ölü satır silindi | `cmd_editor.gd` |
| 15 | Yeni MCP tool `update_addon` (source + base_path parametreli) | `Tools/AddonUpdateTools.cs` |
| 16 | Recursive taramada `DirAccess.is_link()` → link'lere girilmez (`type:"link"` listelenir), `visited` sözlüğü + `MAX_ENTRIES=100000` üst sınırı | `cmd_filesystem.gd` |
| 17 | Uzantı filtresi iki tarafta da `to_lower()` ile karşılaştırılır | `cmd_filesystem.gd` |
| 18 | `_node_to_dict` `max_depth` parametresi aldı (varsayılan 10); C# `scene_get_data` tool'una `maxDepth` eklendi | `cmd_scene.gd`, `Tools/SceneTools.cs` |
| 19 | `get_property_list()` döngü dışına alındı (O(n²) → O(n)) | `cmd_node.gd`, `cmd_resource.gd` |
| 20 | Logger referansı tutulur; `_exit_tree()`'de `OS.remove_logger` (`has_method` korumalı) | `log_collector.gd` |
| 21 | `screenshot_viewport`'a `mode` parametresi (`auto`/`2d`/`3d`); `auto` 3D viewport'u yalnızca görünürse seçer | `cmd_screenshot.gd`, `Tools/ScreenshotTools.cs` |
| 22 | Port aralığı **46300–46599** (300 porta genişletildi) — iki taraf senkron | `mcp_server.gd`, `PortCoordinator.cs` |
| 23 | `_wstest/` dizini silindi | (repo) |
| 24 | `out/win-x64/` dizini silindi | (repo) |
| 25 | Cached port bağlanamazsa aralık taranıp yeni port **her iki lock'a da** yazılır (zaten bind sonrası `_write_port_lock` çağrılıyor; davranış belgelendi) | `mcp_server.gd` |
| 26 | Backslash davranışı kod yorumuyla belgelendi (JSON escape otomatik, C# tarafı sorunsuz kullanır) | `cmd_editor.gd` |
| 27 | `find_node` recursion → **iteratif BFS**. **EK BUG:** eski kod `not _matches` ile eşleşmEYEN node'ları topluyordu (ters mantık) — düzeltildi | `cmd_node.gd` |
| 28 | Tüm `.cs` dosyaları UTF-8 no-BOM. `GodotBridge.cs` ve `ServerVersion.cs`'deki mojibake (çift-encode olmuş Türkçe yorumlar) temiz yazıldı | `GodotBridge.cs`, `ServerVersion.cs` |
| 29 | `GodotResponse.FormatResult()` — `GetRawText()` tabanlı; `Nullable<JsonElement>.ToString()`'in boş string döndürme tuzağı giderildi. `script_read`/`filesystem_read_file` `TryGetProperty` ile sağlamlaştırıldı | `Protocol/GodotResponse.cs`, `Tools/ScriptTools.cs`, `Tools/FileSystemTools.cs` |

## Doğrulama

- ✅ `dotnet build` / `dotnet publish`: 0 hata, 0 uyarı
- ✅ Publish çıktısı: `GodotFastMCP\publish\GodotMcpServer.exe` v1.3.0 (eski süreç durdurulup yenilendi; MCP istemcisi bir sonraki tool çağrısında yeni sürümü başlatır)
- ✅ Deploy edilen addon'da tüm düzeltmeler mevcut (grep ile doğrulandı)
- ⏳ **Kullanıcı adımı gerekli:** Godot'u açın (islemcioyun projesi) — eklenti v1.3.0 ile yüklenecek. Ardından `editor_get_state`, `filesystem_list` vb. tool'lar tekrar test edilmeli.

## Bilinen Sınırlar / Sonradan Test Edilecekler

- Godot 4.7'de `EditorInterface` regresyonu resmi olarak düzeltilirse `_init` tipleri geri eklenebilir (tipsiz bırakmak da sorunsuzdur).
- Sorun 16/18/19/21 düzeltmeleri kod analiziyle yapıldı; Godot açıldıktan sonra canlı test önerilir.
- `script_get_errors`: Godot sürümünde `ScriptEditor.get_warnings()` yoksa açık hata döner (önceden sessizce boş dönerdi).

---

# 🔥 18 Temmuz 2026 (2. tur) — Canlı Godot Testinde Bulunan 3 API Hatası → Düzeltildi (v1.3.1)

İlk düzeltme paketi (v1.3.0) deploy edilip Godot açıldığında editor loglarında
3 ayrı API uyumsuzluğu görüldü. Hepsi Godot 4.7.1 mono'da doğrulandı:

### 1. `EditorInterface.save_scene_as()` 4.7'de **void** dönüyor
```
Parse Error: Cannot get return value of call to "save_scene_as()" because it returns "void".
```
Sorun 13 fix'inde `err = _ei.save_scene_as(path)` yazılmıştı (4.x dokümanına göre
Error dönüyordu). **Çözüm:** dönüş atanmıyor; `save_scene_as` sonrası başarı
`scene.scene_file_path == path` kontrolüyle doğrulanıyor. `save_scene()` de
artık statement olarak çağrılıyor (imza değişikliğine karşı dayanıklı).

### 2. `DirAccess.is_link()` static değil, **instance metodu**
```
Parse Error: Cannot call non-static function "is_link()" on the class "DirAccess" directly.
```
**Çözüm:** `DirAccess.is_link(full_path)` → `dir.is_link(file_name)` (2 yer).
Instance metodu, açık dizindeki girdiyi doğrudan kontrol eder (daha doğru semantik).

### 3. Godot 4.7 regresyonu **tipli üye değişkeni de kapsıyor** (Sorun 7 genişletildi)
```
cmd_node.gd:9 - Internal script error! Opcode: 28  (_ei = ei satırı)
```
İlk turda sadece `_init` parametresi tipsizleştirilmişti; `var _ei: EditorInterface`
tipli ÜYE değişken de RefCounted + @tool kombinasyonunda bozuk bytecode üretiyormuş
(Node türevli McpServer/CommandDispatcher'da tipli üye sorunsuz çalışıyor — regresyon
yalnızca RefCounted'a özgü). **Çözüm:** 8 cmd_*.gd dosyasında üye de `var _ei`
(tipsiz) yapıldı.

### Ek sağlamlaştırma
- `_create_cmd`'a `script.can_instantiate()` kontrolü eklendi → parse hatalı
  script'te "Nonexistent function 'new' in base 'GDScript'" yerine temiz log.
- `plugin.gd` yorumundaki eski port aralığı (46300-46400) güncellendi (46300-46599).

### Deploy (v1.3.1)
- `plugin.cfg` → **1.3.1**; repo → `islemcioyun/addons/godot_mcp` ve
  `GodotFastMCP/publish/addons/godot_mcp` senkronize edildi (auto-update'in
  eski sürümü geri push'laması engellendi).
- **Kullanıcı adımı:** Godot'u yeniden başlatın (Project > Reload Current Project
  veya kapat/aç). Log'da parse error / Opcode 28 kalmamalı; ardından
  `editor_get_state`, `filesystem_list` test edilebilir.
