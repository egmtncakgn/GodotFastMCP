using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.AddConsole(opts =>
    opts.LogToStandardErrorThreshold = LogLevel.Trace);

builder.Services.AddSingleton<GodotBridge>();
builder.Services.AddSingleton<AddonUpdateTools>();
builder.Services.AddSingleton<ServerUpdateManager>();

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

var host = builder.Build();

var logger = host.Services.GetRequiredService<ILogger<Program>>();
var bridge = host.Services.GetRequiredService<GodotBridge>();

// Tek instance garantisi: aynı anda yalnız bir GodotMcpServer.exe çalışır.
var mutex = PortCoordinator.AcquireSingleInstance();
if (mutex is null)
{
    logger.LogWarning("[GodotMCP] Başka bir GodotMcpServer.exe zaten çalışıyor. Bu instance kapatılıyor.");
    return;
}

// Genel hata yakalama
AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
{
    logger.LogCritical("Beklenmeyen hata: {Exception}", e.ExceptionObject);
};

// Önceki oturumda self-update uygulandıysa bildir
ServerUpdateManager.ConsumeUpdateMarker(logger);

// Arka planda server güncellemesi kontrolü (non-blocking, sadece log)
_ = Task.Run(async () =>
{
    try
    {
        var updater = host.Services.GetRequiredService<ServerUpdateManager>();
        var (release, available) = await updater.CheckForUpdateAsync();
        if (available && release?.TagName is not null)
            logger.LogInformation("🆕 Yeni server sürümü mevcut: {Tag} (şu an: v{Current}). " +
                                  "Güncellemek için 'server_self_update' tool'unu kullanın.",
                                  release.TagName, ServerVersion.Current);
    }
    catch { /* arka plan kontrolü sessizce geçer */ }
});

// Başlangıçta Godot'a bağlanmayı dene (sonucu logla)
var addonUpdater = host.Services.GetRequiredService<AddonUpdateTools>();
bridge.OnConnected = addonUpdater.AutoUpdateAsync;

// KRİTİK: Bağlantıyı BEKLEME. Godot kapalıyken port taraması uzun sürebilir;
// await edilirse MCP stdio host'u geç başlar ve istemci initialize yanıtı
// alamadan kilitlenir. Arka planda bağlan, tool çağrıldığında da tetiklenir.
_ = Task.Run(async () =>
{
    try
    {
        await bridge.ConnectAsync(CancellationToken.None);
        if (bridge.IsConnected)
            logger.LogInformation("✅ Godot bağlantısı kuruldu (addon otomatik güncellenecek).");
        else
            logger.LogWarning("⚠️ Godot'a başlangıçta bağlanılamadı. Tool çağrıldıkça otomatik tekrar denenecek.");
    }
    catch (Exception ex)
    {
        logger.LogWarning("⚠️ Godot bağlantı denemesi hata verdi: {Message}", ex.Message);
    }
});

logger.LogInformation("🚀 GodotMcpServer v{Version} başlatıldı. MCP stdio transport dinleniyor...", ServerVersion.Current);

try
{
    await host.RunAsync();
}
catch (Exception ex)
{
    logger.LogCritical("Host hatası: {Message}", ex.Message);
}
finally
{
    logger.LogInformation("👋 GodotMcpServer kapanıyor...");
    await bridge.DisposeAsync();
    if (mutex is not null)
    {
        try { mutex.ReleaseMutex(); } catch { /* farklı thread'den çağrılmışsa yoksay */ }
        mutex.Dispose();
    }
}
