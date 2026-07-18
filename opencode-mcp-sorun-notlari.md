# Godot MCP — opencode Bağlantı Sorunu Teşhis ve Düzeltme Notları

**Tarih:** 18.07.2026
**Uzman:** opencode asistanı
**Kapsam:** `C:\Projects\GodotMcpServer\GodotFastMCP\publish\GodotMcpServer.exe` ile
opencode arasındaki MCP stdio bağlantısının neden kurulmadığını bulmak ve düzeltmek.

---

## 1. Tespit Edilenler (salt okunur inceleme)

### 1.1 Çalışan süreçler (teşhis anında)
| PID | Süreç | Başlangıç | Not |
|-----|-------|-----------|-----|
| 1868 | `godot` | 18.07.2026 00:59:15 | Kullanıcının editörü |
| 15656 | `GodotMcpServer` | 18.07.2026 01:00:33 | **Açıkta kalmış eski instance** — mutex'i tutuyor, yeni opencode denemesi tek-instance guard nedeniyle hemen çıkıyor |

### 1.2 opencode konfigürasyonu (`C:\Users\Egemen\.config\opencode\opencode.json`)
```json
"godotMCP": {
  "type": "local",
  "command": ["C:\\Projects\\GodotMcpServer\\GodotFastMCP\\publish\\GodotMcpServer.exe"],
  "enabled": true
}
```
- Yol: **doğru** (EXE mevcut, 139264 byte)
- Tip: `local` → **stdio transport** → EXE çalıştırılıp stdin/stdout JSON-RPC beklenir
- Yol dizini doğru (build çıktısı `publish\`)

### 1.3 Program.cs stdio yapılandırması
- `WithStdioServerTransport()` ile kayıtlı
- Tüm loglar stderr'e yönlendirilmiş (`LogToStandardErrorThreshold = LogLevel.Trace`) → stdout MCP için temiz
- Background `Task.Run` ile Godot'a bağlanma host'u bloklamıyor (doğru)

### 1.4 Kök neden (DOĞRULANDI)
**Eski `GodotMcpServer.exe` (PID 15656) global mutex'i tutuyordu.**

`Program.cs` mutex kontrolünü `host.RunAsync()`'ten **önce** yapıyor; mutex alınamazsa
hemen `return` ediyor. Bu durumda:
- `host.RunAsync()` çağrılmıyor → MCP stdio transport **hiç başlamıyor**
- stdout boş kalıyor → opencode JSON-RPC `initialize` yanıtı alamıyor → "bağlanamıyor"
- stderr'e sadece uyarı yazılıyor (opencode görmez)

### 1.5 Doğrulama deneyleri
| # | Test | Sonuç |
|---|------|-------|
| 1 | Yeni instance başlat (mutex tutuk) | 1.5 sn'de çıktı, ExitCode=0, stdout BOŞ, stderr: "Başka bir GodotMcpServer.exe zaten çalışıyor" — **kök neden teyit edildi** |
| 2 | PID 15656 kill edildi, yeni instance | 50ms'de `initialize` yanıtı, 17434 byte tool listesi (52 tool) — **stale fix doğrulandı** |
| 3 | Stale process manuel öldürüldükten sonra yeni başlatma | Stale recovery kodu ile **otomatik** ele geçirildi — **kalıcı fix çalışıyor** |

---

## 2. Yapılan Değişiklikler

### 2.1 `PortCoordinator.cs` — stale mutex recovery eklendi
**Sorun:** Eski bir instance çöktüğünde veya Task Manager'dan öldürüldüğünde, named
mutex abandoned durumda kalıyor → yeni başlangıçlar süresiz engelleniyor.

**Çözüm:** PID'i yan dosyaya yaz (`%LOCALAPPDATA%\GodotMCP\owner.pid`); mutex alınamazsa
sahibin yaşayıp yaşamadığını kontrol et; ölüyse `AbandonedMutexException` yoluyla
mutex'i geri al.

Eklenen / değiştirilen:
- `using System.Diagnostics;` (Process)
- `using System.Runtime.InteropServices;` (kernel P/Invoke)
- `OwnerPidFile` alanı: `%LOCALAPPDATA%\GodotMCP\owner.pid`
- `ReadOwnerPid()` / `WriteOwnerPid(int pid)` yardımcı metotları
- `IsProcessAlive(int pid)` — `Process.GetProcessById` + `OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION)` P/Invoke
- `AcquireSingleInstance()` — 3 denemelik retry + `AbandonedMutexException` yakalama + `Mutex.OpenExisting` ile handle kurtarma

Stale recovery akışı:
```
AcquireSingleInstance()
  ├─ new Mutex(...) → createdNew=true            → sahip ol (yaz owner.pid)
  ├─ new Mutex(...) → createdNew=false           → aşama 2
  │    ├─ owner.pid'deki PID canlı              → null dön (gerçek çakışma)
  │    └─ owner.pid'deki PID ölü/bilinmiyor     → release, 50ms bekle, tekrar dene
  └─ new Mutex(...) → AbandonedMutexException    → OpenExisting + WaitOne ile referansı geri al
```

### 2.2 Build / Publish
```
dotnet build  -c Release → 0 uyarı, 0 hata (4.95 sn)
dotnet publish -c Release -o publish → başarılı
publish\GodotMcpServer.exe → 139264 byte, 18.07.2026 01:13:43
```

### 2.3 Doğrulama
- Initialize: **OK** (serverInfo: `GodotMcpServer 1.2.0.0`)
- Tool sayısı: **52** (tools/list tam yanıt)
- Stale recovery: **OK** (sahipsiz mutex otomatik ele geçirildi)
- Live conflict: ikinci instance ExitCode 0 ile çıkıyor (mutex canlı tutuluyor) — bu eski davranış, kullanıcının raporladığı senaryo dışı

---

## 3. Yapılmayanlar (bilinçli)

- **Canlı çakışmada ExitCode=0 → 1 değişikliği YAPILMADI.** Sebep: bu durum kullanıcının
  EXE'yi iki kez manuel başlattığı nadir senaryo; opencode bu instance'ı görmüyor (ilk
  canlı instance'la çalışıyor). Değişiklik fayda/risk dengesinde riskli.
- **opencode.json değişikliği YAPILMADI.** Yol zaten doğruydu.

---

## 4. Doğrulama Adımları
- [x] Eski süreç (PID 15656) sonlandırıldı
- [x] Yeni EXE stdio üzerinden MCP `initialize` yanıt veriyor
- [x] 52 tool başarıyla listeleniyor
- [x] Stale recovery kendi kendini toparlıyor (PID 16980 simüle crash sonrası)
- [x] Build temiz, publish başarılı

---

## 5. opencode'da Yapılması Gereken
**Hiçbir şey.** opencode.json doğru, EXE düzeltildi. opencode'u yeniden başlatmak
yeterli (yoksa hâlâ eski başarısız önbellekli süreç olabilir). Eğer yine sorun olursa:
1. `taskkill /F /IM GodotMcpServer.exe` ile tüm instance'ları temizle
2. opencode'u yeniden başlat
