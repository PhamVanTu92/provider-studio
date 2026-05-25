namespace ProviderStudio.Query;

public interface IQueryExecutor : IAsyncDisposable
{
    /// <summary>
    /// Executes the configured query and returns a JSON string (array of objects).
    /// </summary>
    Task<string> ExecuteAsync(
        string queryTarget,
        QueryType queryType,
        IReadOnlyList<ParamMappingConfig> paramMappings,
        string? paramsJson,
        CancellationToken ct);

    /// <summary>Runs a lightweight test — opens connection and pings.</summary>
    Task<string> TestConnectionAsync(CancellationToken ct);
}
