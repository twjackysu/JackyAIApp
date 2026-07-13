using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JackyAIApp.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddListeningResponseTest : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ListeningResponseTestId",
                table: "ExamAttempts",
                type: "nvarchar(450)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ListeningResponseTests",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Prompt = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ListeningResponseTests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ListeningResponseOptions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsCorrect = table.Column<bool>(type: "bit", nullable: false),
                    Explanation = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ListeningResponseTestId = table.Column<string>(type: "nvarchar(450)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ListeningResponseOptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ListeningResponseOptions_ListeningResponseTests_ListeningResponseTestId",
                        column: x => x.ListeningResponseTestId,
                        principalTable: "ListeningResponseTests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExamAttempts_ListeningResponseTestId",
                table: "ExamAttempts",
                column: "ListeningResponseTestId");

            migrationBuilder.CreateIndex(
                name: "IX_ListeningResponseOptions_ListeningResponseTestId",
                table: "ListeningResponseOptions",
                column: "ListeningResponseTestId");

            migrationBuilder.AddForeignKey(
                name: "FK_ExamAttempts_ListeningResponseTests_ListeningResponseTestId",
                table: "ExamAttempts",
                column: "ListeningResponseTestId",
                principalTable: "ListeningResponseTests",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ExamAttempts_ListeningResponseTests_ListeningResponseTestId",
                table: "ExamAttempts");

            migrationBuilder.DropTable(
                name: "ListeningResponseOptions");

            migrationBuilder.DropTable(
                name: "ListeningResponseTests");

            migrationBuilder.DropIndex(
                name: "IX_ExamAttempts_ListeningResponseTestId",
                table: "ExamAttempts");

            migrationBuilder.DropColumn(
                name: "ListeningResponseTestId",
                table: "ExamAttempts");
        }
    }
}
