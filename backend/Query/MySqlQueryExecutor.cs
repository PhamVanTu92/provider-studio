using System.Text.Json;
using MySqlConnector;

namespace ProviderStudio.Query;

/// <summary>MySQL / MariaDB query executor using MySqlConnector.</summary>
public sealed class MySqlQueryExecutor : IQueryExecutor
{
    private readonly string _connectionString;

    public MySqlQueryExecutor(string connectionString)
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

        await using var conn = new MySqlConnection(_connectionString);
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
        await using var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        return $"Connected in {sw.ElapsedMilliseconds}ms (MySQL {conn.ServerVersion})";
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static string BuildSql(string target, QueryType queryType, IReadOnlyList<SqlParam> sqlParams)
    {
        return queryType switch
        {
            QueryType.View       => $"SELECT * FROM {target}",
            QueryType.StoredProc => $"CALL {target}({string.Join(", ", sqlParams.Select(p => p.Name))})",
            QueryType.Function   => $"SELECT {target}({string.Join(", ", sqlParams.Select(p => p.Name))})",
            QueryType.RawSql     => target,
            _                    => target,
        };
    }

    private static void BindParams(MySqlCommand cmd, IReadOnlyList<SqlParam> sqlParams)
    {
        foreach (var p in sqlParams)
            cmd.Parameters.AddWithValue(p.Name, p.Value ?? DBNull.Value);
    }

    private static async Task<string> ReadJsonAsync(MySqlDataReader reader, CancellationToken ct)
    {
        var rows = new List<Dictionary<string, object?>>();
        while (await reader.ReadAsync(ct))
        {
            var row = new Dictionary<string, object?>(reader.FieldCount);
            for (int i = 0; i < reader.FieldCount; i++)
                row[reader.GetName(i)] = reader.IsDBNull(i) ? null : NormalizeValue(reader.GetValue(i));
            rows.Add(row);
        }
        return JsonSerializer.Serialize(rows);
    }

    private static object? NormalizeValue(object? val) => val switch
    {
        DateTime dt  => dt.ToString("O"),
        _            => val,
    };
}
