using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JackyAIApp.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUserWordLastReviewedDate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastReviewedDate",
                table: "UserWords",
                type: "datetime2",
                nullable: true);

            // Words reviewed before this column: the last review set NextReviewDate to that day plus
            // ReviewIntervalDays (often fractional, e.g. 13.8), and the interval stays until the next
            // review, so taking it off gives the day back. Half a day is added before dropping the
            // time, so a tick of rounding either way still lands on that day, not the one before.
            // EXEC: in a generated script this shares a batch with the ALTER above, and SQL Server
            // compiles a batch before running it, when the column does not exist yet.
            migrationBuilder.Sql("""
                EXEC('UPDATE UserWords
                SET LastReviewedDate = CAST(DATEADD(minute, 720 - CAST(ROUND(ReviewIntervalDays * 1440, 0) AS int), NextReviewDate) AS date)
                WHERE ReviewCount > 0 AND NextReviewDate IS NOT NULL')
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastReviewedDate",
                table: "UserWords");
        }
    }
}
