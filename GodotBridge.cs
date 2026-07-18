using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Websocket.Client;

public class GodotBridge : IAsyncDisposable
{
    private const int HealthCheckIntervalMs = 15000;
    private const int MaxReconnectAttempts = 10;
    private const int ConnectVerifyTimeoutMs = 4000;

    private readonly ILogger<GodotBridge> _logger;
    private WebsocketClient? _client;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<GodotResponse>> _pending = new();
    private readonly SemaphoreSlim _connectLock = new(1, 1);
    private readonly CancellationTokenSource _shutdownCts = new();
    private bool _connected;
    private int _reconnectAttempts;
    private bool _healthCheckRunning;

    /// <summary>
    /// Her başarılı (yeniden) bağlantıdan sonra çağrılır (örn. addon auto-update).
    /// Callback kendi içinde versiyon kontrolü yapar; ucuzdur, her bağlantıda
    /// güvenle çağrılabilir. Null ise devre dışı.
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

            // Ortam değişkeni varsa onu kullan (manuel override).
            // Yoksa Godot'un yazdığı lock dosyasından portu oku,
            // o da yoksa tüm çakışmasız aralığı tara.
            var candidatePorts = ResolveCandidatePorts();

            var connectedUrl = await TryConnectAsync(candidatePorts, ct);

            if (connectedUrl is null)
            {
                _connected = false;
                _logger.LogWarning("[GodotBridge] Godot'a bağlanılamadı. Godot açık mı? Eklenti etkin mi? " +
                                   "Port aralığı {Start}-{End} tarandı.", PortCoordinator.PortRangeStart, PortCoordinator.PortRangeEnd);
                // NOT: caller'ın ct'si değil shutdown token kullanılır — tool isteği
                // iptal edildiğinde reconnect döngüsü ölmemeli.
                ScheduleReconnect(_shutdownCts.Token);
                return;
            }

            _logger.LogInformation("[GodotBridge] Bağlandı (TCP): {Url}", connectedUrl);
            _logger.LogInformation("[GodotBridge] ✅ Godot bağlantısı kuruldu");
            _connected = true;
            _reconnectAttempts = 0;
            StartHealthCheck(_shutdownCts.Token);

