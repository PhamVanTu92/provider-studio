using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProviderStudio.Data;
using ProviderStudio.Data.Entities;
using ProviderStudio.Query;
using ProviderStudio.Shared;
using System.Text.Json.Serialization;

namespace ProviderStudio.Api;

// ─── DTOs ─────────────────────────────────────────────────────────────────────

public sealed record DbConnectionDto(
    string Id, string ProviderId, string Name,
    string SourceType,
    // DB source
    string DbType, string Host, int Port, string Database, string Username,
    string ExtraOptions,
    // API source
    string ApiBaseUrl, string ApiAuthType, string ApiDefaultHeaders,
    string CreatedAt);

public sealed record CreateDbConnectionRequest(
    [property: JsonPropertyName("name")]               string Name,
    [property: JsonPropertyName("sourceType")]         string SourceType = "database",
    // DB source fields
    [property: JsonPropertyName("dbType")]             string DbType = "",
    [property: JsonPropertyName("host")]               string Host = "",
    [property: JsonPropertyName("port")]               int    Port = 5432,
    [property: JsonPropertyName("database")]           string Database = "",
    [property: JsonPropertyName("username")]           string Username = "",
    [property: JsonPropertyName("password")]           string Password = "",
    [property: JsonPropertyName("extraOptions")]       string ExtraOptions = "{}",
    // API source fields
    [property: JsonPropertyName("apiBaseUrl")]         string? ApiBaseUrl = null,
    [property: JsonPropertyName("apiAuthType")]        string? ApiAuthType = null,
    [property: JsonPropertyName("apiAuthValue")]       string? ApiAuthValue = null,
    [property: JsonPropertyName("apiDefaultHeaders")]  string? ApiDefaultHeaders = null);

public sealed record UpdateDbConnectionRequest(
    [property: JsonPropertyName("name")]               string? Name,
    // DB source fields
    [property: JsonPropertyName("host")]               string? Host,
    [property: JsonPropertyName("port")]               int?    Port,
    [property: JsonPropertyName("database")]           string? Database,
    [property: JsonPropertyName("username")]           string? Username,
    [property: JsonPropertyName("password")]           string? Password,
    [property: JsonPropertyName("extraOptions")]       string? ExtraOptions,
    // API source fields
    [property: JsonPropertyName("apiBaseUrl")]         string? ApiBaseUrl,
    [property: JsonPropertyName("apiAuthType")]        string? ApiAuthType,
    [property: JsonPropertyName("apiAuthValue")]       string? ApiAuthValue,
    [property: JsonPropertyName("apiDefaultHeaders")]  string? ApiDefaultHeaders);

// ─── Controller ───────────────────────────────────────────────────────────────

[ApiController, Route("api/providers/{providerId}/connections")]
public sealed class DbConnectionsController : ControllerBase
{
    private readonly StudioDbContext   _db;
    private readonly IHttpClientFactory _httpFactory;

    public DbConnectionsController(StudioDbContext db, IHttpClientFactory httpFactory)
    {
        _db          = db;
        _httpFactory = httpFactory;
    }

