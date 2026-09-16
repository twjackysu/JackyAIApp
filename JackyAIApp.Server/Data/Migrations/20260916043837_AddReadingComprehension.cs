using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JackyAIApp.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddReadingComprehension : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ReadingPassageId",
                table: "ExamAttempts",
                type: "nvarchar(450)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ReadingPassages",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PassageText = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReadingPassages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReadingQuestions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    QuestionText = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PassageId = table.Column<string>(type: "nvarchar(450)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReadingQuestions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReadingQuestions_ReadingPassages_PassageId",
                        column: x => x.PassageId,
                        principalTable: "ReadingPassages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReadingQuestionOptions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsCorrect = table.Column<bool>(type: "bit", nullable: false),
                    Explanation = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    QuestionId = table.Column<string>(type: "nvarchar(450)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReadingQuestionOptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReadingQuestionOptions_ReadingQuestions_QuestionId",
                        column: x => x.QuestionId,
                        principalTable: "ReadingQuestions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExamAttempts_ReadingPassageId",
                table: "ExamAttempts",
                column: "ReadingPassageId");

            migrationBuilder.CreateIndex(
                name: "IX_ReadingQuestionOptions_QuestionId",
                table: "ReadingQuestionOptions",
                column: "QuestionId");

            migrationBuilder.CreateIndex(
                name: "IX_ReadingQuestions_PassageId",
                table: "ReadingQuestions",
                column: "PassageId");

            migrationBuilder.AddForeignKey(
                name: "FK_ExamAttempts_ReadingPassages_ReadingPassageId",
                table: "ExamAttempts",
                column: "ReadingPassageId",
                principalTable: "ReadingPassages",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ExamAttempts_ReadingPassages_ReadingPassageId",
                table: "ExamAttempts");

            migrationBuilder.DropTable(
                name: "ReadingQuestionOptions");

            migrationBuilder.DropTable(
                name: "ReadingQuestions");

            migrationBuilder.DropTable(
                name: "ReadingPassages");

            migrationBuilder.DropIndex(
                name: "IX_ExamAttempts_ReadingPassageId",
                table: "ExamAttempts");

            migrationBuilder.DropColumn(
                name: "ReadingPassageId",
                table: "ExamAttempts");
        }
    }
}
