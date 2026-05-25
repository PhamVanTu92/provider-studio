using System.Collections.Concurrent;
using System.Text.Json;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Logging;
using ProviderStudio.Query;
using ReportingPlatform.Provider.V1;

namespace ProviderStudio.Runtime;

/// <summary>
/// Manages a single provider's gRPC bidirectional stream.
/// Mirrors excel-provider's ProviderBridgeClient but driven by dynamic config.
/// </summary>
public sealed class ProviderSession : IAsyncDisposable
{
    private static readonly int[] BackoffMs = [5_000, 15_000, 30_000, 60_000, 120_000];

    private readonly ProviderRuntimeConfig    _cfg;
    private readonly IHttpClientFactory       _httpFactory;
    private readonly ILogger<ProviderSession> _logger;

    private readonly TokenCache _tokenCache;
    private readonly RuntimeStatus _status;
    private readonly CancellationTokenSource _cts = new();

    // In-flight request tracking
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _inflight = new();
    private volatile int  _inflightCount;
    private readonly SemaphoreSlim _drainSignal = new(0, int.MaxValue);

    public RuntimeStatus Status => _status;
    public Task? RunTask { get; private set; }

    public ProviderSession(
        ProviderRuntimeConfig config,
        IHttpClientFactory    httpFactory,
        ILogger<ProviderSession> logger)
    {
        _cfg         = config;
        _httpFactory = httpFactory;
        _logger      = logger;

        _status = new RuntimeStatus
        {
            ProviderId   = config.ProviderId,
            ProviderName = config.ProviderName,
            State        = SessionState.Stopped,
        };

        _tokenCache = new TokenCache(
            httpFactory,
            config.TokenEndpoint,
            config.ClientId,
            config.ClientSecretPlain,
            logger);
    }

    public void Start()
    {
        _status.State = SessionState.Connecting;
        RunTask = RunLoopAsync(_cts.Token);
    }

    public async Task StopAsync()
    {
        await _cts.CancelAsync();
        if (RunTask is not null)
            try { await RunTask; } catch { /* swallow */ }
        _status.State = SessionState.Stopped;
    }

    public ValueTask DisposeAsync() => new(StopAsync());

    // ─── Connect loop ─────────────────────────────────────────────────────────

