using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JackyAIApp.Server.Data.Migrations
{
    /// <summary>
    /// Portfolio's AI classification of a symbol moves out of StockSymbolInfos (MarketData's table)
    /// into StockClassifications, Portfolio's own. The rows are copied before the columns are dropped,
    /// and copied back on the way down.
    /// </summary>
    public partial class SplitStockClassification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StockClassifications",
                columns: table => new
                {
                    Symbol = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Category = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SubCategory = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Tags = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ClassifiedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockClassifications", x => x.Symbol);
                });

            // Only rows that were classified: a null Category meant "not yet", which is now no row.
            // EXEC: in a generated script this shares a batch with the CREATE TABLE above, and SQL
            // Server compiles a batch before running it, when the table does not exist yet.
            migrationBuilder.Sql("""
                EXEC('INSERT INTO StockClassifications (Symbol, Category, SubCategory, Tags, ClassifiedAt)
                SELECT Symbol, Category, SubCategory, Tags, COALESCE(ClassifiedAt, LastResolvedAt)
                FROM StockSymbolInfos
                WHERE Category IS NOT NULL')
                """);

            migrationBuilder.DropColumn(
                name: "Category",
                table: "StockSymbolInfos");

            migrationBuilder.DropColumn(
                name: "ClassifiedAt",
                table: "StockSymbolInfos");

            migrationBuilder.DropColumn(
                name: "SubCategory",
                table: "StockSymbolInfos");

            migrationBuilder.DropColumn(
                name: "Tags",
                table: "StockSymbolInfos");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Category",
                table: "StockSymbolInfos",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ClassifiedAt",
                table: "StockSymbolInfos",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SubCategory",
                table: "StockSymbolInfos",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Tags",
                table: "StockSymbolInfos",
                type: "nvarchar(max)",
                nullable: true);

            // Up drops no StockSymbolInfos row, so every symbol classified before it is still there. One
            // first classified after Up has no row to go back to; it is classified again when next shown.
            migrationBuilder.Sql("""
                EXEC('UPDATE s SET Category = c.Category, SubCategory = c.SubCategory, Tags = c.Tags, ClassifiedAt = c.ClassifiedAt
                FROM StockSymbolInfos s JOIN StockClassifications c ON c.Symbol = s.Symbol')
                """);

            migrationBuilder.DropTable(
                name: "StockClassifications");
        }
    }
}
