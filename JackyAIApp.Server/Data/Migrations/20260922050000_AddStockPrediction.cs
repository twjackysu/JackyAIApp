using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JackyAIApp.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddStockPrediction : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StockPredictions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    StockCode = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Market = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(255)", nullable: false),
                    PredictedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PriceAtPrediction = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    HorizonMonthsMin = table.Column<int>(type: "int", nullable: false),
                    HorizonMonthsMax = table.Column<int>(type: "int", nullable: false),
                    PredictedTrend = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EvaluatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ActualTrend = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ActualPriceAtEvaluation = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    WasCorrect = table.Column<bool>(type: "bit", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockPredictions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StockPredictions_EvaluatedAtUtc_PredictedAtUtc",
                table: "StockPredictions",
                columns: new[] { "EvaluatedAtUtc", "PredictedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StockPredictions");
        }
    }
}
