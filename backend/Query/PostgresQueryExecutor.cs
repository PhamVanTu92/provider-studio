using System.Text;
using System.Text.Json;
using Npgsql;

namespace ProviderStudio.Query;

/// <summary>PostgreSQL query executor using Npgsql.</summary>
public sealed class PostgresQueryExecutor : IQueryExecutor
{
    private readonly string _connectionString;

    public PostgresQueryExecutor(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<string> ExecuteAsync(
        string queryTarget,
        QueryType queryType,
        IReadOnlyList<ParamMappingConfig> paramMappings,
        string? paramsJson,
        CancellationToken ct)
    {
        var sqlParams = ParamMapper.Map(paramsJson, paramMappings);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();

        cmd.CommandText = BuildSql(queryTarget, queryType, sqlParams);
        BindParams(cmd, sqlParams);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await ReadJsonAsync(reader, ct);
    }

    public async Task<string> TestConnectionAsync(CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("SELECT 1", conn);
        await cmd.ExecuteScalarAsync(ct);
        return $"Connected in {sw.ElapsedMilliseconds}ms (PostgreSQL {conn.ServerVersion})";
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // ─── SQL Builder ──────────────────────────────────────────────────────────

    private static string BuildSql(string target, QueryType type, IReadOnlyList<SqlParam> sqlParams)
    {
        return type switch
        {
            QueryType.View       => $"SELECT * FROM {target}",
            QueryType.Function   => BuildFunctionSql(target, sqlParams),
            QueryType.StoredProc => BuildProcSql(target, sqlParams),
            QueryType.RawSql     => target,
            _                    => target,
        };
    }

    private static string BuildFunctionSql(string name, IReadOnlyList<SqlParam> sqlParams)
    {
        var args = string.Join(", ", sqlParams.Select(p => p.Name));
        return $"SELECT * FROM {name}({args})";
    }

    private static string BuildProcSql(string name, IReadOnlyList<SqlParam> sqlParams)
    {
        // PostgreSQL stored procedures use CALL; return void or use INOUT params.
        // For table-valued results use functions instead.
        var args = string.Join(", ", sqlParams.Select(p => p.Name));
        return $"CALL {name}({args})";
    }

    private static void BindParams(NpgsqlCommand cmd, IReadOnlyList<SqlParam> sqlParams)
    {
        foreach (var p in sqlParams)
        {
            var param = cmd.Parameters.AddWithValue(p.Name, p.Value ?? DBNull.Value);

            // Map DateOnly → NpgsqlDbType.Date
            if (p.Value is DateOnly d)
            {
                cmd.Parameters[cmd.Parameters.Count - 1].Value = d;
                cmd.Parameters[cmd.Parameters.Count - 1].NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Date;
            }
        }
    }

    // ─── Result → JSON ────────────────────────────────────────────────────────

    private static async Task<string> ReadJsonAsync(NpgsqlDataReader reader, CancellationToken ct)
    {
        var rows = new List<Dictionary<string, object?>>();

        while (await reader.ReadAsync(ct))
        {
            var row = new Dictionary<string, object?>(reader.FieldCount);
            for (int i = 0; i < reader.FieldCount; i++)
            {
                var name = reader.GetName(i);
                var val  = reader.IsDBNull(i) ? null : reader.GetValue(i);
                row[name] = NormalizeValue(val);
            }
            rows.Add(row);
        }

        return JsonSerializer.Serialize(rows);
    }

    private static object? NormalizeValue(object? val) => val switch
    {
        DateOnly d    => d.ToString("yyyy-MM-dd"),
        DateTime dt   => dt.ToString("O"),
        DateTimeOffset dto => dto.ToString("O"),
        _             => val,
    };
}
