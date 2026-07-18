using System.Diagnostics;
using System.Runtime.InteropServices;

/// <summary>
/// Binlerce kullanıcıda çakışmasız çalışması için:
/// 1. Tek instance garantisi (global mutex + stale recovery)
/// 2. Çakışmasız dinamik port seçimi (yalı hazır aralık)
/// 3. Seçilen portu lock dosyasına yazar; Godot eklentisi buradan okur
/// </summary>
public static class PortCoordinator
{
    // IANA "dynamic/private" aralığı: başka uygulamalar nadiren tutar.
    // Sorun 22 fix: aralık 46300-46599'a genişletildi (300 port).
    // Godot tarafı mcp_server.gd ile AYNI aralıkta tutulmalı.
    public const int PortRangeStart = 46300;
    public const int PortRangeEnd = 46599;

    // Lock dosyası: Godot eklentisi ile server arasında port haberleşmesi.
    // Sorun 8 fix: addon artık bu paylaşılan konuma JSON yazar:
    //   {"port": 46300, "project_path": "C:/...", "time": ...}
    // (Eski format düz int'ti — ikisi de desteklenir.)
    private static readonly string LockDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GodotMCP");
    private static readonly string LockFile = Path.Combine(LockDir, "port.txt");
    private static readonly string OwnerPidFile = Path.Combine(LockDir, "owner.pid");

    // Tek instance için global mutex adı (her kullanıcı için ayrı).
    private const string MutexName = @"Global\GodotMcpServer_SingleInstance";

    /// <summary>
    /// Aynı anda tek GodotMcpServer.exe çalıştığından emin olur.
    /// NULL dönerse çağıran proses hemen çıkmalıdır.
    /// <para>
    /// Stale recovery: eski sahip çökmüşse (mutex abandoned), Windows kernel otomatik
    /// serbest bırakır; .NET <see cref="AbandonedMutexException"/> fırlatır. Bu durumda
    /// mutex'i ele geçirip owner.pid dosyasını yeni PID ile güncelleriz.
    /// </para>
    /// </summary>
    public static Mutex? AcquireSingleInstance()
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            Mutex? mutex = null;
            bool createdNew = false;
            bool abandoned = false;

            try
            {
                mutex = new Mutex(initiallyOwned: true, MutexName, out createdNew);
            }
            catch (AbandonedMutexException)
            {
                abandoned = true;
            }

            if (abandoned)
            {
                // Sahiplik bize geçti (kernel signaled) ama handle kayboldu.
                // Aynı adla yeniden açıp WaitOne ile referansını al.
                try
                {
                    mutex = Mutex.OpenExisting(MutexName);
                    if (mutex.WaitOne(TimeSpan.FromSeconds(2)))
                    {
                        WriteOwnerPid(Environment.ProcessId);
                        return mutex;
                    }
                    try { mutex.Dispose(); } catch { }
                }
                catch { /* bir sonraki denemede tekrar dene */ }
                continue;
            }

            if (createdNew)
            {
                WriteOwnerPid(Environment.ProcessId);
                return mutex;
            }

            // createdNew=false: başka bir canlı süreç tutuyor olabilir.
            var existingPid = ReadOwnerPid();
            if (existingPid is { } pid && IsProcessAlive(pid))
            {
                try { mutex!.ReleaseMutex(); } catch { }
                mutex!.Dispose();
                return null;
            }

