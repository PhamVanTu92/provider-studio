using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProviderStudio.Data.Entities;

public sealed class ParamMappingEntity
{
    [Key] public string Id          { get; set; } = Guid.NewGuid().ToString();

    [Required] public string OperationId   { get; set; } = string.Empty;
    /// <summary>Key trong ParamsJson, e.g. "fromDate"</summary>
    [Required] public string JsonPath      { get; set; } = string.Empty;
    /// <summary>SQL parameter name (DB source) or request key (API source), e.g. "@from_date" / "from_date"</summary>
    [Required] public string ParamName     { get; set; } = string.Empty;
    /// <summary>string | int | decimal | date | bool</summary>
    [Required] public string ParamType     { get; set; } = "string";
               public bool   IsRequired    { get; set; } = false;
               public string DefaultValue  { get; set; } = string.Empty;
               public int    SortOrder     { get; set; } = 0;
    /// <summary>For API source: query | body | path | header</summary>
               public string ApiTarget     { get; set; } = "query";

    [ForeignKey(nameof(OperationId))]
    public OperationEntity? Operation { get; set; }
}
