using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Websocket.Client;

public class GodotBridge : IAsyncDisposable
{
    private const string DefaultUrl = "ws://127.0.0.1:6505";
    private const int MaxPortScan = 3; // 6505, 6506, 6507
    private const int HealthCheckIntervalMs = 15000;
    private const int MaxReconnectAttempts = 10;

    private readonly ILogger<GodotBridge> _logger;
    private WebsocketClient? _client;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<GodotResponse>> _pending = new();
    private readonly SemaphoreSlim _connectLock = new(1, 1);
    private readonly CancellationTokenSource _shutdownCts = new();
    private bool _connected;
    private int _reconnectAttempts;
    private bool _healthCheckRunning;
    private bool _autoUpdateDone;

    /// <summary>
    /// Her başarılı bağlantıdan sonra çağrılır (örn. addon auto-update).
    /// Null ise devre dışı.
    /// </summary>
    public Func<Task>? OnConnected { get; set; }

    public GodotBridge(ILogger<GodotBridge> logger) => _logger = logger;

    public bool IsConnected => _connected && (_client?.IsRunning ?? false);

    public async Task ConnectAsync(CancellationToken ct)
    {
        await _connectLock.WaitAsync(ct);
        try
        {
            if (IsConnected) return;

            // Önce environment'tan URL al, yoksa port taraması yap
            var baseUrl = Environment.GetEnvironmentVariable("GODOT_MCP_WS_URL") ?? DefaultUrl;
            var connectedUrl = await TryConnectWithPortScanAsync(baseUrl, ct);

            if (connectedUrl is null)
            {
                _connected = false;
                _logger.LogWarning("[GodotBridge] Godot'a bağlanılamadı. Godot açık mı? Eklenti etkin mi? " +
                                   "Port taraması 6505-650{MaxPort} başarısız.", MaxPortScan);
                ScheduleReconnect(ct);
                return;
            }

            _logger.LogInformation("[GodotBridge] Bağlandı (TCP): {Url}", connectedUrl);
            _logger.LogInformation("[GodotBridge] ✅ Godot bağlantısı kuruldu");
            _connected = true;
            _reconnectAttempts = 0;
            StartHealthCheck(ct);

            // İlk bağlantıda otomatik addon güncelleme (sıfır manuel kurulum)
            if (OnConnected is not null && !_autoUpdateDone)
            {
                _autoUpdateDone = true;
                _ = Task.Run(async () =>
                {
                    try { await OnConnected.Invoke(); }
                    catch (Exception ex) { _logger.LogWarning("[GodotBridge] Auto-update hatası: {Message}", ex.Message); }
                });
            }
        }
        catch (Exception ex)
        {
            _connected = false;
            _logger.LogWarning("[GodotBridge] Bağlantı hatası: {Message}", ex.Message);
            ScheduleReconnect(ct);
        }
        finally
        {
            _connectLock.Release();
        }
    }

    private async Task<string?> TryConnectWithPortScanAsync(string baseUrl, CancellationToken ct)
    {
        var uri = new Uri(baseUrl);
        var host = uri.Host;
        var startPort = uri.Port;

        for (int i = 0; i < MaxPortScan; i++)
        {
            var port = startPort + i;
            var url = $"ws://127.0.0.1:{port}";

            try
            {
                _logger.LogInformation("[GodotBridge] Deneniyor: {Url}", url);

                // Eski client varsa kapat
                if (_client?.IsRunning == true)
                    _client.Stop(System.Net.WebSockets.WebSocketCloseStatus.NormalClosure, "Yeniden bağlanılıyor");

                _client = new WebsocketClient(new Uri(url))
                {
                    ReconnectTimeout = TimeSpan.FromSeconds(10),
                    ErrorReconnectTimeout = TimeSpan.FromSeconds(5),
                    IsReconnectionEnabled = false
                };

                RegisterHandlers(url);

                await _client.Start();

                // Gerçek handshake'in tamamlanmasını bekle
                await Task.Delay(500, ct);

                if (_client.IsRunning)
                    return url;
            }
            catch (Exception ex)
            {
                _logger.LogDebug("[GodotBridge] Port {Port} başarısız: {Message}", port, ex.Message);
            }
        }

        return null;
    }

