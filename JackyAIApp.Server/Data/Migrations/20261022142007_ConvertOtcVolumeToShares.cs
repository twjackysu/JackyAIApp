using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JackyAIApp.Server.Data.Migrations
{
    /// <summary>
    /// Stored TPEx (上櫃) volumes from lots to shares. TWStockLib 2.x passed TPEx's daily history
    /// through in lots, so those rows were stored in lots and multiplied by 1000 when read; from 2.3
    /// (we move to 3.0) it gives shares like TWSE's. The rows converted are exactly the ones the read
    /// multiplied: a TW row whose symbol's StockSymbolInfos.TwMarketType is "OTC". Its own Market
    /// column is "TW" for every TW row and says nothing of the exchange.
    /// </summary>
    public partial class ConvertOtcVolumeToShares : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE d SET Volume = d.Volume * 1000
                FROM DailyStockPrices d
                JOIN StockSymbolInfos s ON s.Symbol = d.Symbol
                WHERE d.Market = 'TW' AND s.TwMarketType = 'OTC'
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Back to lots, for TWStockLib 2.x and the read that multiplied them again. Rows stored
            // after Up were stored in shares too, so they convert back the same way.
            migrationBuilder.Sql("""
                UPDATE d SET Volume = d.Volume / 1000
                FROM DailyStockPrices d
                JOIN StockSymbolInfos s ON s.Symbol = d.Symbol
                WHERE d.Market = 'TW' AND s.TwMarketType = 'OTC'
                """);
        }
    }
}