    private async Task RunLoopAsync(CancellationToken ct)
    {
        int attempt = 0;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                _status.State            = SessionState.Connecting;
                _status.ReconnectAttempt = attempt;

                var token = await _tokenCache.GetTokenAsync(ct);
                bool ok   = await ConnectAndServeAsync(token, ct);
                if (ok) attempt = 0;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                _status.State     = SessionState.Error;
                _status.LastError = ex.Message;
                var delay = TimeSpan.FromMilliseconds(BackoffMs[Math.Min(attempt, BackoffMs.Length - 1)]);
                _logger.LogWarning(ex,
                    "[{Id}] Attempt {N} failed; reconnecting in {D}ms",
                    _cfg.ProviderId, ++attempt, delay.TotalMilliseconds);
                _status.State            = SessionState.Backoff;
                _status.ReconnectAttempt = attempt;
                try { await Task.Delay(delay, ct); } catch (OperationCanceledException) { break; }
            }
        }
        _status.State = SessionState.Stopped;
        _logger.LogInformation("[{Id}] ProviderSession stopped", _cfg.ProviderId);
    }

    // ─── Connect + Serve ──────────────────────────────────────────────────────

    private async Task<bool> ConnectAndServeAsync(string token, CancellationToken ct)
    {
        _logger.LogInformation("[{Id}] Connecting to {Url}", _cfg.ProviderId, _cfg.BridgeGrpcUrl);

        using var channel = GrpcChannel.ForAddress(_cfg.BridgeGrpcUrl, new GrpcChannelOptions
        {
            HttpHandler = new SocketsHttpHandler
            {
                EnableMultipleHttp2Connections = true,
                KeepAlivePingDelay             = TimeSpan.FromSeconds(30),
                KeepAlivePingTimeout           = TimeSpan.FromSeconds(10),
            }
        });

        var client  = new OperationProvider.OperationProviderClient(channel);
        var headers = new Metadata { { "authorization", $"Bearer {token}" } };

        using var stream = client.Connect(headers, cancellationToken: ct);

        // Send Hello
        var hello = new Hello { ProviderId = _cfg.ProviderId, Version = _cfg.Version };
        foreach (var op in _cfg.Operations.Where(o => o.Mode == OperationMode.Get))
            hello.SupportedOperations.Add(op.Pattern);
        hello.Metadata["instanceId"] = Environment.MachineName;
        hello.Metadata["language"]   = "dotnet9";

        await stream.RequestStream.WriteAsync(new FromProvider { Hello = hello }, ct);
        _logger.LogInformation("[{Id}] Hello sent ({Count} operations)", _cfg.ProviderId,
            hello.SupportedOperations.Count);

        // Await Welcome (5s)
        using var welcomeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        welcomeCts.CancelAfter(TimeSpan.FromSeconds(5));

        Welcome? welcome = null;
        try
        {
            await foreach (var msg in stream.ResponseStream.ReadAllAsync(welcomeCts.Token))
            {
                if (msg.MessageCase == ToProvider.MessageOneofCase.Welcome)  { welcome = msg.Welcome; break; }
                if (msg.MessageCase == ToProvider.MessageOneofCase.Disconnect)
                {
                    _logger.LogWarning("[{Id}] Disconnect before Welcome: {R}", _cfg.ProviderId, msg.Disconnect.Reason);
                    return false;
                }
            }
        }
        catch (OperationCanceledException) when (welcomeCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            throw new TimeoutException("Timed out waiting for Welcome (5s)");
        }

        if (welcome is null) return false;

        _status.State       = SessionState.Connected;
        _status.SessionId   = welcome.SessionId;
        _status.ConnectedAt = DateTimeOffset.UtcNow;
        _status.LastError   = null;

        _logger.LogInformation("[{Id}] Connected — sessionId={Sid}", _cfg.ProviderId, welcome.SessionId);

        await ServeAsync(stream, welcome, ct);
        await channel.ShutdownAsync();
        return true;
    }

    // ─── Serve ────────────────────────────────────────────────────────────────

    private async Task ServeAsync(
        AsyncDuplexStreamingCall<FromProvider, ToProvider> stream,
        Welcome welcome,
        CancellationToken ct)
    {
        var writeLock = new SemaphoreSlim(1, 1);
        int hbInterval = welcome.HeartbeatIntervalSeconds > 0 ? welcome.HeartbeatIntervalSeconds : 30;

        using var hbCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var hbTask = RunHeartbeatAsync(stream.RequestStream, writeLock, hbInterval, hbCts.Token);

        bool refreshRequested = false;
        try
        {
            await foreach (var msg in stream.ResponseStream.ReadAllAsync(ct))
            {
                switch (msg.MessageCase)
                {
                    case ToProvider.MessageOneofCase.Request:
                        _status.LastHeartbeatAt = DateTimeOffset.UtcNow;
                        DispatchFireAndForget(msg.Request, stream.RequestStream, writeLock, ct);
                        break;

                    case ToProvider.MessageOneofCase.Cancel:
                        CancelRequest(msg.Cancel.RequestId);
                        break;

                    case ToProvider.MessageOneofCase.RefreshAuth:
                        refreshRequested = true;
                        goto doneReading;

                    case ToProvider.MessageOneofCase.Disconnect:
                        _logger.LogWarning("[{Id}] Disconnect: {R}", _cfg.ProviderId, msg.Disconnect.Reason);
                        goto doneReading;
                }
            }
            doneReading:;
        }
        finally
        {
            hbCts.Cancel();
            try { await hbTask; } catch { }
        }

        if (refreshRequested)
        {
            using var drainCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            drainCts.CancelAfter(TimeSpan.FromSeconds(30));
            try { await WaitDrainAsync(drainCts.Token); }
            catch (OperationCanceledException) when (drainCts.IsCancellationRequested && !ct.IsCancellationRequested)
            { CancelAll(); }

            try { await _tokenCache.AcquireFreshAsync(ct); } catch (Exception ex)
            { _logger.LogWarning(ex, "[{Id}] Pre-fetch token failed", _cfg.ProviderId); }

            try { await stream.RequestStream.CompleteAsync(); } catch { }
        }
    }

    // ─── Dispatch ─────────────────────────────────────────────────────────────

    private void DispatchFireAndForget(
        OperationRequest request,
        IClientStreamWriter<FromProvider> writer,
        SemaphoreSlim writeLock,
        CancellationToken streamCt)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(streamCt);
        _inflight[request.RequestId] = cts;
        Interlocked.Increment(ref _inflightCount);

        _ = Task.Run(async () =>
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Terminal terminal;

            try
            {
                var opCfg = _cfg.Operations.FirstOrDefault(
                    o => o.Pattern == request.Operation && o.Mode == OperationMode.Get);

                if (opCfg is null)
                {
                    terminal = FailTerminal("VALIDATION_ERROR",
                        $"Operation '{request.Operation}' not found", sw.ElapsedMilliseconds);
                }
                else
                {
                    if (request.TimeoutAtUnixMs > 0)
                    {
                        var dl = request.TimeoutAtUnixMs - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                        if (dl > 0) cts.CancelAfter(TimeSpan.FromMilliseconds(dl));
                    }

                    // Report progress
                    Func<int, string, Task> progress = async (pct, msg) =>
                    {
                        if (!request.WantsProgress) return;
                        var chunk = new FromProvider
                        {
                            ResponseChunk = new OperationResponseChunk
                            {
                                RequestId = request.RequestId,
                                Progress  = new Progress { Percent = pct, Message = msg,
                                    TsUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() }
                            }
                        };
                        await writeLock.WaitAsync(cts.Token);
                        try { await writer.WriteAsync(chunk, cts.Token); }
                        finally { writeLock.Release(); }
                    };

                    await progress(10, "Executing query…");

                    // Execute
                    await using var executor = CreateExecutor(opCfg);
                    var json = await executor.ExecuteAsync(
                        opCfg.QueryTarget,
                        opCfg.QueryType,
                        opCfg.ParamMappings,
                        request.ParamsJson,
                        cts.Token);

                    _status.OperationsHandled++;

                    terminal = new Terminal
                    {
                        Status      = ReportingPlatform.Provider.V1.Status.Done,
                        PayloadJson = json,
                        ElapsedMs   = sw.ElapsedMilliseconds,
                    };
                    _logger.LogInformation("[{Id}] {Op} done in {Ms}ms",
                        _cfg.ProviderId, request.Operation, sw.ElapsedMilliseconds);
                }
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested && !streamCt.IsCancellationRequested)
            {
                terminal = new Terminal { Status = ReportingPlatform.Provider.V1.Status.Cancelled, ElapsedMs = sw.ElapsedMilliseconds };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[{Id}] Handler threw for {Op}", _cfg.ProviderId, request.Operation);
                terminal = FailTerminal("INTERNAL_ERROR", ex.Message, sw.ElapsedMilliseconds);
            }
            finally
            {
                _inflight.TryRemove(request.RequestId, out _);
                cts.Dispose();
            }

            // Write terminal
            try
            {
                var chunk = new FromProvider
                {
                    ResponseChunk = new OperationResponseChunk
                    {
                        RequestId = request.RequestId, Terminal = terminal
                    }
                };
                await writeLock.WaitAsync(streamCt);
                try { await writer.WriteAsync(chunk, streamCt); }
                finally { writeLock.Release(); }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[{Id}] Failed to write Terminal", _cfg.ProviderId);
            }
            finally
            {
                if (Interlocked.Decrement(ref _inflightCount) == 0) _drainSignal.Release();
            }
        }, streamCt);
    }

    private IQueryExecutor CreateExecutor(OperationRuntimeConfig op)
    {
        if (op.SourceType.ToLowerInvariant() == "api")
        {
            return new ApiQueryExecutor(
                _httpFactory,
                op.ApiBaseUrl       ?? string.Empty,
                op.ApiAuthType      ?? "none",
                op.ApiAuthValue     ?? string.Empty,
                op.ApiDefaultHeaders ?? "{}");
        }

        return op.DbType.ToLowerInvariant() switch
        {
            "postgresql" => new PostgresQueryExecutor(op.ConnectionString),
            "sqlserver"  => new SqlServerQueryExecutor(op.ConnectionString),
            "mysql"      => new MySqlQueryExecutor(op.ConnectionString),
            _ => throw new NotSupportedException($"DbType '{op.DbType}' not supported")
        };
    }

    private void CancelRequest(string id)
    {
        if (_inflight.TryGetValue(id, out var cts)) cts.Cancel();
    }
    private void CancelAll()
    {
        foreach (var (_, cts) in _inflight) cts.Cancel();
    }
    private async Task WaitDrainAsync(CancellationToken ct)
    {
        if (_inflightCount == 0) return;
        await _drainSignal.WaitAsync(ct);
    }

    // ─── Heartbeat ────────────────────────────────────────────────────────────

    private async Task RunHeartbeatAsync(
        IClientStreamWriter<FromProvider> writer,
        SemaphoreSlim writeLock, int intervalSeconds, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), ct);
                var hb = new FromProvider
                {
                    Heartbeat = new Heartbeat { TsUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() }
                };
                await writeLock.WaitAsync(ct);
                try { await writer.WriteAsync(hb, ct); }
                finally { writeLock.Release(); }
                _status.LastHeartbeatAt = DateTimeOffset.UtcNow;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _logger.LogWarning(ex, "[{Id}] Heartbeat error", _cfg.ProviderId); }
    }

    private static Terminal FailTerminal(string code, string msg, long ms) =>
        new() { Status = ReportingPlatform.Provider.V1.Status.Failed, ElapsedMs = ms,
                Error  = new Error { Code = code, Message = msg } };
}
