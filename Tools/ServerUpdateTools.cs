using System.ComponentModel;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

/// <summary>
/// Server'ın kendi güncelleme sistemi (addon güncellemeden bağımsız).
/// GitHub Releases üzerinden çalışır; release workflow'u her tag'de
/// GodotMcpServer-{rid}.zip paketleri yayınlar.
/// </summary>
[McpServerToolType]
public class ServerUpdateTools(
    ServerUpdateManager updater,
    IHostApplicationLifetime lifetime,
    ILogger<ServerUpdateTools> logger)
{
    [McpServerTool(Name = "server_version")]
    [Description("Çalışan GodotMcpServer'ın versiyonunu, platformunu ve kurulum dizinini döndürür.")]
    public Task<string> GetVersion()
    {
        var dev = ServerUpdateManager.IsDevEnvironment();
        return Task.FromResult(
            $"GodotMcpServer v{ServerVersion.Current}\n" +
            $"Platform: {ServerUpdateManager.CurrentRid}\n" +
            $"Kurulum dizini: {ServerUpdateManager.InstallDir}\n" +
            $"Mod: {(dev ? "geliştirici (kaynak kod — güncelleme için git pull)" : "kurulu sürüm (self-update aktif)")}");
    }

    [McpServerTool(Name = "server_check_update")]
    [Description("GitHub Releases'de server'ın daha yeni bir sürümü olup olmadığını kontrol eder. Güncelleme varsa sürüm notlarıyla birlikte bildirir.")]
    public async Task<string> CheckUpdate()
    {
        try
        {
            var (release, available) = await updater.CheckForUpdateAsync();
            if (release is null)
                return "[GodotMCP] Güncelleme kontrolü yapılamadı (ağ hatası veya GitHub erişilemez).";

            var latest = release.TagName?.TrimStart('v', 'V') ?? "?";
            if (!available)
                return $"[GodotMCP] Server güncel. (v{ServerVersion.Current}, en son sürüm: v{latest})";

            var notes = release.Body is { Length: > 0 } body
                ? body.Length > 500 ? body[..500] + "…" : body
                : "(sürüm notu yok)";

            return $"[GodotMCP] 🆕 Yeni server sürümü mevcut: v{latest} (şu an: v{ServerVersion.Current})\n" +
                   $"Yayın tarihi: {release.PublishedAt:yyyy-MM-dd}\n" +
                   $"Sürüm notları:\n{notes}\n\n" +
                   $"Güncellemek için 'server_self_update' tool'unu çağırın.";
        }
        catch (Exception ex)
        {
            return $"[GodotMCP] Güncelleme kontrolü hatası: {ex.Message}";
        }
    }

    [McpServerTool(Name = "server_self_update")]
    [Description("Server'ın en son sürümünü GitHub Releases'den indirir ve uygular. Server kendini kapatır; MCP istemcisi bir sonraki tool çağrısında yeni sürümü otomatik başlatır. Geliştirici ortamında (kaynak kod) çalışmaz.")]
    public async Task<string> SelfUpdate()
    {
        try
        {
            if (ServerUpdateManager.IsDevEnvironment())
                return "[GodotMCP] Kaynak koddan çalışıyorsunuz. Güncellemek için: git pull && dotnet publish";

            var (release, available) = await updater.CheckForUpdateAsync();
            if (release is null)
                return "[GodotMCP] Güncelleme kontrolü yapılamadı (ağ hatası).";
            if (!available)
                return $"[GodotMCP] Zaten güncelsiniz (v{ServerVersion.Current}).";

            var (success, message) = await updater.StageUpdateAsync(release);
            if (!success)
                return $"[GodotMCP Hata] {message}";

            // Yanıt istemciye ulaştıktan sonra graceful shutdown:
            // watcher script proses kapanır kapanmaz dosyaları değiştirir.
            _ = Task.Run(async () =>
            {
                await Task.Delay(2500);
                logger.LogInformation("[Update] Güncelleme uygulanıyor, server kapanıyor...");
                lifetime.StopApplication();
            });

            return $"[GodotMCP] ✅ {message}\n" +
                   "Server şimdi kapanacak ve yeni sürüme güncellenecek. " +
                   "MCP istemciniz bir sonraki tool çağrısında yeni sürümü otomatik başlatır " +
                   "(başlamazsa istemciyi yeniden başlatın).";
        }
        catch (Exception ex)
        {
            return $"[GodotMCP] Self-update hatası: {ex.Message}";
        }
    }
}
