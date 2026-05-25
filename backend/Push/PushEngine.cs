using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ProviderStudio.Data;
using ProviderStudio.Query;
using ProviderStudio.Runtime;
using ProviderStudio.Shared;

namespace ProviderStudio.Push;

/// <summary>
/// BackgroundService that manages one PushWorker per enabled push operation.
/// Workers poll their respective databases and notify HDOS when data changes.
/// </summary>
public sealed class PushEngine : BackgroundService
{
    private readonly IServiceScopeFactory  _scope;
    private readonly IHttpClientFactory    _httpFactory;
    private readonly ILogger<PushEngine>   _logger;

    private readonly ConcurrentDictionary<string, (Task Task, CancellationTokenSource Cts)> _workers = new();

    public PushEngine(
        IServiceScopeFactory scope,
        IHttpClientFactory   httpFactory,
        ILogger<PushEngine>  logger)
    {
        _scope       = scope;
        _httpFactory = httpFactory;
        _logger      = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("PushEngine starting…");

        using var scope = _scope.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StudioDbContext>();

        var pushOps = await db.Operations
            .Where(o => o.IsEnabled && o.Mode == "push")
            .Include(o => o.DbConnection)
            .Include(o => o.Provider)
            .Include(o => o.ParamMappings)
            .ToListAsync(stoppingToken);

        foreach (var op in pushOps)
            StartWorker(op);

        _logger.LogInformation("PushEngine started {Count} push workers", _workers.Count);

        await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

        // Shutdown
        foreach (var (_, (_, cts)) in _workers) await cts.CancelAsync();
        await Task.WhenAll(_workers.Values.Select(w => w.Task));
        _logger.LogInformation("PushEngine stopped");
    }

    public void StartWorker(Data.Entities.OperationEntity op)
    {
        if (op.DbConnection is null || op.Provider is null) return;
        if (_workers.ContainsKey(op.Id)) return;

        var cts = new CancellationTokenSource();
        var opCfg = BuildOpConfig(op);
        var ingestion = new IngestionClient(
            _httpFactory,
            op.Provider.IngestionBaseUrl,
            op.Provider.IngestionTokenEndpoint,
            op.Provider.ClientId,
            Encryption.Decrypt(op.Provider.ClientSecretEnc),
            _logger);

        var task = Task.Run(() => RunWorkerAsync(opCfg, ingestion, cts.Token));
        _workers[op.Id] = (task, cts);
    }

    public async Task StopWorkerAsync(string operationId)
    {
        if (_workers.TryRemove(operationId, out var w))
        {
            await w.Cts.CancelAsync();
            try { await w.Task; } catch { }
        }
    }

    public async Task RestartWorkerAsync(Data.Entities.OperationEntity op)
    {
        await StopWorkerAsync(op.Id);
        StartWorker(op);
    }

    // ─── Worker loop ──────────────────────────────────────────────────────────

    private async Task RunWorkerAsync(
        OperationRuntimeConfig opCfg,
        IngestionClient ingestion,
        CancellationToken ct)
    {
        _logger.LogInformation("PushWorker started for {Pattern}", opCfg.Pattern);
        string? lastCheckValue = null;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(opCfg.PushPollIntervalSeconds), ct);

                var (changed, newValue) = await DetectChangeAsync(opCfg, lastCheckValue, ct);
                lastCheckValue = newValue;
                if (changed)
                {
                    _logger.LogInformation("PushWorker: change detected for {Pattern}", opCfg.Pattern);
                    await ingestion.NotifyAsync(new[] { opCfg.Pattern }, ct);
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "PushWorker error for {Pattern}", opCfg.Pattern);
                try { await Task.Delay(TimeSpan.FromSeconds(10), ct); } catch { break; }
            }
        }

        _logger.LogInformation("PushWorker stopped for {Pattern}", opCfg.Pattern);
    }

    private async Task<(bool Changed, string? NewValue)> DetectChangeAsync(
        OperationRuntimeConfig opCfg,
        string? lastCheckValue,
        CancellationToken ct)
    {
        // No change query → always push unconditionally
        if (string.IsNullOrWhiteSpace(opCfg.PushChangeQuery))
            return (true, null);

        IQueryExecutor executor;
        if (opCfg.SourceType.ToLowerInvariant() == "api")
        {
            executor = new ApiQueryExecutor(
                _httpFactory,
                opCfg.ApiBaseUrl       ?? string.Empty,
                opCfg.ApiAuthType      ?? "none",
                opCfg.ApiAuthValue     ?? string.Empty,
                opCfg.ApiDefaultHeaders ?? "{}");
        }
        else
        {
            executor = opCfg.DbType.ToLowerInvariant() switch
            {
                "postgresql" => new PostgresQueryExecutor(opCfg.ConnectionString),
                "sqlserver"  => new SqlServerQueryExecutor(opCfg.ConnectionString),
                "mysql"      => new MySqlQueryExecutor(opCfg.ConnectionString),
                _ => throw new NotSupportedException(opCfg.DbType)
            };
        }

        await using (executor)
        {
            var json = await executor.ExecuteAsync(
                opCfg.PushChangeQuery, QueryType.RawSql,
                Array.Empty<ParamMappingConfig>(), null, ct);

            // First run: set baseline, don't push
            if (lastCheckValue is null) return (false, json);

            bool changed = json != lastCheckValue;
            return (changed, json);
        }
    }

    private static OperationRuntimeConfig BuildOpConfig(Data.Entities.OperationEntity o)
    {
        var conn       = o.DbConnection!;
        var sourceType = conn.SourceType;
        bool isApi     = sourceType.ToLowerInvariant() == "api";

        return new OperationRuntimeConfig(
            OperationId:             o.Id,
            Pattern:                 o.Pattern,
            Mode:                    OperationMode.Push,
            DbConnectionId:          o.DbConnectionId,
            SourceType:              sourceType,
            DbType:                  isApi ? string.Empty : conn.DbType,
            ConnectionString:        isApi ? string.Empty
                                           : QueryExecutorFactory.BuildConnectionString(
                                                 conn, Encryption.Decrypt(conn.PasswordEnc)),
            ApiBaseUrl:              isApi ? conn.ApiBaseUrl  : null,
            ApiAuthType:             isApi ? conn.ApiAuthType : null,
            ApiAuthValue:            isApi && !string.IsNullOrEmpty(conn.ApiAuthHeaderEnc)
                                           ? Encryption.Decrypt(conn.ApiAuthHeaderEnc) : null,
            ApiDefaultHeaders:       isApi ? conn.ApiDefaultHeaders : null,
            QueryType:               QueryType.RawSql,
            QueryTarget:             o.QueryTarget,
            ParamMappings:           [],
            PushPollIntervalSeconds: o.PushPollIntervalSeconds,
            PushChangeQuery:         o.PushChangeQuery);
    }
}
