using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace ProviderStudio.Runtime;

/// <summary>
/// Per-session JWT cache for a specific provider.
/// Mirrors excel-provider's TokenService but takes credentials via constructor (not IOptions).
/// </summary>
public sealed class TokenCache
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly string _tokenEndpoint;
    private readonly string _clientId;
    private readonly string _clientSecretPlain;
    private readonly ILogger _logger;

    private readonly SemaphoreSlim _lock = new(1, 1);
    private string?        _cached;
    private DateTimeOffset _issuedAt;
    private int            _expiresIn = 900;

    private const int RefreshLeadSeconds = 80;

    public TokenCache(
        IHttpClientFactory httpFactory,
        string tokenEndpoint,
        string clientId,
        string clientSecretPlain,
        ILogger logger)
    {
        _httpFactory       = httpFactory;
        _tokenEndpoint     = tokenEndpoint;
        _clientId          = clientId;
        _clientSecretPlain = clientSecretPlain;
        _logger            = logger;
    }

    public async Task<string> GetTokenAsync(CancellationToken ct = default)
    {
        if (_cached is not null
            && DateTimeOffset.UtcNow < _issuedAt.AddSeconds(_expiresIn - RefreshLeadSeconds))
            return _cached;

        await _lock.WaitAsync(ct);
        try
        {
            if (_cached is not null
                && DateTimeOffset.UtcNow < _issuedAt.AddSeconds(_expiresIn - RefreshLeadSeconds))
                return _cached;
            return await FetchAsync(ct);
        }
        finally { _lock.Release(); }
    }

    public async Task<string> AcquireFreshAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try { return await FetchAsync(ct); }
        finally { _lock.Release(); }
    }

    private async Task<string> FetchAsync(CancellationToken ct)
    {
        _logger.LogInformation("[{ClientId}] Fetching provider token from {Endpoint}",
            _clientId, _tokenEndpoint);

        using var http = _httpFactory.CreateClient("provider-token");
        var req = new TokenRequest
        {
            ClientId     = _clientId,
            ClientSecret = _clientSecretPlain,
            GrantType    = "client_credentials",
        };

        var resp = await http.PostAsJsonAsync(
            _tokenEndpoint, req, TokenJsonCtx.Default.TokenRequest, ct);

        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException(
                $"Token endpoint {_tokenEndpoint} returned {(int)resp.StatusCode}: {body}");
        }

        var tok = await resp.Content.ReadFromJsonAsync(
            TokenJsonCtx.Default.TokenResponse, ct)
            ?? throw new InvalidOperationException("Token endpoint returned null");

        _cached    = tok.AccessToken;
        _issuedAt  = DateTimeOffset.UtcNow;
        _expiresIn = tok.ExpiresIn;

        _logger.LogInformation("[{ClientId}] Token acquired, expires in {ExpiresIn}s",
            _clientId, _expiresIn);
        return _cached;
    }
}

internal sealed class TokenRequest
{
    [JsonPropertyName("clientId")]     public string ClientId     { get; set; } = string.Empty;
    [JsonPropertyName("clientSecret")] public string ClientSecret { get; set; } = string.Empty;
    [JsonPropertyName("grantType")]    public string GrantType    { get; set; } = "client_credentials";
}
internal sealed class TokenResponse
{
    [JsonPropertyName("accessToken")] public string AccessToken { get; set; } = string.Empty;
    [JsonPropertyName("expiresIn")]   public int    ExpiresIn   { get; set; } = 900;
}
[JsonSerializable(typeof(TokenRequest))]
[JsonSerializable(typeof(TokenResponse))]
internal sealed partial class TokenJsonCtx : System.Text.Json.Serialization.JsonSerializerContext { }
