using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProviderStudio.Data.Entities;

public sealed class OperationEntity
{
    [Key] public string Id { get; set; } = Guid.NewGuid().ToString();

    [Required] public string ProviderId            { get; set; } = string.Empty;
    [Required] public string DbConnectionId        { get; set; } = string.Empty;
    /// <summary>e.g. report.sales.daily</summary>
    [Required] public string Pattern               { get; set; } = string.Empty;
    /// <summary>get | push</summary>
    [Required] public string Mode                  { get; set; } = "get";
    /// <summary>view | storedproc | function | rawsql</summary>
    [Required] public string QueryType             { get; set; } = "rawsql";
    /// <summary>View/proc/function name, or raw SQL text</summary>
    [Required] public string QueryTarget           { get; set; } = string.Empty;
               public int    PushPollIntervalSeconds { get; set; } = 60;
               /// <summary>SQL that returns a scalar — used to detect changes for push mode</summary>
               public string PushChangeQuery       { get; set; } = string.Empty;
               public bool   IsEnabled             { get; set; } = true;
               public string CreatedAt             { get; set; } = DateTimeOffset.UtcNow.ToString("O");
               public string UpdatedAt             { get; set; } = DateTimeOffset.UtcNow.ToString("O");

    [ForeignKey(nameof(ProviderId))]
    public ProviderEntity?      Provider      { get; set; }
    [ForeignKey(nameof(DbConnectionId))]
    public DbConnectionEntity?  DbConnection  { get; set; }
    public List<ParamMappingEntity> ParamMappings { get; set; } = [];
}
