using System.Security.Principal;

/// <summary>
/// Binlerce kullanıcıda çakışmasız çalışması için:
/// 1. Tek instance garantisi (global mutex)
/// 2. Çakışmasız dinamik port seçimi (yalı hazır aralık)
/// 3. Seçilen portu lock dosyasına yazar; Godot eklentisi buradan okur
/// </summary>
public static class PortCoordinator
{
    // IANA "dynamic/private" aralığı: başka uygulamalar nadiren tutar.
    public const int PortRangeStart = 46300;
    public const int PortRangeEnd = 46400;

    // Lock dosyası: Godot eklentisi ile server arasında port haberleşmesi.
    private static readonly string LockDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GodotMCP");
    private static readonly string LockFile = Path.Combine(LockDir, "port.txt");

    // Tek instance için global mutex adı (her kullanıcı için ayrı).
    private const string MutexName = @"Global\GodotMcpServer_SingleInstance";

    /// <summary>
    /// Aynı anda tek GodotMcpServer.exe çalıştığından emin olur.
    /// false dönerse çağıran proses hemen çıkmalıdır.
    /// </summary>
    public static Mutex? AcquireSingleInstance()
    {
        var mutex = new Mutex(initiallyOwned: true, MutexName, out bool createdNew);
        if (!createdNew)
        {
            try { mutex.ReleaseMutex(); } catch { }
            mutex.Dispose();
            return null;
        }
        return mutex;
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
    /// Godot eklentisinin okuyacağı portu döndürür. Yoksa null.
    /// </summary>
    public static int? ReadCachedPort()
    {
        try
        {
            if (File.Exists(LockFile) && int.TryParse(File.ReadAllText(LockFile).Trim(), out var p))
                return p;
        }
        catch { }
        return null;
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
