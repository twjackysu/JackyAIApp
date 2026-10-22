namespace JackyAIApp.Server.Services.Market
{
    /// <summary>
    /// Taiwan volume units. TWStockLib passes each figure through as the exchange publishes it:
    /// TWSE's daily history in shares (成交股數), TPEx's daily history in lots (成交張數), and the
    /// realtime quote's running total in lots. <see cref="DTO.Finance.DailyPrice.Volume"/> is shares
    /// for every TW bar the providers hand out: <see cref="PersistentTwHistoricalProvider"/> scales
    /// a TPEx history and <see cref="TwTodayOverlayMarketDataProvider"/> the quote.
    /// </summary>
    public static class TwVolume
    {
        public const long SharesPerLot = 1000;

        /// <summary>What to multiply a stored bar's volume by to get shares: persisted rows keep the
        /// exchange's own unit (lots for TPEx, so OTC). An unresolved market reads as TWSE, as the
        /// fetch does.</summary>
        public static long ToSharesFactor(string? twMarketType) =>
            twMarketType == nameof(TWStockLib.Models.MarketType.OTC) ? SharesPerLot : 1;
    }
}
