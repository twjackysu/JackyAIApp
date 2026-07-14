using JackyAIApp.Server.DTO.Finance;
using TWStockLib.Models;

namespace JackyAIApp.Server.Services.Finance.DataProviders
{
    /// <summary>
    /// The subset of <see cref="TWStockLibHistoricalProvider"/> that <see
    /// cref="PersistentTwHistoricalProvider"/> depends on directly (rather than <see
    /// cref="IMarketDataProvider"/>'s single fixed-window <c>FetchAsync</c>). Extracted purely as a
    /// test seam: TWStockLib's own <c>Result&lt;T&gt;</c> return types are awkward to construct in
    /// tests, so mocking at this boundary (our own types only) keeps
    /// PersistentTwHistoricalProvider's tests independent of TWStockLib's exact API shape.
    /// </summary>
    public interface ITWStockLibHistoricalProvider
    {
        Task<MarketData> FetchRangeAsync(
            string stockCode, DateTime startDate, DateTime endDate,
            MarketType? knownMarketType, CancellationToken cancellationToken = default);

        Task<MarketType> DetermineMarketType(string stockCode);
    }
}
