using System.Reflection;

/// <summary>
/// Tek versiyon kaynağı.
/// - Server versiyonu: csproj'daki &lt;Version&gt; etiketinden assembly'e gömülür, buradan okunur.
/// - Addon versiyonu: publish çıktısına kopyalanan addons/godot_mcp/plugin.cfg'den okunur.
/// Release workflow'u tag ile csproj versiyonunun eşleştiğini doğrular.
/// </summary>
public static class ServerVersion
{
    /// <summary>Çalışan server'ın versiyonu (örn. "1.2.0").</summary>
    public static string Current { get; } = ReadAssemblyVersion();

    /// <summary>GitHub repository (owner/repo) — güncelleme kontrolü buradan yapılır.</summary>
    public const string GitHubRepo = "egmtncakgn/GodotFastMCP";

    private static string ReadAssemblyVersion()
    {
        var info = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(info))
        {
            // "1.2.0+abc123" gibi suffix'leri temizle
            var plus = info.IndexOfAny(new[] { '+', '-' });
            return plus > 0 ? info[..plus] : info;
        }
        return Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
    }

    /// <summary>
    /// "v1.2.0" / "1.2.0" formatındaki versiyonu parse eder.
    /// </summary>
    public static bool TryParse(string? text, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(text)) return false;
        var clean = text.Trim().TrimStart('v', 'V');
        var dash = clean.IndexOf('-'); // prerelease suffix'ini at
        if (dash > 0) clean = clean[..dash];
        return Version.TryParse(clean, out version!);
    }

    /// <summary>candidate, baseline'dan daha yeni mi?</summary>
    public static bool IsNewer(string candidate, string baseline)
    {
        if (!TryParse(candidate, out var c) || !TryParse(baseline, out var b))
            return false;
        return c > b;
    }
}