            // Her bağlantıda addon versiyon kontrolü + gerekiyorsa güncelleme
            // (callback ucuzdur: versiyon eşitse dosya göndermez)
            if (OnConnected is not null)
            {
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
            ScheduleReconnect(_shutdownCts.Token);
        }
        finally
        {
            _connectLock.Release();
        }
    }

    private List<int> ResolveCandidatePorts()
    {
        var ports = new List<int>();
        int? deferredLockPort = null;

        // 1) Manuel env override (GODOT_MCP_WS_URL=ws://127.0.0.1:PORT)
        var env = Environment.GetEnvironmentVariable("GODOT_MCP_WS_URL");
        if (!string.IsNullOrWhiteSpace(env) && Uri.TryCreate(env, UriKind.Absolute, out var uri) && uri.Port > 0)
        {
            ports.Add(uri.Port);
            return ports;
        }

        // 2) Godot'un paylaşılan lock dosyasına yazdığı port (en olası doğru hedef).
        //    Sorun 8 fix: lock artık JSON (port + project_path). GODOT_PROJECT_PATH
        //    ayarlıysa ve lock başka projeye aitse, o portu listenin SONUNA ertele
        //    (birden fazla Godot projesi açıkken yanlış projeye bağlanmayı zorlaştırır).
        var (cached, lockProject) = PortCoordinator.ReadCachedLock();
        if (cached is { } p)
        {
            var expected = Environment.GetEnvironmentVariable("GODOT_PROJECT_PATH");
            if (!string.IsNullOrWhiteSpace(expected) && !string.IsNullOrWhiteSpace(lockProject) &&
                !PathsEqual(expected, lockProject))
            {
                deferredLockPort = p;
                _logger.LogDebug("[GodotBridge] Lock portu {Port} başka projeye ait ({Project}), sona ertelendi.", p, lockProject);
            }
            else
            {
                ports.Add(p);
            }
        }

        // 3) Tüm çakışmasız aralığı tara
        for (int port = PortCoordinator.PortRangeStart; port <= PortCoordinator.PortRangeEnd; port++)
            if (!ports.Contains(port)) ports.Add(port);

        if (deferredLockPort is { } dp && !ports.Contains(dp))
            ports.Add(dp);

        return ports;
    }

    private async Task<string?> TryConnectAsync(List<int> ports, CancellationToken ct)
    {
        // 1) Hızlı TCP taraması: kapalı portlarda WebSocket handshake denemek
        //    pahalıdır (300ms+ bekleme). Connection refused ~1ms döner.
        //    Geniş aralık Godot kapalıyken bile <1 saniyede elenir.
        var openPorts = new List<int>();
        foreach (var port in ports)
        {
            if (ct.IsCancellationRequested) return null;
            if (await IsTcpOpenAsync(port, ct))
                openPorts.Add(port);
        }

        if (openPorts.Count == 0)
        {
            _logger.LogDebug("[GodotBridge] Taranan aralıkta açık port yok (Godot kapalı olabilir).");
            return null;
        }

        // 2) Sadece açık portlarda WS handshake + Godot doğrulaması
        foreach (var port in openPorts)
        {
            var url = $"ws://127.0.0.1:{port}";

            try
            {
                _logger.LogInformation("[GodotBridge] Deneniyor: {Url}", url);

                // Eski client varsa kapat
                if (_client?.IsRunning == true)
                    _ = _client.Stop(System.Net.WebSockets.WebSocketCloseStatus.NormalClosure, "Yeniden bağlanılıyor");

                _client = new WebsocketClient(new Uri(url))
                {
                    ReconnectTimeout = TimeSpan.FromSeconds(10),
                    ErrorReconnectTimeout = TimeSpan.FromSeconds(5),
                    IsReconnectionEnabled = false
                };

                RegisterHandlers(url);

                await _client.Start();
                await Task.Delay(300, ct);

                if (!_client.IsRunning)
                    continue;

                // SADECE handshake yetmez: yanlış Godot/node'a bağlanmamak için
                // Godot'dan gerçek bir "pong" yanıtı gelene kadar bağlantı sayılmaz.
                if (await VerifyGodotConnectionAsync(ct))
                    return url;

                _logger.LogDebug("[GodotBridge] Port {Port} handshake kabul etti ama doğrulama başarısız (ping veya proje eşleşmesi).", port);
                _ = _client.Stop(System.Net.WebSockets.WebSocketCloseStatus.NormalClosure, "Doğrulanamadı");
            }
            catch (Exception ex)
            {
                _logger.LogDebug("[GodotBridge] Port {Port} başarısız: {Message}", port, ex.Message);
            }
        }

        return null;
    }

    /// <summary>TCP seviyesinde hızlı port probe (localhost'ta refused ~1ms).</summary>
    private static async Task<bool> IsTcpOpenAsync(int port, CancellationToken ct)
    {
        using var socket = new System.Net.Sockets.Socket(
            System.Net.Sockets.AddressFamily.InterNetwork,
            System.Net.Sockets.SocketType.Stream,
            System.Net.Sockets.ProtocolType.Tcp);
        try
        {
            await socket.ConnectAsync(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, port), ct);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task<bool> VerifyGodotConnectionAsync(CancellationToken ct)
    {
        try
        {
            var pong = await SendAsync("ping", null, timeoutSeconds: ConnectVerifyTimeoutMs / 1000, ct: ct);
            if (!pong.Success)
                return false;

            // Sorun 8 fix: GODOT_PROJECT_PATH ayarlıysa doğru projeye bağlandığımızı
            // doğrula (aynı anda birden fazla Godot projesi açıksa yanlış editor'a
            // bağlanmayı önler). Komut başarısız olursa (eski addon) doğrulama
            // atlanır ve bağlantı kabul edilir.
            var expected = Environment.GetEnvironmentVariable("GODOT_PROJECT_PATH");
            if (!string.IsNullOrWhiteSpace(expected))
            {
                var pathResp = await SendAsync("editor_get_project_path", null,
                    timeoutSeconds: ConnectVerifyTimeoutMs / 1000, ct: ct);
                if (pathResp.Success && pathResp.Result is JsonElement je &&
                    je.ValueKind == JsonValueKind.Object && je.TryGetProperty("path", out var pp))
                {
                    if (!PathsEqual(expected, pp.GetString()))
                    {
                        _logger.LogWarning(
                            "[GodotBridge] Bağlanılan proje ({Actual}) beklenenden ({Expected}) farklı; sonraki aday denenecek.",
                            pp.GetString(), expected);
                        return false;
                    }
                }
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>İki dosya yolunu normalize edip büyük/küçük harfe duyarsız karşılaştırır.</summary>
    private static bool PathsEqual(string? a, string? b)
    {
        var na = NormalizePath(a);
        var nb = NormalizePath(b);
        return na is not null && nb is not null &&
               string.Equals(na, nb, StringComparison.OrdinalIgnoreCase);
    }

    private static string? NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            return Path.GetFullPath(path.Trim())
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return path.Trim().TrimEnd('\\', '/');
        }
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
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(Math.Max(100, delayMs), ct);
                if (!ct.IsCancellationRequested)
                    await ConnectAsync(ct);
            }
            catch (OperationCanceledException) { /* kapanış */ }
            catch (Exception ex)
            {
                _logger.LogDebug("[GodotBridge] Reconnect denemesi hatası: {Message}", ex.Message);
            }
        }, ct);
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

    public ValueTask DisposeAsync()
    {
        try
        {
            if (_client is not null && _client.IsRunning)
                _ = _client.Stop(System.Net.WebSockets.WebSocketCloseStatus.NormalClosure, "MCP kapanıyor");
        }
        catch { /* yoksay */ }
        finally
        {
            _pending.Clear();
            if (!_shutdownCts.IsCancellationRequested)
                _shutdownCts.Cancel();
            _shutdownCts.Dispose();
        }
        return ValueTask.CompletedTask;
    }
}
