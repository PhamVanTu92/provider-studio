using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace ProviderStudio.Query;

/// <summary>
/// Executes an HTTP API endpoint as a data source.
/// QueryType.View / Function  → HTTP GET  (params placed in path / query string / headers)
/// QueryType.RawSql / StoredProc → HTTP POST (params placed in JSON body / path / headers)
/// The response body is returned as a JSON array string.
/// </summary>
public sealed class ApiQueryExecutor : IQueryExecutor
{
    private readonly HttpClient _http;
    private readonly string     _baseUrl;

    public ApiQueryExecutor(
        IHttpClientFactory httpFactory,
        string baseUrl,
        string authType,
        string authValue,
        string defaultHeaders)
    {
        _http    = httpFactory.CreateClient("api-source");
        _baseUrl = baseUrl.TrimEnd('/');

        // Apply authentication
        switch (authType.ToLowerInvariant())
        {
            case "bearer":
                _http.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", authValue);
                break;
            case "apikey":
                _http.DefaultRequestHeaders.TryAddWithoutValidation("X-Api-Key", authValue);
                break;
            case "basic":
                _http.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Basic", authValue);
                break;
            // "none" → no auth header
        }

        // Apply default headers from JSON
        if (!string.IsNullOrWhiteSpace(defaultHeaders) && defaultHeaders.Trim() != "{}")
        {
            try
            {
                var hdrs = JsonSerializer.Deserialize<Dictionary<string, string>>(defaultHeaders);
                if (hdrs is not null)
                    foreach (var (k, v) in hdrs)
                        _http.DefaultRequestHeaders.TryAddWithoutValidation(k, v);
            }
            catch { /* ignore malformed header JSON */ }
        }
    }

    public async Task<string> ExecuteAsync(
        string queryTarget,
        QueryType queryType,
        IReadOnlyList<ParamMappingConfig> paramMappings,
        string? paramsJson,
        CancellationToken ct)
    {
        // Parse incoming params JSON
        var paramValues = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(paramsJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(paramsJson);
                foreach (var prop in doc.RootElement.EnumerateObject())
                    paramValues[prop.Name] = prop.Value.ValueKind == JsonValueKind.String
                        ? prop.Value.GetString()
                        : prop.Value.ToString();
            }
            catch { /* ignore parse errors */ }
        }

        // Categorize params by ApiTarget
        var pathParams   = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var queryParams  = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var bodyParams   = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var headerParams = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var pm in paramMappings)
        {
            paramValues.TryGetValue(pm.JsonPath, out var rawVal);
            if (rawVal is null && !string.IsNullOrEmpty(pm.DefaultValue))
                rawVal = pm.DefaultValue;
            if (rawVal is null && pm.IsRequired)
                throw new ArgumentException($"Required param '{pm.JsonPath}' is missing");
            if (rawVal is null) continue;

            // Strip SQL @ prefix from param name if present
            var key = pm.ParamName.TrimStart('@');

            switch (pm.ApiTarget.ToLowerInvariant())
            {
                case "path":
                    pathParams[key] = rawVal;
                    break;
                case "body":
                    bodyParams[key] = CoerceValue(rawVal, pm.ParamType);
                    break;
                case "header":
                    headerParams[key] = rawVal;
                    break;
                default: // "query"
                    queryParams[key] = rawVal;
                    break;
            }
        }

        // Build URL (path substitution + query string)
        var endpointPath = queryTarget.TrimStart('/');
        foreach (var (k, v) in pathParams)
            endpointPath = endpointPath.Replace($"{{{k}}}", Uri.EscapeDataString(v));

        var url = $"{_baseUrl}/{endpointPath}";

        if (queryParams.Count > 0)
        {
            var qs = string.Join("&",
                queryParams.Select(kv =>
                    $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
            url = $"{url}?{qs}";
        }

        bool isGet = queryType is QueryType.View or QueryType.Function;

        using var req = new HttpRequestMessage(isGet ? HttpMethod.Get : HttpMethod.Post, url);

        // Per-request headers
        foreach (var (k, v) in headerParams)
            req.Headers.TryAddWithoutValidation(k, v);

        // JSON body for POST
        if (!isGet)
        {
            // If no explicit body params, promote query params to body
            var body = bodyParams.Count > 0
                ? bodyParams
                : queryParams.ToDictionary(kv => kv.Key, kv => (object?)kv.Value);
            req.Content = new StringContent(
                JsonSerializer.Serialize(body),
                Encoding.UTF8, "application/json");
        }

        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseContentRead, ct);
        resp.EnsureSuccessStatusCode();

        var responseBody = (await resp.Content.ReadAsStringAsync(ct)).Trim();

        // Normalise to JSON array
        if (responseBody.StartsWith('['))
            return responseBody;
        if (responseBody.StartsWith('{'))
            return $"[{responseBody}]";

        // Plain text / non-JSON → wrap
        return JsonSerializer.Serialize(new[] { new { result = responseBody } });
    }

    public async Task<string> TestConnectionAsync(CancellationToken ct)
    {
        using var req  = new HttpRequestMessage(HttpMethod.Get, _baseUrl);
        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        return $"HTTP {(int)resp.StatusCode} {resp.ReasonPhrase} — connection OK";
    }

    public ValueTask DisposeAsync()
    {
        _http.Dispose();
        return ValueTask.CompletedTask;
    }

    private static object? CoerceValue(string raw, string paramType) =>
        paramType.ToLowerInvariant() switch
        {
            "int"     => int.TryParse(raw, out var i)     ? (object?)i : raw,
            "decimal" => decimal.TryParse(raw, out var d) ? (object?)d : raw,
            "bool"    => bool.TryParse(raw, out var b)    ? (object?)b : raw,
            _         => raw,
        };
}
