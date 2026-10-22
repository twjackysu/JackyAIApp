namespace JackyAIApp.Server.Services.Market
{
    /// <summary>
    /// Taiwan volume units. <see cref="DTO.Finance.DailyPrice.Volume"/> is shares for every TW bar the
    /// providers hand out, and so is every stored <c>DailyStockPrice</c>: TWStockLib gives the daily
    /// history of both exchanges in shares (it converts TPEx's lots since 2.3; rows stored before were
    /// converted by the ConvertOtcVolumeToShares migration). The realtime quote's running total is
    /// still in lots, as MIS publishes it, which <see cref="TwTodayOverlayMarketDataProvider"/> scales.
    /// </summary>
    public static class TwVolume
    {
        public const long SharesPerLot = 1000;
    }
}
