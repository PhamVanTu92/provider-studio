using System.ComponentModel.DataAnnotations;

namespace ProviderStudio.Data.Entities;

public sealed class ProviderEntity
{
    [Key] public string Id { get; set; } = Guid.NewGuid().ToString();

    [Required] public string Name                     { get; set; } = string.Empty;
    [Required] public string ClientId                 { get; set; } = string.Empty;
    [Required] public string ClientSecretEnc          { get; set; } = string.Empty; // AES-256
    [Required] public string TokenEndpoint            { get; set; } = string.Empty;
    [Required] public string BridgeGrpcUrl            { get; set; } = string.Empty;
    [Required] public string IngestionBaseUrl         { get; set; } = string.Empty;
    [Required] public string IngestionTokenEndpoint   { get; set; } = string.Empty;
               public string Version                  { get; set; } = "1.0.0";
               public bool   IsEnabled                { get; set; } = true;
               public string CreatedAt                { get; set; } = DateTimeOffset.UtcNow.ToString("O");
               public string UpdatedAt                { get; set; } = DateTimeOffset.UtcNow.ToString("O");

    // Nav
    public List<DbConnectionEntity> DbConnections { get; set; } = [];
    public List<OperationEntity>    Operations     { get; set; } = [];
}
