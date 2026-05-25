using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace ProviderStudio.Push;

/// <summary>
/// Pushes datasource.updated events to the HDOS Ingestion API.
/// One instance per provider (configured with provider-specific credentials).
/// </summary>
public sealed class IngestionClient
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly string _ingestionBaseUrl;
    private readonly string _tokenEndpoint;
    private readonly string _clientId;
    private readonly string _clientSecretPlain;
    private readonly ILogger _logger;

    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string?        _cachedToken;
    private DateTimeOffset _tokenExpiresAt;

    public IngestionClient(
        IHttpClientFactory httpFactory,
        string ingestionBaseUrl,
        string tokenEndpoint,
        string clientId,
        string clientSecretPlain,
        ILogger logger)
    {
        _httpFactory       = httpFactory;
        _ingestionBaseUrl  = ingestionBaseUrl;
        _tokenEndpoint     = tokenEndpoint;
        _clientId          = clientId;
        _clientSecretPlain = clientSecretPlain;
        _logger            = logger;
    }

    public async Task NotifyAsync(string[] affectedOperations, CancellationToken ct = default)
    {
        var token = await GetTokenAsync(ct);
        if (token is null)
        {
            _logger.LogWarning("[{Id}] No ingestion token — notification skipped", _clientId);
            return;
        }

        var payload = new IngestionEvent
        {
            EventType  = "datasource.updated",
            OccurredAt = DateTimeOffset.UtcNow,
            Payload    = new IngestionPayload { Source = "provider-studio", AffectedOperations = affectedOperations }
        };

        var endpoint = $"{_ingestionBaseUrl.TrimEnd('/')}/api/v1/events";

        try
        {
            using var http = _httpFactory.CreateClient("ingestion");
            using var req  = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = JsonContent.Create(payload, IngestionJsonCtx.Default.IngestionEvent)
            };
            req.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

            using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);

            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("[{Id}] Ingestion API returned {Status}: {Body}",
                    _clientId, (int)resp.StatusCode, body);
                return;
            }

            _logger.LogInformation("[{Id}] WidgetStale sent — ops=[{Ops}]",
                _clientId, string.Join(", ", affectedOperations));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[{Id}] Failed to notify Ingestion API", _clientId);
        }
    }

    private async Task<string?> GetTokenAsync(CancellationToken ct)
    {
        if (_cachedToken is not null && DateTimeOffset.UtcNow < _tokenExpiresAt)
            return _cachedToken;

        await _tokenLock.WaitAsync(ct);
        try
        {
            if (_cachedToken is not null && DateTimeOffset.UtcNow < _tokenExpiresAt)
                return _cachedToken;

            using var http = _httpFactory.CreateClient("ingestion");
            using var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"]    = "client_credentials",
                ["client_id"]     = _clientId,
                ["client_secret"] = _clientSecretPlain,
                ["scope"]         = "ingestion",
            });

            using var resp = await http.PostAsync(_tokenEndpoint, form, ct);
            if (!resp.IsSuccessStatusCode) return null;

            var tok = await resp.Content.ReadFromJsonAsync(
                IngestionJsonCtx.Default.IngestionTokenResp, ct);
            if (tok?.AccessToken is null) return null;

            _cachedToken    = tok.AccessToken;
            _tokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(tok.ExpiresIn - 60, 30));
            return _cachedToken;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[{Id}] Failed to fetch ingestion token", _clientId);
            return null;
        }
        finally { _tokenLock.Release(); }
    }
}

// ─── DTOs ─────────────────────────────────────────────────────────────────────

internal sealed class IngestionEvent
{
    [JsonPropertyName("eventType")]  public string EventType  { get; set; } = string.Empty;
    [JsonPropertyName("occurredAt")] public DateTimeOffset OccurredAt { get; set; }
    [JsonPropertyName("payload")]    public IngestionPayload Payload   { get; set; } = new();
}
internal sealed class IngestionPayload
{
    [JsonPropertyName("source")]              public string   Source             { get; set; } = string.Empty;
    [JsonPropertyName("affectedOperations")]  public string[] AffectedOperations { get; set; } = [];
}
internal sealed class IngestionTokenResp
{
    [JsonPropertyName("access_token")] public string? AccessToken { get; set; }
    [JsonPropertyName("expires_in")]   public int     ExpiresIn   { get; set; } = 300;
}

[JsonSerializable(typeof(IngestionEvent))]
[JsonSerializable(typeof(IngestionPayload))]
[JsonSerializable(typeof(IngestionTokenResp))]
internal sealed partial class IngestionJsonCtx : JsonSerializerContext { }
