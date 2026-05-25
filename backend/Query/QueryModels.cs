namespace ProviderStudio.Query;

public enum QueryType { View, StoredProc, Function, RawSql }
public enum OperationMode { Get, Push }
public enum DbType { PostgreSql, SqlServer, MySql }

public sealed record ParamMappingConfig(
    string JsonPath,
    string ParamName,
    string ParamType,   // string | int | decimal | date | bool
    bool   IsRequired,
    string DefaultValue,
    string ApiTarget = "query");  // API source: query | body | path | header

public sealed record SqlParam(string Name, object? Value);
