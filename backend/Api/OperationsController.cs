using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProviderStudio.Data;
using ProviderStudio.Data.Entities;
using ProviderStudio.Push;
using ProviderStudio.Query;
using ProviderStudio.Runtime;
using System.Text.Json.Serialization;

namespace ProviderStudio.Api;

// ─── DTOs ─────────────────────────────────────────────────────────────────────

public sealed record ParamMappingDto(
    string Id, string JsonPath, string ParamName,
    string ParamType, bool IsRequired, string DefaultValue, int SortOrder,
    string ApiTarget);

public sealed record OperationDto(
    string Id, string ProviderId, string DbConnectionId,
    string Pattern, string Mode, string QueryType, string QueryTarget,
    int PushPollIntervalSeconds, string PushChangeQuery, bool IsEnabled,
    string CreatedAt, List<ParamMappingDto> ParamMappings);

public sealed record ParamMappingRequest(
    [property: JsonPropertyName("jsonPath")]     string JsonPath,
    [property: JsonPropertyName("paramName")]    string ParamName,
    [property: JsonPropertyName("paramType")]    string ParamType = "string",
    [property: JsonPropertyName("isRequired")]   bool   IsRequired = false,
    [property: JsonPropertyName("defaultValue")] string DefaultValue = "",
    [property: JsonPropertyName("sortOrder")]    int    SortOrder = 0,
    [property: JsonPropertyName("apiTarget")]    string ApiTarget = "query");

public sealed record CreateOperationRequest(
    [property: JsonPropertyName("dbConnectionId")]          string DbConnectionId,
    [property: JsonPropertyName("pattern")]                 string Pattern,
    [property: JsonPropertyName("mode")]                    string Mode,
    [property: JsonPropertyName("queryType")]               string QueryType,
    [property: JsonPropertyName("queryTarget")]             string QueryTarget,
    [property: JsonPropertyName("pushPollIntervalSeconds")] int    PushPollIntervalSeconds = 60,
    [property: JsonPropertyName("pushChangeQuery")]         string PushChangeQuery = "",
    [property: JsonPropertyName("isEnabled")]               bool   IsEnabled = true,
    [property: JsonPropertyName("paramMappings")]           List<ParamMappingRequest>? ParamMappings = null);

public sealed record UpdateOperationRequest(
    [property: JsonPropertyName("dbConnectionId")]          string? DbConnectionId,
    [property: JsonPropertyName("pattern")]                 string? Pattern,
    [property: JsonPropertyName("mode")]                    string? Mode,
    [property: JsonPropertyName("queryType")]               string? QueryType,
    [property: JsonPropertyName("queryTarget")]             string? QueryTarget,
    [property: JsonPropertyName("pushPollIntervalSeconds")] int?    PushPollIntervalSeconds,
    [property: JsonPropertyName("pushChangeQuery")]         string? PushChangeQuery,
    [property: JsonPropertyName("isEnabled")]               bool?   IsEnabled,
    [property: JsonPropertyName("paramMappings")]           List<ParamMappingRequest>? ParamMappings);

public sealed record TestOperationRequest(
    [property: JsonPropertyName("paramsJson")] string? ParamsJson);

// ─── Controller ───────────────────────────────────────────────────────────────

[ApiController, Route("api/providers/{providerId}/operations")]
public sealed class OperationsController : ControllerBase
{
    private readonly StudioDbContext    _db;
    private readonly RuntimeManager     _runtime;
    private readonly PushEngine         _push;
    private readonly IHttpClientFactory _httpFactory;

    public OperationsController(StudioDbContext db, RuntimeManager runtime, PushEngine push,
        IHttpClientFactory httpFactory)
    {
        _db          = db;
        _runtime     = runtime;
        _push        = push;
        _httpFactory = httpFactory;
    }

