using ProviderStudio.Data.Entities;
using ProviderStudio.Shared;

namespace ProviderStudio.Query;

public static class QueryExecutorFactory
{
    /// <summary>
    /// Returns the correct executor for the given connection.
    /// For API sources, <paramref name="httpFactory"/> must be supplied.
    /// </summary>
    public static IQueryExecutor Create(DbConnectionEntity conn, IHttpClientFactory? httpFactory = null)
    {
        if (conn.SourceType.ToLowerInvariant() == "api")
        {
            if (httpFactory is null)
                throw new InvalidOperationException("IHttpClientFactory is required for API data sources.");
            var authValue = string.IsNullOrEmpty(conn.ApiAuthHeaderEnc)
                ? string.Empty
                : Encryption.Decrypt(conn.ApiAuthHeaderEnc);
            return new ApiQueryExecutor(httpFactory, conn.ApiBaseUrl,
                conn.ApiAuthType, authValue, conn.ApiDefaultHeaders);
        }

        var password = Encryption.Decrypt(conn.PasswordEnc);
        var cs = BuildConnectionString(conn, password);

        return conn.DbType.ToLowerInvariant() switch
        {
            "postgresql" => new PostgresQueryExecutor(cs),
            "sqlserver"  => new SqlServerQueryExecutor(cs),
            "mysql"      => new MySqlQueryExecutor(cs),
            _ => throw new NotSupportedException($"Unsupported DbType: {conn.DbType}")
        };
    }

    public static string BuildConnectionString(DbConnectionEntity conn, string password) =>
        conn.DbType.ToLowerInvariant() switch
        {
            "postgresql" =>
                $"Host={conn.Host};Port={conn.Port};Database={conn.Database};" +
                $"Username={conn.Username};Password={password};",
            "sqlserver" =>
                $"Server={conn.Host},{conn.Port};Database={conn.Database};" +
                $"User Id={conn.Username};Password={password};TrustServerCertificate=True;",
            "mysql" =>
                $"Server={conn.Host};Port={conn.Port};Database={conn.Database};" +
                $"User={conn.Username};Password={password};",
            _ => throw new NotSupportedException($"Unsupported DbType: {conn.DbType}")
        };
}
