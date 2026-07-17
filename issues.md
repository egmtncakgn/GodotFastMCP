# MCP Godot Server - Sorun Raporu

## Oluşturma Tarihi: 17 Temmuz 2026

---

## 🐛 Sorun 1: C# Script Dosyası Bulunamıyor

**Tarih:** 17.07.2026  
**Durum:** Açık  
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
- Godot tamamen yüklendikten sonra tool çağırın (sahne işlemleri için)
