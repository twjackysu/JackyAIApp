using JackyAIApp.Server.Data.Models.Portfolio;
using JackyAIApp.Server.Data.Models.SQL;
using JackyAIApp.Server.DTO.Finance;

namespace JackyAIApp.Server.Services.Portfolio
{
    /// <summary>
    /// The rate a position converts to TWD at, and which rate that is. A fund converts at the 集保 rate:
    /// what 基富通 converts it back at and shows (a mid-market rate put it ~0.5% off). A US stock converts
    /// at the 國泰世華 rate published for its quote's own day, 國泰 複委託 valuing it as
    /// 股數 × 最新價 × 昨收匯率. Anything else in USD converts at the mid-market rate, which is also the
    /// fallback when a published rate is missing: an FX lookup must never stop a portfolio from being valued.
    /// </summary>
    /// <remarks>
    /// Today's valuation and the previous trading day's (the day change) convert through one resolver, so
    /// the two sides never differ by rate source, only by price or holdings.
    /// </remarks>
    internal sealed class PositionFxResolver(
        decimal midUsdTwd,
        IReadOnlyDictionary<string, decimal> tdccRates,
        IReadOnlyDictionary<(string Currency, DateOnly Date), decimal> cathayRates,
        IReadOnlyDictionary<string, MarketData> stocks,
        IReadOnlyDictionary<string, FundNavHistory> funds)
    {
        /// <summary>The live mid-market USD/TWD rate.</summary>
        public decimal MidUsdTwd => midUsdTwd;

        /// <summary>
        /// The rate that converts <paramref name="position"/>'s amounts in <paramref name="currency"/>, and
        /// its source (<see cref="FxRateSources"/>); (1, null) for TWD or a currency with no rate.
        /// </summary>
        public (decimal Rate, string? Source) Resolve(HoldingPosition position, string? currency)
        {
            if (string.IsNullOrWhiteSpace(currency) || currency == "TWD") return (1m, null);

            var policy = AssetValuationPolicy.For(position.AssetType);
            if (policy.PublishedFx == PublishedFxSource.Tdcc && tdccRates.TryGetValue(currency, out var tdcc))
                return (tdcc, FxRateSources.Tdcc);
            if (CathayRate(position, policy, currency) is { } cathay)
                return (cathay, FxRateSources.Cathay);
            return currency == "USD" ? (midUsdTwd, FxRateSources.Mid) : (1m, null);
        }

        /// <summary>The rate alone (see <see cref="Resolve"/>).</summary>
        public decimal RateFor(HoldingPosition position, string? currency) => Resolve(position, currency).Rate;

        private decimal? CathayRate(HoldingPosition position, AssetValuationPolicy policy, string currency)
        {
            if (policy.PublishedFx != PublishedFxSource.CathayOnQuoteDay) return null;
            if (policy.Latest(position, stocks, funds)?.Day is not { } quoteDay) return null;
            return cathayRates.TryGetValue((currency, quoteDay), out var rate) ? rate : null;
        }
    }
}
