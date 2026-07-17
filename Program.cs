using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.AddConsole(opts =>
    opts.LogToStandardErrorThreshold = LogLevel.Trace);

builder.Services.AddSingleton<GodotBridge>();

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

var host = builder.Build();

var logger = host.Services.GetRequiredService<ILogger<Program>>();
var bridge = host.Services.GetRequiredService<GodotBridge>();

// Genel hata yakalama
AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
{
    logger.LogCritical("Beklenmeyen hata: {Exception}", e.ExceptionObject);
};

// Başlangıçta Godot'a bağlanmayı dene (sonucu logla)
var addonUpdater = host.Services.GetRequiredService<AddonUpdateTools>();
bridge.OnConnected = addonUpdater.AutoUpdateAsync;

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

logger.LogInformation("🚀 GodotMcpServer başlatıldı. MCP stdio transport dinleniyor...");

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
}
