using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProviderStudio.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Providers",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    ClientId = table.Column<string>(type: "TEXT", nullable: false),
                    ClientSecretEnc = table.Column<string>(type: "TEXT", nullable: false),
                    TokenEndpoint = table.Column<string>(type: "TEXT", nullable: false),
                    BridgeGrpcUrl = table.Column<string>(type: "TEXT", nullable: false),
                    IngestionBaseUrl = table.Column<string>(type: "TEXT", nullable: false),
                    IngestionTokenEndpoint = table.Column<string>(type: "TEXT", nullable: false),
                    Version = table.Column<string>(type: "TEXT", nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Providers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DbConnections",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    ProviderId = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    SourceType = table.Column<string>(type: "TEXT", nullable: false),
                    DbType = table.Column<string>(type: "TEXT", nullable: false),
                    Host = table.Column<string>(type: "TEXT", nullable: false),
                    Port = table.Column<int>(type: "INTEGER", nullable: false),
                    Database = table.Column<string>(type: "TEXT", nullable: false),
                    Username = table.Column<string>(type: "TEXT", nullable: false),
                    PasswordEnc = table.Column<string>(type: "TEXT", nullable: false),
                    ExtraOptions = table.Column<string>(type: "TEXT", nullable: false),
                    ApiBaseUrl = table.Column<string>(type: "TEXT", nullable: false),
                    ApiAuthType = table.Column<string>(type: "TEXT", nullable: false),
                    ApiAuthHeaderEnc = table.Column<string>(type: "TEXT", nullable: false),
                    ApiDefaultHeaders = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DbConnections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DbConnections_Providers_ProviderId",
                        column: x => x.ProviderId,
                        principalTable: "Providers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Operations",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    ProviderId = table.Column<string>(type: "TEXT", nullable: false),
                    DbConnectionId = table.Column<string>(type: "TEXT", nullable: false),
                    Pattern = table.Column<string>(type: "TEXT", nullable: false),
                    Mode = table.Column<string>(type: "TEXT", nullable: false),
                    QueryType = table.Column<string>(type: "TEXT", nullable: false),
                    QueryTarget = table.Column<string>(type: "TEXT", nullable: false),
                    PushPollIntervalSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    PushChangeQuery = table.Column<string>(type: "TEXT", nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Operations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Operations_DbConnections_DbConnectionId",
                        column: x => x.DbConnectionId,
                        principalTable: "DbConnections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Operations_Providers_ProviderId",
                        column: x => x.ProviderId,
                        principalTable: "Providers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ParamMappings",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    OperationId = table.Column<string>(type: "TEXT", nullable: false),
                    JsonPath = table.Column<string>(type: "TEXT", nullable: false),
                    ParamName = table.Column<string>(type: "TEXT", nullable: false),
                    ParamType = table.Column<string>(type: "TEXT", nullable: false),
                    IsRequired = table.Column<bool>(type: "INTEGER", nullable: false),
                    DefaultValue = table.Column<string>(type: "TEXT", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    ApiTarget = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ParamMappings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ParamMappings_Operations_OperationId",
                        column: x => x.OperationId,
                        principalTable: "Operations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DbConnections_ProviderId",
                table: "DbConnections",
                column: "ProviderId");

            migrationBuilder.CreateIndex(
                name: "IX_Operations_DbConnectionId",
                table: "Operations",
                column: "DbConnectionId");

            migrationBuilder.CreateIndex(
                name: "IX_Operations_ProviderId_Pattern",
                table: "Operations",
                columns: new[] { "ProviderId", "Pattern" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ParamMappings_OperationId_JsonPath",
                table: "ParamMappings",
                columns: new[] { "OperationId", "JsonPath" });

            migrationBuilder.CreateIndex(
                name: "IX_Providers_ClientId",
                table: "Providers",
                column: "ClientId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ParamMappings");

            migrationBuilder.DropTable(
                name: "Operations");

            migrationBuilder.DropTable(
                name: "DbConnections");

            migrationBuilder.DropTable(
                name: "Providers");
        }
    }
}
