using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JackyAIApp.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDailyChallengeSet : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DailyChallengeSets",
                columns: table => new
                {
                    ChallengeDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    QuestionsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailyChallengeSets", x => x.ChallengeDate);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DailyChallengeSets");
        }
    }
}
