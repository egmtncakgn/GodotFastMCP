using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

[McpServerToolType]
public class AddonUpdateTools(GodotBridge bridge, ILogger<AddonUpdateTools> logger)
{
    private readonly ILogger<AddonUpdateTools> _logger = logger;

    // ── Addon kaynak dizini çözümleme ───────────────────────────────────────
    // ESKİ BUG: BaseDirectory/../addons sabitti → son kullanıcıda (publish
    // dizini repo içinde değilken) klasör bulunamıyor, auto-update sessizce
    // hiçbir şey yapmıyordu. Şimdi aday dizinler sırayla denenir:
    //   1) <kurulum>/addons/godot_mcp          (publish çıktısı — csproj kopyalar)
    //   2) BaseDirectory'den yukarı 6 seviye tarama (dev: bin/Debug/net8.0 → repo kökü)

    private static string? FindAddonSourceDir()
    {
        // 1) Publish dizininde gömülü kopya
        var embedded = Path.Combine(AppContext.BaseDirectory, "addons", "godot_mcp");
        if (IsValidAddonDir(embedded)) return embedded;

        // 2) Yukarı doğru tara (geliştirici ortamı: repo kökünü bul)
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 6 && dir is not null; i++, dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "addons", "godot_mcp");
            if (IsValidAddonDir(candidate)) return candidate;
        }
        return null;
    }

    private static bool IsValidAddonDir(string path) =>
        Directory.Exists(path) && File.Exists(Path.Combine(path, "plugin.cfg"));

    /// <summary>
    /// Server'ın gönderdiği addon'un versiyonu — tek kaynak: plugin.cfg.
    /// Okunamazsa null döner (bu durumda versiyon karşılaştırması atlanır).
    /// </summary>
    private static string? ReadShippedAddonVersion(string addonDir)
    {
        try
        {
            foreach (var line in File.ReadLines(Path.Combine(addonDir, "plugin.cfg")))
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("version=", StringComparison.Ordinal))
                    return trimmed["version=".Length..].Trim().Trim('"');
            }
        }
        catch { }
        return null;
    }

    [McpServerTool(Name = "sync_addon")]
    [Description("GodotMCP addon'unu git remote'dan günceller ve Godot projesinin addons/godot_mcp klasörüne kopyalar. Hedef proje yolu otomatik olarak Godot'a sorularak bulunur (GODOT_PROJECT_PATH env veya manuel yol fallback). Godot'un yeniden açılması veya reimport gerektirebilir.")]
    public async Task<string> SyncAddon(
        [Description("Git pull yapılsın mı? (varsayılan true)")] bool gitPull = true,
        [Description("Hedef Godot proje yolu (verilirse otomatik tespiti geçer; varsayılan: Godot'a sor)")] string? targetProjectPath = null)
    {
        try
        {
            return await Task.Run(async () =>
            {
                string? pullWarning = null;
                var repoAddon = FindAddonSourceDir();
                if (repoAddon is null)
                    return "[GodotMCP] Addon kaynağı bulunamadı. Kurulum dizininde addons/godot_mcp olmalı.";

                if (gitPull)
                {
                    var pulled = GitPull(FindRepoRoot(repoAddon) ?? repoAddon);
                    if (!pulled.success)
                    {
                        // Git yoksa gömülü addon kaynağından devam et (kullanıcı PC'sinde git olmayabilir)
                        pullWarning = $"[GodotMCP] Uyarı: git pull yapılamadı ({pulled.error}). Gömülü addon kaynağı kullanılacak.";
                    }
                }

                var target = await ResolveTarget(targetProjectPath);
                if (target is null)
                    return "[GodotMCP] Hedef Godot proje yolu belirlenemedi. Godot açıkken 'update_addon_push' kullanın veya GODOT_PROJECT_PATH env değişkenini ayarlayın.";

                var dest = Path.Combine(target, "addons", "godot_mcp");
                CopyDirectory(repoAddon, dest);

                return $"[GodotMCP] Addon güncellendi: {dest}\n" +
                   $"{(pullWarning != null ? pullWarning + "\n" : "")}" +
                   $"Git pull: {(gitPull ? "evet" : "hayır")}\n" +
                   $"Godot'da Project > Reload Current Project veya editor'ü yeniden açın.";
            });
        }
        catch (Exception ex)
        {
            return $"[GodotMCP] Addon senkron hatası: {ex.Message}";
        }
    }

    /// <summary>
    /// Her Godot bağlantısında çağrılır. Versiyon kontrolü yapar:
    /// Godot'daki addon versiyonu server'ın gönderdiğiyle aynıysa hiçbir
    /// dosya gönderilmez (binlerce kullanıcıda her bağlantıda ~50 dosya
    /// push'lanmaz). Farklıysa tam güncelleme push'lanır.
    /// </summary>
    public async Task AutoUpdateAsync()
    {
        try
        {
            var addonDir = FindAddonSourceDir();
            if (addonDir is null)
            {
                _logger.LogWarning("[GodotMCP] Auto-update: addon kaynağı bulunamadı (kurulum dizininde addons/godot_mcp yok).");
                return;
            }

            // 1) Godot'daki addon versiyonunu sor
            var shippedVersion = ReadShippedAddonVersion(addonDir);
            string? godotVersion = null;
            try
            {
                var versionResp = await bridge.SendAsync("get_version", timeoutSeconds: 5);
                if (versionResp.Success && versionResp.Result is JsonElement ve &&
                    ve.TryGetProperty("plugin_version", out var pv))
                    godotVersion = pv.GetString();
            }
            catch { /* eski addon'da get_version yoksa godotVersion null kalır → push edilir */ }

            // 2) Versiyonlar eşitse atla
            if (shippedVersion is not null && godotVersion is not null &&
                string.Equals(shippedVersion, godotVersion, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("[GodotMCP] Addon güncel (v{Version}), push atlandı.", shippedVersion);
                return;
            }

            _logger.LogInformation("[GodotMCP] Addon güncelleniyor: Godot v{Godot} → v{Shipped}",
                godotVersion ?? "?", shippedVersion ?? "?");
            await PushAddonFilesAsync(addonDir);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("[GodotMCP] Auto-update hata: {Message}", ex.Message);
        }
    }

    /// <summary>Addon dosyalarını Godot'a gönderir (git pull + push akışı).</summary>
    private async Task PushAddonFilesAsync(string addonDir)
    {
        // Geliştirici ortamındaysak git pull ile en güncel halini al
        var repoRoot = FindRepoRoot(addonDir);
        if (repoRoot is not null)
            GitPull(repoRoot);

        var files = new List<Dictionary<string, string>>();
        foreach (var file in Directory.EnumerateFiles(addonDir, "*", SearchOption.AllDirectories))
        {
            // .uid dosyalarini disarida birak (Godot otomatik uretir, carpisma yaratir)
            if (file.EndsWith(".uid", StringComparison.OrdinalIgnoreCase))
                continue;
            var rel = Path.GetRelativePath(addonDir, file).Replace('\\', '/');
            files.Add(new Dictionary<string, string>
            {
                ["path"] = rel,
                ["content"] = await File.ReadAllTextAsync(file)
            });
        }

        var result = await bridge.SendAsync("update_addon_push", new()
        {
            ["files"] = files,
            ["base_path"] = "res://addons/godot_mcp"
        }, timeoutSeconds: 60);

        if (result.Success)
            _logger.LogInformation("[GodotMCP] Auto-update tamamlandı ({Count} dosya).", files.Count);
        else
            _logger.LogWarning("[GodotMCP] Auto-update başarısız: {Error}", result.Error);
    }

    /// <summary>Addon dizininden yukarı çıkarak .git içeren repo kökünü bulur (yoksa null).</summary>
    private static string? FindRepoRoot(string addonDir)
    {
        var dir = new DirectoryInfo(addonDir);
        for (int i = 0; i < 6 && dir is not null; i++, dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, ".git")))
                return dir.FullName;
        return null;
    }

    [McpServerTool(Name = "update_addon_push")]
    [Description("Repo'daki en güncel addon dosyalarını Godot'a canlı olarak gönderir; Godot dosyaları diske yazar ve reimport eder. Godot açıkken bile güncelleme yapılır.")]
    public async Task<string> UpdateAddonPush(
        [Description("Git pull yapılsın mı? (varsayılan true, sadece geliştirici ortamında etkili)")] bool gitPull = true)
    {
        try
        {
            string? pullWarning = null;
            var addonDir = FindAddonSourceDir();
            if (addonDir is null)
                return "[GodotMCP] Addon kaynağı bulunamadı. Kurulum dizininde addons/godot_mcp olmalı.";

            if (gitPull)
            {
                var repoRoot = FindRepoRoot(addonDir);
                if (repoRoot is not null)
                {
                    var pulled = GitPull(repoRoot);
                    if (!pulled.success)
                        pullWarning = $"[GodotMCP] Uyarı: git pull yapılamadı ({pulled.error}). Gömülü addon kaynağı kullanılacak.";
                }
            }

            var files = new List<Dictionary<string, string>>();
            foreach (var file in Directory.EnumerateFiles(addonDir, "*", SearchOption.AllDirectories))
            {
                // .uid dosyalarini disarida birak (Godot otomatik uretir, carpisma yaratir)
                if (file.EndsWith(".uid", StringComparison.OrdinalIgnoreCase))
                    continue;
                var rel = Path.GetRelativePath(addonDir, file).Replace('\\', '/');
                files.Add(new Dictionary<string, string>
                {
                    ["path"] = rel,
                    ["content"] = await File.ReadAllTextAsync(file)
                });
            }

            var result = await bridge.SendAsync("update_addon_push", new()
            {
                ["files"] = files,
                ["base_path"] = "res://addons/godot_mcp"
            }, timeoutSeconds: 60);

            return result.Success
                ? $"[GodotMCP] Addon Godot'a gönderildi ({files.Count} dosya).{(pullWarning != null ? " " + pullWarning : "")} Godot reimport yaptı."
                : $"[GodotMCP Hata] {result.Error}";
        }
        catch (TimeoutException) { return "[GodotMCP] Godot yanıt vermedi."; }
        catch (Exception ex) { return $"[GodotMCP] Addon push hatası: {ex.Message}"; }
    }

    private static (bool success, string error) GitPull(string repoRoot)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = "pull --ff-only",
                WorkingDirectory = repoRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            using var proc = Process.Start(psi);
            if (proc is null) return (false, "git başlatılamadı.");
            var err = proc.StandardError.ReadToEnd();
            proc.WaitForExit();
            if (proc.ExitCode != 0)
                return (false, err.Trim());
            return (true, "");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    private async Task<string?> ResolveTarget(string? overridePath)
    {
        // 1. Açıkça verilen yol (en yüksek öncelik)
        if (!string.IsNullOrEmpty(overridePath) && Directory.Exists(overridePath))
            return Path.GetFullPath(overridePath);

        // 2. Godot'a sor (çalışan editor'den proje yolunu al) — binlerce kullanıcı için sıfır config
        try
        {
            if (bridge.IsConnected)
            {
                var resp = await bridge.SendAsync("editor_get_project_path");
                if (resp.Success && resp.Result is JsonElement je && je.TryGetProperty("path", out var p))
                {
                    var godotPath = p.GetString();
                    if (!string.IsNullOrEmpty(godotPath) && Directory.Exists(godotPath))
                        return Path.GetFullPath(godotPath);
                }
            }
        }
        catch { /* Godot'a sorulamadıysa fallback'e düş */ }

        // 3. Env değişkeni (opsiyonel manuel override)
        var env = Environment.GetEnvironmentVariable("GODOT_PROJECT_PATH");
        if (!string.IsNullOrEmpty(env) && Directory.Exists(env))
            return Path.GetFullPath(env);

        return null;
    }

    private static void CopyDirectory(string source, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(dir.Replace(source, dest));
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            if (file.EndsWith(".uid", StringComparison.OrdinalIgnoreCase))
                continue;
            var target = file.Replace(source, dest);
            File.Copy(file, target, overwrite: true);
        }
    }
}