    [HttpGet]
    public async Task<IActionResult> List(string providerId, CancellationToken ct)
    {
        var ops = await _db.Operations
            .Where(o => o.ProviderId == providerId)
            .Include(o => o.ParamMappings)
            .OrderBy(o => o.Pattern)
            .ToListAsync(ct);
        return Ok(ops.Select(ToDto));
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(string providerId, string id, CancellationToken ct)
    {
        var op = await _db.Operations
            .Include(o => o.ParamMappings)
            .FirstOrDefaultAsync(o => o.Id == id && o.ProviderId == providerId, ct);
        if (op is null) return NotFound();
        return Ok(ToDto(op));
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        string providerId,
        [FromBody] CreateOperationRequest req,
        CancellationToken ct)
    {
        if (!await _db.Providers.AnyAsync(p => p.Id == providerId, ct))
            return NotFound(new { error = "Provider not found." });
        if (!await _db.DbConnections.AnyAsync(c => c.Id == req.DbConnectionId && c.ProviderId == providerId, ct))
            return BadRequest(new { error = "DbConnection not found in this provider." });
        if (await _db.Operations.AnyAsync(o => o.ProviderId == providerId && o.Pattern == req.Pattern, ct))
            return Conflict(new { error = $"Pattern '{req.Pattern}' already exists in this provider." });

        var entity = new OperationEntity
        {
            ProviderId              = providerId,
            DbConnectionId          = req.DbConnectionId,
            Pattern                 = req.Pattern,
            Mode                    = req.Mode.ToLowerInvariant(),
            QueryType               = req.QueryType.ToLowerInvariant(),
            QueryTarget             = req.QueryTarget,
            PushPollIntervalSeconds = req.PushPollIntervalSeconds,
            PushChangeQuery         = req.PushChangeQuery,
            IsEnabled               = req.IsEnabled,
        };

        if (req.ParamMappings is not null)
            entity.ParamMappings = req.ParamMappings.Select(ToParamEntity).ToList();

        _db.Operations.Add(entity);
        await _db.SaveChangesAsync(ct);

        await _runtime.ConfigChangedAsync(providerId, ct);
        if (entity.Mode == "push" && entity.IsEnabled)
        {
            var full = await _db.Operations
                .Include(o => o.DbConnection)
                .Include(o => o.Provider)
                .Include(o => o.ParamMappings)
                .FirstAsync(o => o.Id == entity.Id, ct);
            _push.StartWorker(full);
        }

        return CreatedAtAction(nameof(Get), new { providerId, id = entity.Id }, ToDto(entity));
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(
        string providerId, string id,
        [FromBody] UpdateOperationRequest req,
        CancellationToken ct)
    {
        var op = await _db.Operations
            .Include(o => o.ParamMappings)
            .FirstOrDefaultAsync(o => o.Id == id && o.ProviderId == providerId, ct);
        if (op is null) return NotFound();

        if (req.DbConnectionId         is not null) op.DbConnectionId          = req.DbConnectionId;
        if (req.Pattern                is not null) op.Pattern                 = req.Pattern;
        if (req.Mode                   is not null) op.Mode                    = req.Mode.ToLowerInvariant();
        if (req.QueryType              is not null) op.QueryType               = req.QueryType.ToLowerInvariant();
        if (req.QueryTarget            is not null) op.QueryTarget             = req.QueryTarget;
        if (req.PushPollIntervalSeconds is not null) op.PushPollIntervalSeconds = req.PushPollIntervalSeconds.Value;
        if (req.PushChangeQuery        is not null) op.PushChangeQuery         = req.PushChangeQuery;
        if (req.IsEnabled              is not null) op.IsEnabled               = req.IsEnabled.Value;
        op.UpdatedAt = DateTimeOffset.UtcNow.ToString("O");

        // Replace param mappings if provided
        if (req.ParamMappings is not null)
        {
            _db.ParamMappings.RemoveRange(op.ParamMappings);
            op.ParamMappings = req.ParamMappings.Select(ToParamEntity).ToList();
        }

        await _db.SaveChangesAsync(ct);
        await _runtime.ConfigChangedAsync(providerId, ct);
        await _push.RestartWorkerAsync(op);

        return Ok(ToDto(op));
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string providerId, string id, CancellationToken ct)
    {
        var op = await _db.Operations
            .FirstOrDefaultAsync(o => o.Id == id && o.ProviderId == providerId, ct);
        if (op is null) return NotFound();

        await _push.StopWorkerAsync(id);
        _db.Operations.Remove(op);
        await _db.SaveChangesAsync(ct);
        await _runtime.ConfigChangedAsync(providerId, ct);
        return NoContent();
    }

    [HttpPost("{id}/test")]
    public async Task<IActionResult> Test(
        string providerId, string id,
        [FromBody] TestOperationRequest req,
        CancellationToken ct)
    {
        var op = await _db.Operations
            .Include(o => o.DbConnection)
            .Include(o => o.ParamMappings)
            .FirstOrDefaultAsync(o => o.Id == id && o.ProviderId == providerId, ct);
        if (op is null) return NotFound();
        if (op.DbConnection is null) return BadRequest(new { error = "DB connection not found." });

        try
        {
            await using var executor = QueryExecutorFactory.Create(op.DbConnection, _httpFactory);
            var mappings = op.ParamMappings
                .OrderBy(p => p.SortOrder)
                .Select(p => new ParamMappingConfig(
                    p.JsonPath, p.ParamName, p.ParamType, p.IsRequired, p.DefaultValue, p.ApiTarget))
                .ToList();

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var queryType = op.QueryType.ToLowerInvariant() switch
            {
                "view"       => QueryType.View,
                "storedproc" => QueryType.StoredProc,
                "function"   => QueryType.Function,
                _            => QueryType.RawSql,
            };
            var json = await executor.ExecuteAsync(op.QueryTarget, queryType, mappings, req.ParamsJson, ct);
            return Ok(new { ok = true, elapsedMs = sw.ElapsedMilliseconds, result = json });
        }
        catch (Exception ex)
        {
            return Ok(new { ok = false, error = ex.Message });
        }
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static OperationDto ToDto(OperationEntity o) =>
        new(o.Id, o.ProviderId, o.DbConnectionId, o.Pattern, o.Mode,
            o.QueryType, o.QueryTarget, o.PushPollIntervalSeconds, o.PushChangeQuery,
            o.IsEnabled, o.CreatedAt,
            o.ParamMappings.OrderBy(p => p.SortOrder).Select(ToParamDto).ToList());

    private static ParamMappingDto ToParamDto(ParamMappingEntity p) =>
        new(p.Id, p.JsonPath, p.ParamName, p.ParamType, p.IsRequired, p.DefaultValue, p.SortOrder, p.ApiTarget);

    private static ParamMappingEntity ToParamEntity(ParamMappingRequest r) => new()
    {
        JsonPath     = r.JsonPath,
        ParamName    = r.ParamName,
        ParamType    = r.ParamType,
        IsRequired   = r.IsRequired,
        DefaultValue = r.DefaultValue,
        SortOrder    = r.SortOrder,
        ApiTarget    = r.ApiTarget,
    };
}
