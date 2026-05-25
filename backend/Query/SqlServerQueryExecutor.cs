using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace ProviderStudio.Query;

/// <summary>SQL Server query executor using Microsoft.Data.SqlClient.</summary>
public sealed class SqlServerQueryExecutor : IQueryExecutor
{
    private readonly string _connectionString;

    public SqlServerQueryExecutor(string connectionString)
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

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();

        (cmd.CommandText, cmd.CommandType) = BuildCommand(queryTarget, queryType);
        BindParams(cmd, sqlParams);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await ReadJsonAsync(reader, ct);
    }

    public async Task<string> TestConnectionAsync(CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        return $"Connected in {sw.ElapsedMilliseconds}ms (SQL Server {conn.ServerVersion})";
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static (string sql, System.Data.CommandType type) BuildCommand(
        string target, QueryType queryType)
    {
        return queryType switch
        {
            QueryType.View       => ($"SELECT * FROM {target}", System.Data.CommandType.Text),
            QueryType.StoredProc => (target, System.Data.CommandType.StoredProcedure),
            QueryType.Function   => ($"SELECT * FROM {target}", System.Data.CommandType.Text),
            QueryType.RawSql     => (target, System.Data.CommandType.Text),
            _                    => (target, System.Data.CommandType.Text),
        };
    }

    private static void BindParams(SqlCommand cmd, IReadOnlyList<SqlParam> sqlParams)
    {
        foreach (var p in sqlParams)
            cmd.Parameters.AddWithValue(p.Name, p.Value ?? DBNull.Value);
    }

    private static async Task<string> ReadJsonAsync(SqlDataReader reader, CancellationToken ct)
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
