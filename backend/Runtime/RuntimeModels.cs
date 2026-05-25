using ProviderStudio.Query;

namespace ProviderStudio.Runtime;

public enum SessionState { Stopped, Connecting, Connected, Error, Backoff }

public sealed class RuntimeStatus
{
    public string      ProviderId      { get; set; } = string.Empty;
    public string      ProviderName    { get; set; } = string.Empty;
    public SessionState State          { get; set; } = SessionState.Stopped;
    public string?     SessionId       { get; set; }
    public DateTimeOffset? ConnectedAt { get; set; }
    public DateTimeOffset? LastHeartbeatAt { get; set; }
    public int         InflightRequests { get; set; }
    public int         ReconnectAttempt { get; set; }
    public string?     LastError       { get; set; }
    public long        OperationsHandled { get; set; }
}

/// <summary>Immutable snapshot of a provider's config loaded at session start.</summary>
public sealed record ProviderRuntimeConfig(
    string   ProviderId,
    string   ProviderName,
    string   ClientId,
    string   ClientSecretPlain,
    string   TokenEndpoint,
    string   BridgeGrpcUrl,
    string   IngestionBaseUrl,
    string   IngestionTokenEndpoint,
    string   Version,
    IReadOnlyList<OperationRuntimeConfig> Operations);

public sealed record OperationRuntimeConfig(
    string   OperationId,
    string   Pattern,
    OperationMode Mode,
    string   DbConnectionId,
    // ── Source type ──────────────────────────────────────────────────────────
    string   SourceType,           // "database" | "api"
    // ── Database source ──────────────────────────────────────────────────────
    string   DbType,
    string   ConnectionString,     // fully built, password decrypted
    // ── API source ───────────────────────────────────────────────────────────
    string?  ApiBaseUrl,
    string?  ApiAuthType,          // none | bearer | apikey | basic
    string?  ApiAuthValue,         // decrypted auth credential
    string?  ApiDefaultHeaders,    // JSON object of extra headers
    // ── Query ────────────────────────────────────────────────────────────────
    QueryType QueryType,
    string   QueryTarget,
    IReadOnlyList<ParamMappingConfig> ParamMappings,
    int      PushPollIntervalSeconds,
    string   PushChangeQuery);
