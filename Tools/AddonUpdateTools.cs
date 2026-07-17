using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

[McpServerToolType]
public class AddonUpdateTools(GodotBridge bridge, ILogger<AddonUpdateTools> logger)
{
    private readonly ILogger<AddonUpdateTools> _logger = logger;
    private static readonly string RepoAddonDir =
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "addons", "godot_mcp");

    private static string ResolvedRepoAddonDir =>
        Path.GetFullPath(RepoAddonDir);

    [McpServerTool(Name = "sync_addon")]
    [Description("GodotMCP addon'unu git remote'dan günceller ve Godot projesinin addons/godot_mcp klasörüne kopyalar. Hedef proje yolu otomatik olarak Godot'a sorularak bulunur (GODOT_PROJECT_PATH env veya manuel yol fallback). Godot'un yeniden açılması veya reimport gerektirebilir.")]
    public async Task<string> SyncAddon(
        [Description("Git pull yapılsın mı? (varsayılan true)")] bool gitPull = true,
        [Description("Hedef Godot proje yolu (verilirse otomatik tespiti geçer; varsayılan: Godot'a sor)")] string? targetProjectPath = null)
    {
        try
        {
            return await Task.Run(() =>
            {
                string? pullWarning = null;
                var repoAddon = ResolvedRepoAddonDir;
                if (!Directory.Exists(repoAddon))
                    return $"[GodotMCP] Repo addon klasörü bulunamadı: {repoAddon}";

                if (gitPull)
                {
                    var pulled = GitPull(Path.GetFullPath(Path.Combine(repoAddon, "..", "..")));
                    if (!pulled.success)
                    {
                        // Git yoksa gömülü addon kaynağından devam et (kullanıcı PC'sinde git olmayabilir)
                        pullWarning = $"[GodotMCP] Uyarı: git pull yapılamadı ({pulled.error}). Gömülü addon kaynağı kullanılacak.";
                    }
                }

                var target = ResolveTarget(targetProjectPath).GetAwaiter().GetResult();
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
    /// Bağlantı kurulunca otomatik çağrılır. Godot'a en güncel addon'u canlı gönderir.
    /// Kullanıcı etkileşimi gerektirmez.
    /// </summary>
    public async Task AutoUpdateAsync()
    {
        try
        {
            var repoAddon = ResolvedRepoAddonDir;
            if (!Directory.Exists(repoAddon))
            {
                _logger?.LogWarning("[GodotMCP] Auto-update: addon kaynağı bulunamadı: {Path}", repoAddon);
                return;
            }

            // Git varsa güncelle, yoksa gömülü kaynaktan devam et
            GitPull(Path.GetFullPath(Path.Combine(repoAddon, "..", "..")));

            var files = new List<Dictionary<string, string>>();
            foreach (var file in Directory.EnumerateFiles(repoAddon, "*", SearchOption.AllDirectories))
            {
                // .uid dosyalarini disarida birak (Godot otomatik uretir, carpisma yaratir)
                if (file.EndsWith(".uid", StringComparison.OrdinalIgnoreCase))
                    continue;
                var rel = Path.GetRelativePath(repoAddon, file).Replace('\\', '/');
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
            });

            if (_logger != null)
            {
                if (result.Success)
                    _logger.LogInformation("[GodotMCP] Auto-update tamamlandı ({Count} dosya).", files.Count);
                else
                    _logger.LogWarning("[GodotMCP] Auto-update başarısız: {Error}", result.Error);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning("[GodotMCP] Auto-update hata: {Message}", ex.Message);
        }
    }

    [McpServerTool(Name = "update_addon_push")]
    [Description("Repo'daki en güncel addon dosyalarını Godot'a canlı olarak gönderir; Godot dosyaları diske yazar ve reimport eder. Godot açıkken bile güncelleme yapılır.")]
    public async Task<string> UpdateAddonPush(
        [Description("Git pull yapılsın mı? (varsayılan true)")] bool gitPull = true)
    {
        try
        {
            string? pullWarning = null;
            var repoAddon = ResolvedRepoAddonDir;
            if (!Directory.Exists(repoAddon))
                return $"[GodotMCP] Repo addon klasörü bulunamadı: {repoAddon}";

            if (gitPull)
            {
                var pulled = GitPull(Path.GetFullPath(Path.Combine(repoAddon, "..", "..")));
                if (!pulled.success)
                {
                    // Git yoksa veya hata varsa: gömülü (publish edilmiş) addon kaynağından devam et.
                    // Kullanıcı PC'sinde git olmayabilir; bu kabul edilebilir bir durum.
                    pullWarning = $"[GodotMCP] Uyarı: git pull yapılamadı ({pulled.error}). Gömülü addon kaynağı kullanılacak.";
                }
            }

            var files = new List<Dictionary<string, string>>();
            foreach (var file in Directory.EnumerateFiles(repoAddon, "*", SearchOption.AllDirectories))
            {
                // .uid dosyalarini disarida birak (Godot otomatik uretir, carpisma yaratir)
                if (file.EndsWith(".uid", StringComparison.OrdinalIgnoreCase))
                    continue;
                var rel = Path.GetRelativePath(repoAddon, file).Replace('\\', '/');
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
            });

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
            var target = file.Replace(source, dest);
            File.Copy(file, target, overwrite: true);
        }
    }
}
