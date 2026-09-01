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

        /// <summary>
        /// Which TW market a symbol trades on, or <c>null</c> when that could not be established.
        /// Null is distinct from TSE on purpose: a caller that persists the answer must not record
        /// a guess as if it were a lookup result. See the implementation for what went wrong when
        /// the two were the same value.
        /// </summary>
        Task<MarketType?> DetermineMarketType(string stockCode);

        /// <summary>Realtime/final quote for a TW symbol, or null on any failure (never throws).
        /// Resolves market type first when not supplied (memory-cached, cheap), then tries that
        /// market followed by the other one — same fallback <see cref="TWStockLibHistoricalProvider"/>
        /// already used internally for its own company-name lookup, now also the single source for
        /// <c>TwTodayOverlayMarketDataProvider</c>'s today-price overlay.</summary>
        Task<StockQuote?> GetLatestQuoteAsync(
            string stockCode, MarketType? knownMarketType, CancellationToken cancellationToken = default);
    }
}