            // Sahipsiz ya da ölü PID. Handle'ı bırak, kısa bekle, tekrar dene.
            try { mutex!.ReleaseMutex(); } catch { }
            mutex!.Dispose();
            Thread.Sleep(50);
        }

        return null;
    }

    private static int? ReadOwnerPid()
    {
        try
        {
            if (File.Exists(OwnerPidFile) && int.TryParse(File.ReadAllText(OwnerPidFile).Trim(), out var p))
                return p;
        }
        catch { }
        return null;
    }

    private static void WriteOwnerPid(int pid)
    {
        try
        {
            Directory.CreateDirectory(LockDir);
            File.WriteAllText(OwnerPidFile, pid.ToString());
        }
        catch { /* yardımcı dosya - port dosyası gibi opsiyonel */ }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);

    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    private static bool IsProcessAlive(int pid)
    {
        if (pid <= 0) return false;
        if (pid == Environment.ProcessId) return true;

        try
        {
            using var p = Process.GetProcessById(pid);
            return !p.HasExited;
        }
        catch (ArgumentException) { return false; }
        catch { /* aşağıdaki kernel yoluna düş */ }

        try
        {
            var h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (h == IntPtr.Zero) return false;
            try { return true; } finally { CloseHandle(h); }
        }
        catch { return false; }
    }

    /// <summary>
    /// Dinleyecek boş portu bulur (TCP üzerinden probe ederek).
    /// </summary>
    public static int FindFreePort()
    {
        // Önce daha önce seçilmiş ve hâlâ geçerli bir port var mı? (sıcak yeniden başlatmada stabilite)
        var cached = ReadCachedPort();
        if (cached is { } p && IsPortFree(p))
            return p;

        for (int port = PortRangeStart; port <= PortRangeEnd; port++)
        {
            if (IsPortFree(port))
                return port;
        }
        throw new InvalidOperationException(
            $"Boş port bulunamadı ({PortRangeStart}-{PortRangeEnd}). Ağ durumunu kontrol edin.");
    }

    /// <summary>
    /// Gerçekten dinlemeye başlayan portu lock dosyasına yazar.
    /// Godot eklentisi bu dosyayı okuyup aynı porta bağlanır.
    /// </summary>
    public static void PublishPort(int port)
    {
        try
        {
            Directory.CreateDirectory(LockDir);
            File.WriteAllText(LockFile, port.ToString());
        }
        catch
        {
            // Lock dosyası yazılamasa bile bağlantı çalışır; sadece Godot auto-discovery zorlaşır.
        }
    }

    /// <summary>
    /// Godot eklentisinin paylaşılan lock'a yazdığı portu döndürür. Yoksa null.
    /// Hem düz int (eski format) hem JSON (yeni format: {"port":N,...}) desteklenir.
    /// </summary>
    public static int? ReadCachedPort()
    {
        var (port, _) = ReadCachedLock();
        return port;
    }

    /// <summary>
    /// Paylaşılan lock dosyasından (port, project_path) okur.
    /// Sorun 8 fix: birden fazla Godot projesi açıksa server'ın hangi projeye
    /// bağlanacağını doğrulamak için proje yolu da döndürülür.
    /// </summary>
    public static (int? Port, string? ProjectPath) ReadCachedLock()
    {
        try
        {
            if (!File.Exists(LockFile)) return (null, null);
            var text = File.ReadAllText(LockFile).Trim();
            if (text.Length == 0) return (null, null);

            // Eski format: düz int
            if (int.TryParse(text, out var p))
                return (p, null);

            // Yeni format: JSON {"port": 46300, "project_path": "...", "time": ...}
            if (text.StartsWith('{'))
            {
                using var doc = System.Text.Json.JsonDocument.Parse(text);
                var root = doc.RootElement;
                int? port = root.TryGetProperty("port", out var pe) && pe.TryGetInt32(out var pv) ? pv : null;
                var project = root.TryGetProperty("project_path", out var pp) ? pp.GetString() : null;
                return (port, project);
            }
        }
        catch { }
        return (null, null);
    }

    private static bool IsPortFree(int port)
    {
        // TCP socket üzerinden gerçek probe: sockaddr'a bind edip hemen kapat.
        using var socket = new System.Net.Sockets.Socket(
            System.Net.Sockets.AddressFamily.InterNetwork,
            System.Net.Sockets.SocketType.Stream,
            System.Net.Sockets.ProtocolType.Tcp);
        try
        {
            socket.Bind(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, port));
            return true;
        }
        catch (System.Net.Sockets.SocketException)
        {
            return false; // port başkası tarafından tutuluyor
        }
    }
}
