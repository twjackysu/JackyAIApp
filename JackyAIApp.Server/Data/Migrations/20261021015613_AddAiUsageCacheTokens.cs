using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JackyAIApp.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAiUsageCacheTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CacheReadTokens",
                table: "AiUsageLogs",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CacheWriteTokens",
                table: "AiUsageLogs",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CacheReadTokens",
                table: "AiUsageLogs");

            migrationBuilder.DropColumn(
                name: "CacheWriteTokens",
                table: "AiUsageLogs");
        }
    }
}
