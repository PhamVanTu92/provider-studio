using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProviderStudio.Data.Entities;

public sealed class DbConnectionEntity
{
    [Key] public string Id { get; set; } = Guid.NewGuid().ToString();

    [Required] public string ProviderId    { get; set; } = string.Empty;
    [Required] public string Name          { get; set; } = string.Empty;

    // ── Source type ───────────────────────────────────────────────────────────
    /// <summary>database | api</summary>
    public string SourceType { get; set; } = "database";

    // ── Database source fields (used when SourceType == "database") ───────────
    /// <summary>postgresql | sqlserver | mysql</summary>
               public string DbType        { get; set; } = string.Empty;
               public string Host          { get; set; } = string.Empty;
               public int    Port          { get; set; } = 5432;
               public string Database      { get; set; } = string.Empty;
               public string Username      { get; set; } = string.Empty;
               public string PasswordEnc   { get; set; } = string.Empty; // AES-256
               public string ExtraOptions  { get; set; } = "{}";         // JSON

    // ── API source fields (used when SourceType == "api") ────────────────────
               public string ApiBaseUrl        { get; set; } = string.Empty;
    /// <summary>none | bearer | apikey | basic</summary>
               public string ApiAuthType       { get; set; } = "none";
    /// <summary>AES-256 encrypted auth value (token / key / base64 credentials)</summary>
               public string ApiAuthHeaderEnc  { get; set; } = string.Empty;
    /// <summary>JSON object of extra request headers, e.g. {"X-Tenant":"acme"}</summary>
               public string ApiDefaultHeaders { get; set; } = "{}";

    public string CreatedAt { get; set; } = DateTimeOffset.UtcNow.ToString("O");
    public string UpdatedAt { get; set; } = DateTimeOffset.UtcNow.ToString("O");

    [ForeignKey(nameof(ProviderId))]
    public ProviderEntity? Provider { get; set; }
    public List<OperationEntity> Operations { get; set; } = [];
}
