using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProviderStudio.Data;
using ProviderStudio.Data.Entities;
using ProviderStudio.Runtime;
using ProviderStudio.Shared;
using System.Text.Json.Serialization;

namespace ProviderStudio.Api;

// ─── DTOs ─────────────────────────────────────────────────────────────────────

public sealed record ProviderDto(
    string Id, string Name, string ClientId,
    string TokenEndpoint, string BridgeGrpcUrl,
    string IngestionBaseUrl, string IngestionTokenEndpoint,
    string Version, bool IsEnabled, string CreatedAt);

public sealed record CreateProviderRequest(
    [property: JsonPropertyName("name")]                   string Name,
    [property: JsonPropertyName("clientId")]               string ClientId,
    [property: JsonPropertyName("clientSecret")]           string ClientSecret,
    [property: JsonPropertyName("tokenEndpoint")]          string TokenEndpoint,
    [property: JsonPropertyName("bridgeGrpcUrl")]          string BridgeGrpcUrl,
    [property: JsonPropertyName("ingestionBaseUrl")]       string IngestionBaseUrl,
    [property: JsonPropertyName("ingestionTokenEndpoint")] string IngestionTokenEndpoint,
    [property: JsonPropertyName("version")]                string Version = "1.0.0",
    [property: JsonPropertyName("isEnabled")]              bool IsEnabled = true);

public sealed record UpdateProviderRequest(
    [property: JsonPropertyName("name")]                   string? Name,
    [property: JsonPropertyName("clientSecret")]           string? ClientSecret,
    [property: JsonPropertyName("tokenEndpoint")]          string? TokenEndpoint,
    [property: JsonPropertyName("bridgeGrpcUrl")]          string? BridgeGrpcUrl,
    [property: JsonPropertyName("ingestionBaseUrl")]       string? IngestionBaseUrl,
    [property: JsonPropertyName("ingestionTokenEndpoint")] string? IngestionTokenEndpoint,
    [property: JsonPropertyName("version")]                string? Version,
    [property: JsonPropertyName("isEnabled")]              bool? IsEnabled);

// ─── Controller ───────────────────────────────────────────────────────────────

[ApiController, Route("api/providers")]
public sealed class ProvidersController : ControllerBase
{
    private readonly StudioDbContext _db;
    private readonly RuntimeManager  _runtime;

    public ProvidersController(StudioDbContext db, RuntimeManager runtime)
    {
        _db      = db;
        _runtime = runtime;
    }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var providers = await _db.Providers.ToListAsync(ct);
        var statuses  = _runtime.GetAllStatuses().ToDictionary(s => s.ProviderId);

        var result = providers.Select(p => new
        {
            Provider = ToDto(p),
            Status   = statuses.TryGetValue(p.Id, out var s) ? s : null
        });
        return Ok(result);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(string id, CancellationToken ct)
    {
        var p = await _db.Providers.FindAsync(new object[] { id }, ct);
        if (p is null) return NotFound();
        return Ok(ToDto(p));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateProviderRequest req, CancellationToken ct)
    {
        if (await _db.Providers.AnyAsync(p => p.ClientId == req.ClientId, ct))
            return Conflict(new { error = $"ClientId '{req.ClientId}' already exists." });

        var entity = new ProviderEntity
        {
            Name                    = req.Name,
            ClientId                = req.ClientId,
            ClientSecretEnc         = Encryption.Encrypt(req.ClientSecret),
            TokenEndpoint           = req.TokenEndpoint,
            BridgeGrpcUrl           = req.BridgeGrpcUrl,
            IngestionBaseUrl        = req.IngestionBaseUrl,
            IngestionTokenEndpoint  = req.IngestionTokenEndpoint,
            Version                 = req.Version,
            IsEnabled               = req.IsEnabled,
        };

        _db.Providers.Add(entity);
        await _db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(Get), new { id = entity.Id }, ToDto(entity));
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] UpdateProviderRequest req, CancellationToken ct)
    {
        var p = await _db.Providers.FindAsync(new object[] { id }, ct);
        if (p is null) return NotFound();

        if (req.Name is not null)                   p.Name                   = req.Name;
        if (req.ClientSecret is not null)           p.ClientSecretEnc        = Encryption.Encrypt(req.ClientSecret);
        if (req.TokenEndpoint is not null)          p.TokenEndpoint          = req.TokenEndpoint;
        if (req.BridgeGrpcUrl is not null)          p.BridgeGrpcUrl          = req.BridgeGrpcUrl;
        if (req.IngestionBaseUrl is not null)       p.IngestionBaseUrl       = req.IngestionBaseUrl;
        if (req.IngestionTokenEndpoint is not null) p.IngestionTokenEndpoint = req.IngestionTokenEndpoint;
        if (req.Version is not null)                p.Version                = req.Version;
        if (req.IsEnabled is not null)              p.IsEnabled              = req.IsEnabled.Value;
        p.UpdatedAt = DateTimeOffset.UtcNow.ToString("O");

        await _db.SaveChangesAsync(ct);
        await _runtime.ConfigChangedAsync(id, ct);
        return Ok(ToDto(p));
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
    {
        var p = await _db.Providers.FindAsync(new object[] { id }, ct);
        if (p is null) return NotFound();

        await _runtime.StopProviderAsync(id);
        _db.Providers.Remove(p);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("{id}/test-auth")]
    public async Task<IActionResult> TestAuth(string id, CancellationToken ct)
    {
        var p = await _db.Providers.FindAsync(new object[] { id }, ct);
        if (p is null) return NotFound();

        try
        {
            using var http   = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var secret       = Encryption.Decrypt(p.ClientSecretEnc);
            using var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["clientId"]     = p.ClientId,
                ["clientSecret"] = secret,
                ["grantType"]    = "client_credentials",
            });
            using var resp = await http.PostAsync(p.TokenEndpoint, form, ct);
            var raw = await resp.Content.ReadAsStringAsync(ct);

            if (!resp.IsSuccessStatusCode)
                return Ok(new { ok = false, status = (int)resp.StatusCode, body = raw });

            return Ok(new { ok = true, status = (int)resp.StatusCode });
        }
        catch (Exception ex)
        {
            return Ok(new { ok = false, error = ex.Message });
        }
    }

    private static ProviderDto ToDto(ProviderEntity p) =>
        new(p.Id, p.Name, p.ClientId, p.TokenEndpoint, p.BridgeGrpcUrl,
            p.IngestionBaseUrl, p.IngestionTokenEndpoint, p.Version, p.IsEnabled, p.CreatedAt);
}
