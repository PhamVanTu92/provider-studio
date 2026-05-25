using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ProviderStudio.Data;
using ProviderStudio.Data.Entities;
using ProviderStudio.Query;
using ProviderStudio.Shared;

namespace ProviderStudio.Runtime;

/// <summary>
/// BackgroundService that manages N ProviderSessions — one per enabled provider.
/// Exposes start/stop/restart and status APIs used by RuntimeController.
/// </summary>
public sealed class RuntimeManager : BackgroundService
{
    private readonly IServiceScopeFactory    _scope;
    private readonly IHttpClientFactory      _httpFactory;
    private readonly ILogger<RuntimeManager> _logger;
    private readonly ILoggerFactory          _loggerFactory;

    private readonly ConcurrentDictionary<string, ProviderSession> _sessions = new();

    public RuntimeManager(
        IServiceScopeFactory      scope,
        IHttpClientFactory        httpFactory,
        ILoggerFactory            loggerFactory,
        ILogger<RuntimeManager>   logger)
    {
        _scope         = scope;
        _httpFactory   = httpFactory;
        _loggerFactory = loggerFactory;
        _logger        = logger;
    }

    // ─── BackgroundService ────────────────────────────────────────────────────

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("RuntimeManager starting — loading providers…");

        using var scope = _scope.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StudioDbContext>();

        var providers = await db.Providers
            .Where(p => p.IsEnabled)
            .Include(p => p.Operations.Where(o => o.IsEnabled))
                .ThenInclude(o => o.DbConnection)
            .Include(p => p.Operations)
                .ThenInclude(o => o.ParamMappings)
            .ToListAsync(stoppingToken);

        foreach (var p in providers)
            StartSession(p);

        _logger.LogInformation("RuntimeManager started {Count} provider sessions", _sessions.Count);

        await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

        // Graceful shutdown
        _logger.LogInformation("RuntimeManager stopping all sessions…");
        var stops = _sessions.Values.Select(s => s.StopAsync()).ToArray();
        await Task.WhenAll(stops);
        _logger.LogInformation("RuntimeManager stopped");
    }

    // ─── Public API ───────────────────────────────────────────────────────────

    public IReadOnlyList<RuntimeStatus> GetAllStatuses() =>
        _sessions.Values.Select(s => s.Status).ToList();

    public RuntimeStatus? GetStatus(string providerId) =>
        _sessions.TryGetValue(providerId, out var s) ? s.Status : null;

    public async Task StartProviderAsync(string providerId, CancellationToken ct = default)
    {
        if (_sessions.ContainsKey(providerId)) return;

        using var scope = _scope.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StudioDbContext>();
        var provider = await LoadProviderAsync(db, providerId, ct)
            ?? throw new KeyNotFoundException($"Provider {providerId} not found or not enabled.");

        StartSession(provider);
    }

    public async Task StopProviderAsync(string providerId)
    {
        if (_sessions.TryRemove(providerId, out var session))
            await session.StopAsync();
    }

    public async Task RestartProviderAsync(string providerId, CancellationToken ct = default)
    {
        await StopProviderAsync(providerId);
        await StartProviderAsync(providerId, ct);
    }

    public async Task ConfigChangedAsync(string providerId, CancellationToken ct = default)
    {
        if (_sessions.ContainsKey(providerId))
            await RestartProviderAsync(providerId, ct);
    }

    // ─── Internals ────────────────────────────────────────────────────────────

    private void StartSession(ProviderEntity provider)
    {
        try
        {
            var config  = BuildConfig(provider);
            var session = new ProviderSession(
                config,
                _httpFactory,
                _loggerFactory.CreateLogger<ProviderSession>());

            session.Start();
            _sessions[provider.Id] = session;
            _logger.LogInformation("Session started for provider {Name} ({Id})",
                provider.Name, provider.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start session for provider {Id}", provider.Id);
        }
    }

    private static async Task<ProviderEntity?> LoadProviderAsync(
        StudioDbContext db, string id, CancellationToken ct)
    {
        return await db.Providers
            .Where(p => p.Id == id && p.IsEnabled)
            .Include(p => p.Operations.Where(o => o.IsEnabled))
                .ThenInclude(o => o.DbConnection)
            .Include(p => p.Operations)
                .ThenInclude(o => o.ParamMappings)
            .FirstOrDefaultAsync(ct);
    }

    private static ProviderRuntimeConfig BuildConfig(ProviderEntity p)
    {
        var ops = p.Operations
            .Where(o => o.IsEnabled && o.DbConnection is not null)
            .Select(o =>
            {
                var conn       = o.DbConnection!;
                var sourceType = conn.SourceType;
                bool isApi     = sourceType.ToLowerInvariant() == "api";

                return new OperationRuntimeConfig(
                    OperationId:             o.Id,
                    Pattern:                 o.Pattern,
                    Mode:                    o.Mode == "push" ? OperationMode.Push : OperationMode.Get,
                    DbConnectionId:          o.DbConnectionId,
                    SourceType:              sourceType,
                    DbType:                  isApi ? string.Empty : conn.DbType,
                    ConnectionString:        isApi ? string.Empty
                                                   : QueryExecutorFactory.BuildConnectionString(
                                                         conn, Encryption.Decrypt(conn.PasswordEnc)),
                    ApiBaseUrl:              isApi ? conn.ApiBaseUrl       : null,
                    ApiAuthType:             isApi ? conn.ApiAuthType      : null,
                    ApiAuthValue:            isApi && !string.IsNullOrEmpty(conn.ApiAuthHeaderEnc)
                                                   ? Encryption.Decrypt(conn.ApiAuthHeaderEnc)
                                                   : null,
                    ApiDefaultHeaders:       isApi ? conn.ApiDefaultHeaders : null,
                    QueryType:               ParseQueryType(o.QueryType),
                    QueryTarget:             o.QueryTarget,
                    ParamMappings:           o.ParamMappings
                                              .OrderBy(pm => pm.SortOrder)
                                              .Select(pm => new ParamMappingConfig(
                                                  pm.JsonPath, pm.ParamName, pm.ParamType,
                                                  pm.IsRequired, pm.DefaultValue, pm.ApiTarget))
                                              .ToList(),
                    PushPollIntervalSeconds: o.PushPollIntervalSeconds,
                    PushChangeQuery:         o.PushChangeQuery);
            })
            .ToList();

        return new ProviderRuntimeConfig(
            ProviderId:             p.Id,
            ProviderName:           p.Name,
            ClientId:               p.ClientId,
            ClientSecretPlain:      Encryption.Decrypt(p.ClientSecretEnc),
            TokenEndpoint:          p.TokenEndpoint,
            BridgeGrpcUrl:          p.BridgeGrpcUrl,
            IngestionBaseUrl:       p.IngestionBaseUrl,
            IngestionTokenEndpoint: p.IngestionTokenEndpoint,
            Version:                p.Version,
            Operations:             ops);
    }

    private static QueryType ParseQueryType(string s) => s.ToLowerInvariant() switch
    {
        "view"       => QueryType.View,
        "storedproc" => QueryType.StoredProc,
        "function"   => QueryType.Function,
        _            => QueryType.RawSql,
    };
}
