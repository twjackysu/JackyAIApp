using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JackyAIApp.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddListeningTest : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "WordId",
                table: "ExamAttempts",
                type: "nvarchar(450)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");

            migrationBuilder.AddColumn<string>(
                name: "ListeningTestId",
                table: "ExamAttempts",
                type: "nvarchar(450)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ListeningTests",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ImageUrl = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ImagePrompt = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ListeningTests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ListeningTestOptions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsCorrect = table.Column<bool>(type: "bit", nullable: false),
                    Explanation = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ListeningTestId = table.Column<string>(type: "nvarchar(450)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ListeningTestOptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ListeningTestOptions_ListeningTests_ListeningTestId",
                        column: x => x.ListeningTestId,
                        principalTable: "ListeningTests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExamAttempts_ListeningTestId",
                table: "ExamAttempts",
                column: "ListeningTestId");

            migrationBuilder.CreateIndex(
                name: "IX_ListeningTestOptions_ListeningTestId",
                table: "ListeningTestOptions",
                column: "ListeningTestId");

            migrationBuilder.AddForeignKey(
                name: "FK_ExamAttempts_ListeningTests_ListeningTestId",
                table: "ExamAttempts",
                column: "ListeningTestId",
                principalTable: "ListeningTests",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ExamAttempts_ListeningTests_ListeningTestId",
                table: "ExamAttempts");

            migrationBuilder.DropTable(
                name: "ListeningTestOptions");

            migrationBuilder.DropTable(
                name: "ListeningTests");

            migrationBuilder.DropIndex(
                name: "IX_ExamAttempts_ListeningTestId",
                table: "ExamAttempts");

            migrationBuilder.DropColumn(
                name: "ListeningTestId",
                table: "ExamAttempts");

            migrationBuilder.AlterColumn<string>(
                name: "WordId",
                table: "ExamAttempts",
                type: "nvarchar(450)",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(450)",
                oldNullable: true);
        }
    }
}