    private void RegisterHandlers(string url)
    {
        _client!.MessageReceived.Subscribe(msg =>
        {
            if (msg.Text is null) return;
            try
            {
                var response = JsonSerializer.Deserialize<GodotResponse>(msg.Text);
                if (response?.Id is not null && _pending.TryRemove(response.Id, out var tcs))
                    tcs.TrySetResult(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[GodotBridge] Mesaj ayrıştırılamadı: {Msg}", msg.Text);
            }
        });

        _client.DisconnectionHappened.Subscribe(info =>
        {
            if (_connected)
            {
                _connected = false;
                var delay = CalculateBackoff();
                _logger.LogWarning("[GodotBridge] Bağlantı koptu ({Type}). {Delay}ms sonra tekrar denenecek...",
                    info.Type, delay);
                ScheduleReconnect(_shutdownCts.Token, delay);
            }
        });

        _client.ReconnectionHappened.Subscribe(info =>
        {
            _connected = true;
            _logger.LogInformation("[GodotBridge] Yeniden bağlanıldı: {Type}", info.Type);
        });
    }

    private int CalculateBackoff()
    {
        _reconnectAttempts = Math.Min(_reconnectAttempts + 1, MaxReconnectAttempts);
        var delay = (int)Math.Pow(2, _reconnectAttempts) * 100; // 200ms, 400ms, 800ms...
        return Math.Min(delay, 30000); // Max 30s
    }

    private void ScheduleReconnect(CancellationToken ct, int delayMs = 0)
    {
        if (ct.IsCancellationRequested) return;
        Task.Delay(Math.Max(100, delayMs), ct)
            .ContinueWith(_ => ConnectAsync(ct).GetAwaiter(), TaskContinuationOptions.OnlyOnRanToCompletion);
    }

    private void StartHealthCheck(CancellationToken ct)
    {
        if (_healthCheckRunning) return;
        _healthCheckRunning = true;

        _ = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested && _connected)
            {
                await Task.Delay(HealthCheckIntervalMs, ct);
                if (!_connected || ct.IsCancellationRequested) break;

                try
                {
                    var pong = await SendAsync("ping", null, timeoutSeconds: 5, ct: ct);
                    if (!pong.Success)
                    {
                        _logger.LogWarning("[GodotBridge] Health check başarısız: {Error}", pong.Error);
                        _connected = false;
                        ScheduleReconnect(ct);
                        break;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("[GodotBridge] Health check hatası: {Message}", ex.Message);
                    _connected = false;
                    ScheduleReconnect(ct);
                    break;
                }
            }
            _healthCheckRunning = false;
        }, ct);
    }

    public async Task<GodotResponse> SendAsync(
        string command,
        Dictionary<string, object?>? parameters = null,
        int timeoutSeconds = 30,
        CancellationToken ct = default)
    {
        if (!IsConnected)
        {
            await ConnectAsync(ct);
            if (!IsConnected)
                return new GodotResponse
                {
                    Success = false,
                    Error = "Godot Editor'e bağlanılamadı. Godot'un açık olduğundan ve " +
                             "GodotMCP eklentisinin etkin olduğundan emin olun."
                };
        }

        var request = new GodotRequest { Command = command, Params = parameters };
        var json = JsonSerializer.Serialize(request);
        var tcs = new TaskCompletionSource<GodotResponse>(TaskCreationOptions.RunContinuationsAsynchronously);

        _pending[request.Id] = tcs;

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        timeoutCts.Token.Register(() =>
        {
            if (_pending.TryRemove(request.Id, out var t))
                t.TrySetException(new TimeoutException($"Godot yanıt vermedi ({timeoutSeconds}s)."));
        });

        _client!.Send(json);

        try
        {
            return await tcs.Task;
        }
        catch (TimeoutException)
        {
            return new GodotResponse
            {
                Success = false,
                Error = $"Godot yanıt vermedi ({timeoutSeconds}s). Komut çok karmaşık olabilir veya Godot meşgul."
            };
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_client is not null && _client.IsRunning)
                _client.Stop(System.Net.WebSockets.WebSocketCloseStatus.NormalClosure, "MCP kapanıyor");
        }
        catch { /* yoksay */ }
        finally
        {
            _pending.Clear();
            if (!_shutdownCts.IsCancellationRequested)
                _shutdownCts.Cancel();
            _shutdownCts.Dispose();
        }
    }
}
