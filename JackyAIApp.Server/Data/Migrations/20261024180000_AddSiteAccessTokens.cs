using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JackyAIApp.Server.Data.Migrations
{
    /// <summary>
    /// The personal access tokens that let another AI use the site's tools through the site's own MCP
    /// server (<c>POST /api/mcp</c>): a new table, nothing existing changes. Only a token's hash is kept.
    /// </summary>
    public partial class AddSiteAccessTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SiteAccessTokens",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    TokenHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Hint = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    AllowWrites = table.Column<bool>(type: "bit", nullable: false),
                    ViaOAuth = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastUsedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SiteAccessTokens", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SiteAccessTokens_TokenHash",
                table: "SiteAccessTokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SiteAccessTokens_UserId",
                table: "SiteAccessTokens",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SiteAccessTokens");
        }
    }
}
