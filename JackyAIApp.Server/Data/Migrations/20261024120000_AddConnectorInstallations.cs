using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JackyAIApp.Server.Data.Migrations
{
    /// <summary>
    /// The connectors a user lets the AI use (docs/ai-native/design/03 §3): a new table, nothing
    /// existing changes. A catalog entry is added once per user (a filtered unique index leaves out
    /// servers of their own, which may be many).
    /// </summary>
    public partial class AddConnectorInstallations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ConnectorInstallations",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    DefinitionKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ServerUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    AuthKind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    EncryptedSecret = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Enabled = table.Column<bool>(type: "bit", nullable: false),
                    ToolSettingsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ToolsCacheJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ToolsFetchedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastStatus = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    LastError = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    LastTestedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConnectorInstallations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ConnectorInstallations_UserId",
                table: "ConnectorInstallations",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_ConnectorInstallations_UserId_DefinitionKey",
                table: "ConnectorInstallations",
                columns: new[] { "UserId", "DefinitionKey" },
                unique: true,
                filter: "[DefinitionKey] <> 'custom'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConnectorInstallations");
        }
    }
}
