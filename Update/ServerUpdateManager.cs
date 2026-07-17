using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

/// <summary>
/// Server'ın kendi kendini güncellemesi:
/// GitHub Releases API'sinden en son sürümü kontrol eder, platforma uygun
/// zip'i indirir, staging klasörüne açar ve ayrık (detached) bir "watcher"
/// script başlatır. Watcher, server prosesi kapandıktan sonra dosyaları
/// kurulum dizininin üzerine kopyalar (Windows'ta çalışan exe kilitlidir,
/// bu yüzden güncelleme proses dışından uygulanır).
/// </summary>
public class ServerUpdateManager
{
    private const string ApiBase = "https://api.github.com/repos/" + ServerVersion.GitHubRepo;
    private const string MarkerFileName = "update.applied";

    private readonly ILogger<ServerUpdateManager> _logger;
    private static readonly HttpClient _http = CreateClient();

    public ServerUpdateManager(ILogger<ServerUpdateManager> logger) => _logger = logger;

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("GodotFastMCP", ServerVersion.Current));
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    // ── Yollar ──────────────────────────────────────────────────────────────

    public static string InstallDir => AppContext.BaseDirectory;

    private static string UpdateRootDir
    {
        get
        {
            var baseDir = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
            return Path.Combine(baseDir, "GodotMcpServer", "update");
        }
    }

    private static string MarkerPath => Path.Combine(InstallDir, MarkerFileName);

    public static string CurrentRid
    {
        get
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return "win-x64";
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                return RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "osx-arm64" : "osx-x64";
            return RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "linux-arm64" : "linux-x64";
        }
    }

    // ── Sürüm kontrolü ──────────────────────────────────────────────────────

    /// <summary>GitHub'daki en son release'i döndürür. Ağ hatasında veya henüz release yoksa null.</summary>
    public async Task<ReleaseInfo?> GetLatestReleaseAsync(CancellationToken ct = default)
    {
        try
        {
            using var response = await _http.GetAsync($"{ApiBase}/releases/latest", ct);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                // Repo'da henüz hiç release yok — hata değil, normal durum.
                _logger.LogDebug("[Update] Henüz yayınlanmış release yok.");
                return null;
            }
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(ct);
            return JsonSerializer.Deserialize<ReleaseInfo>(json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("[Update] Release bilgisi alınamadı: {Message}", ex.Message);
            return null;
        }
    }

    /// <summary>Yeni sürüm var mı? (release, updateAvailable) döner.</summary>
    public async Task<(ReleaseInfo? release, bool updateAvailable)> CheckForUpdateAsync(CancellationToken ct = default)
    {
        var release = await GetLatestReleaseAsync(ct);
        if (release?.TagName is null) return (release, false);
        return (release, ServerVersion.IsNewer(release.TagName, ServerVersion.Current));
    }

    /// <summary>
    /// Geliştirici ortamı mı? (BaseDirectory'nin üstünde .git varsa kaynak koddan
    /// çalışıyoruz demektir → self-update yerine git pull önerilir.)
    /// </summary>
    public static bool IsDevEnvironment()
    {
        var dir = new DirectoryInfo(InstallDir);
        for (int i = 0; i < 6 && dir is not null; i++, dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, ".git")))
                return true;
        return false;
    }

    // ── İndirme ve uygulama ─────────────────────────────────────────────────

    /// <summary>
    /// Yeni sürümü indirir, staging'e açar ve watcher script'i ayrık başlatır.
    /// Çağıran, yanıtı ilettikten sonra prosesi kapatmalıdır (watcher kopyalar).
    /// </summary>
    public async Task<(bool success, string message)> StageUpdateAsync(
        ReleaseInfo release, CancellationToken ct = default)
    {
        try
        {
            var rid = CurrentRid;
            var assetName = $"GodotMcpServer-{rid}.zip";
            var asset = release.Assets?.FirstOrDefault(a =>
                string.Equals(a.Name, assetName, StringComparison.OrdinalIgnoreCase));
            if (asset?.DownloadUrl is null)
                return (false, $"Release'de bu platform için paket yok: {assetName}");

            // Güvenlik: sadece github.com üzerinden indir
            if (!Uri.TryCreate(asset.DownloadUrl, UriKind.Absolute, out var uri) ||
                !uri.Host.EndsWith("github.com", StringComparison.OrdinalIgnoreCase))
                return (false, $"Güvenilmeyen indirme adresi: {asset.DownloadUrl}");

            var staging = Path.Combine(UpdateRootDir, release.TagName?.TrimStart('v', 'V') ?? "latest");
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
            Directory.CreateDirectory(staging);

            var zipPath = Path.Combine(UpdateRootDir, assetName);
            _logger.LogInformation("[Update] İndiriliyor: {Url}", asset.DownloadUrl);

            await using (var stream = await _http.GetStreamAsync(uri, ct))
            await using (var file = File.Create(zipPath))
                await stream.CopyToAsync(file, ct);

            ZipFile.ExtractToDirectory(zipPath, staging, overwriteFiles: true);
            File.Delete(zipPath);

            // Staging'de en az server binary'si olmalı
            var exeName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? "GodotMcpServer.exe" : "GodotMcpServer";
            if (!File.Exists(Path.Combine(staging, exeName)))
                return (false, "İndirilen paket geçersiz: server binary bulunamadı.");

            SpawnWatcher(staging, InstallDir);
            return (true, $"v{release.TagName?.TrimStart('v', 'V')} indirildi ve hazırlandı. " +
                          "Server kapanır kapanmaz güncelleme uygulanacak.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning("[Update] Güncelleme hazırlanamadı: {Message}", ex.Message);
            return (false, $"Güncelleme hatası: {ex.Message}");
        }
    }

    /// <summary>
    /// Proses dışı watcher: server PID'si kapanana kadar bekler,
    /// sonra staging dosyalarını kurulum dizinine kopyalar.
    /// </summary>
    private void SpawnWatcher(string staging, string target)
    {
        var pid = Environment.ProcessId;
        var scriptDir = UpdateRootDir;
        Directory.CreateDirectory(scriptDir);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var script = Path.Combine(scriptDir, "apply-update.ps1");
            File.WriteAllText(script, """
                param([int]$ServerPid, [string]$Staging, [string]$Target)
                while (Get-Process -Id $ServerPid -ErrorAction SilentlyContinue) { Start-Sleep -Milliseconds 500 }
                Start-Sleep -Milliseconds 800
                Copy-Item -Path (Join-Path $Staging '*') -Destination $Target -Recurse -Force
                New-Item -ItemType File -Path (Join-Path $Target 'update.applied') -Force | Out-Null
                Remove-Item $Staging -Recurse -Force -ErrorAction SilentlyContinue
                Remove-Item $MyInvocation.MyCommand.Path -Force -ErrorAction SilentlyContinue
                """);
            Process.Start(new ProcessStartInfo
            {
                FileName = "powershell",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{script}\" {pid} \"{staging}\" \"{target}\"",
                CreateNoWindow = true,
                UseShellExecute = false
            });
        }
        else
        {
            var script = Path.Combine(scriptDir, "apply-update.sh");
            File.WriteAllText(script, """
                #!/bin/sh
                while kill -0 "$1" 2>/dev/null; do sleep 0.5; done
                sleep 1
                cp -R "$2"/. "$3"/
                touch "$3/update.applied"
                rm -rf "$2"
                rm -f "$0"
                """);
            Process.Start("chmod", $"+x \"{script}\"").WaitForExit(2000);
            Process.Start(new ProcessStartInfo
            {
                FileName = "/bin/sh",
                Arguments = $"\"{script}\" {pid} \"{staging}\" \"{target}\"",
                CreateNoWindow = true,
                UseShellExecute = false
            });
        }

        _logger.LogInformation("[Update] Watcher başlatıldı (PID {Pid} kapanınca uygulanacak).", pid);
    }

    // ── Başlangıç bildirimi ─────────────────────────────────────────────────

    /// <summary>
    /// Önceki oturumda uygulanan güncelleme var mı? Varsa versiyonu loglar
    /// ve marker dosyasını siler. Program.cs başlangıcında çağrılır.
    /// </summary>
    public static void ConsumeUpdateMarker(ILogger logger)
    {
        try
        {
            if (File.Exists(MarkerPath))
            {
                File.Delete(MarkerPath);
                logger.LogInformation("[Update] ✅ Güncelleme başarıyla uygulandı. Şu anki sürüm: v{Version}",
                    ServerVersion.Current);
            }
        }
        catch { /* marker okunamazsa sorun değil */ }
    }
}

/// <summary>GitHub Releases API yanıt modeli.</summary>
public class ReleaseInfo
{
    [JsonPropertyName("tag_name")] public string? TagName { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("body")] public string? Body { get; set; }
    [JsonPropertyName("published_at")] public DateTime PublishedAt { get; set; }
    [JsonPropertyName("assets")] public List<ReleaseAsset>? Assets { get; set; }
}

public class ReleaseAsset
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("browser_download_url")] public string? DownloadUrl { get; set; }
    [JsonPropertyName("size")] public long Size { get; set; }
}
