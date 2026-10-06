using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JackyAIApp.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTwListedCompanyMarket : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Market",
                table: "TwListedCompanies",
                type: "nvarchar(3)",
                maxLength: 3,
                nullable: true);

            // Every existing row has no Market yet, and nothing would fill it until the list's
            // next scheduled refresh, up to 12 hours away. Backdating the refresh time makes the
            // refresher run at startup, right after this migration. One day, not more: rows are
            // only pruned after 30 days unseen.
            migrationBuilder.Sql(
                "UPDATE TwListedCompanies SET LastRefreshedAt = DATEADD(day, -1, SYSUTCDATETIME())");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Market",
                table: "TwListedCompanies");
        }
    }
}
