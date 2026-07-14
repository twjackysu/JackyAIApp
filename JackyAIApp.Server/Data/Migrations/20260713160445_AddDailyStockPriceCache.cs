using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JackyAIApp.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDailyStockPriceCache : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DailyStockPrices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Symbol = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Market = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TradeDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Open = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    High = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Low = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Close = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Volume = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailyStockPrices", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StockSymbolInfos",
                columns: table => new
                {
                    Symbol = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Market = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TwMarketType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LastResolvedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockSymbolInfos", x => x.Symbol);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DailyStockPrices_Symbol_TradeDate",
                table: "DailyStockPrices",
                columns: new[] { "Symbol", "TradeDate" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DailyStockPrices");

            migrationBuilder.DropTable(
                name: "StockSymbolInfos");
        }
    }
}