    [HttpGet]
    public async Task<IActionResult> List(string providerId, CancellationToken ct)
    {
        var list = await _db.DbConnections
            .Where(c => c.ProviderId == providerId)
            .ToListAsync(ct);
        return Ok(list.Select(ToDto));
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(string providerId, string id, CancellationToken ct)
    {
        var c = await _db.DbConnections
            .FirstOrDefaultAsync(c => c.Id == id && c.ProviderId == providerId, ct);
        if (c is null) return NotFound();
        return Ok(ToDto(c));
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        string providerId,
        [FromBody] CreateDbConnectionRequest req,
        CancellationToken ct)
    {
        if (!await _db.Providers.AnyAsync(p => p.Id == providerId, ct))
            return NotFound(new { error = "Provider not found." });

        var sourceType = req.SourceType.ToLowerInvariant();
        bool isApi     = sourceType == "api";

        var entity = new DbConnectionEntity
        {
            ProviderId      = providerId,
            Name            = req.Name,
            SourceType      = sourceType,
            // DB fields
            DbType          = isApi ? string.Empty : req.DbType.ToLowerInvariant(),
            Host            = isApi ? string.Empty : req.Host,
            Port            = isApi ? 0            : req.Port,
            Database        = isApi ? string.Empty : req.Database,
            Username        = isApi ? string.Empty : req.Username,
            PasswordEnc     = isApi ? string.Empty
                                    : (string.IsNullOrEmpty(req.Password) ? string.Empty : Encryption.Encrypt(req.Password)),
            ExtraOptions    = isApi ? "{}" : req.ExtraOptions,
            // API fields
            ApiBaseUrl        = isApi ? (req.ApiBaseUrl       ?? string.Empty) : string.Empty,
            ApiAuthType       = isApi ? (req.ApiAuthType      ?? "none")       : "none",
            ApiAuthHeaderEnc  = isApi && !string.IsNullOrEmpty(req.ApiAuthValue)
                                    ? Encryption.Encrypt(req.ApiAuthValue) : string.Empty,
            ApiDefaultHeaders = isApi ? (req.ApiDefaultHeaders ?? "{}") : "{}",
        };

        _db.DbConnections.Add(entity);
        await _db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(Get), new { providerId, id = entity.Id }, ToDto(entity));
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(
        string providerId, string id,
        [FromBody] UpdateDbConnectionRequest req,
        CancellationToken ct)
    {
        var c = await _db.DbConnections
            .FirstOrDefaultAsync(x => x.Id == id && x.ProviderId == providerId, ct);
        if (c is null) return NotFound();

        if (req.Name         is not null) c.Name         = req.Name;
        // DB fields
        if (req.Host         is not null) c.Host         = req.Host;
        if (req.Port         is not null) c.Port         = req.Port.Value;
        if (req.Database     is not null) c.Database     = req.Database;
        if (req.Username     is not null) c.Username     = req.Username;
        if (req.Password     is not null) c.PasswordEnc  = Encryption.Encrypt(req.Password);
        if (req.ExtraOptions is not null) c.ExtraOptions = req.ExtraOptions;
        // API fields
        if (req.ApiBaseUrl        is not null) c.ApiBaseUrl        = req.ApiBaseUrl;
        if (req.ApiAuthType       is not null) c.ApiAuthType       = req.ApiAuthType;
        if (req.ApiAuthValue      is not null)
            c.ApiAuthHeaderEnc = string.IsNullOrEmpty(req.ApiAuthValue)
                ? string.Empty : Encryption.Encrypt(req.ApiAuthValue);
        if (req.ApiDefaultHeaders is not null) c.ApiDefaultHeaders = req.ApiDefaultHeaders;

        c.UpdatedAt = DateTimeOffset.UtcNow.ToString("O");
        await _db.SaveChangesAsync(ct);
        return Ok(ToDto(c));
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string providerId, string id, CancellationToken ct)
    {
        var c = await _db.DbConnections
            .FirstOrDefaultAsync(x => x.Id == id && x.ProviderId == providerId, ct);
        if (c is null) return NotFound();

        if (await _db.Operations.AnyAsync(o => o.DbConnectionId == id, ct))
            return Conflict(new { error = "Connection is used by one or more operations." });

        _db.DbConnections.Remove(c);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("{id}/test")]
    public async Task<IActionResult> Test(string providerId, string id, CancellationToken ct)
    {
        var c = await _db.DbConnections
            .FirstOrDefaultAsync(x => x.Id == id && x.ProviderId == providerId, ct);
        if (c is null) return NotFound();

        try
        {
            await using var executor = QueryExecutorFactory.Create(c, _httpFactory);
            var result = await executor.TestConnectionAsync(ct);
            return Ok(new { ok = true, message = result });
        }
        catch (Exception ex)
        {
            return Ok(new { ok = false, error = ex.Message });
        }
    }

    private static DbConnectionDto ToDto(DbConnectionEntity c) =>
        new(c.Id, c.ProviderId, c.Name,
            c.SourceType,
            c.DbType, c.Host, c.Port, c.Database, c.Username, c.ExtraOptions,
            c.ApiBaseUrl, c.ApiAuthType, c.ApiDefaultHeaders,
            c.CreatedAt);
}
