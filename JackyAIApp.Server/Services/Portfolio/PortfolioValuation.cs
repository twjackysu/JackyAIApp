using JackyAIApp.Server.Data.Models.Portfolio;

namespace JackyAIApp.Server.Services.Portfolio
{
    /// <summary>Valuing a snapshot's positions as of a past day, shared by the summary's day change,
    /// the drawdown curve and the benchmark comparison.</summary>
    internal static class PortfolioValuation
    {
        /// <summary>The USD/TWD rate saved on a snapshot, or <see cref="PortfolioPriceLoader.FallbackUsdTwdRate"/>
        /// when it has none. Used to rebuild history as of that snapshot; the current valuation uses the
        /// live rate instead.</summary>
        internal static decimal SnapshotFx(HoldingSnapshot snapshot) =>
            snapshot.UsdTwdRate > 0 ? snapshot.UsdTwdRate : PortfolioPriceLoader.FallbackUsdTwdRate;

        /// <summary>
        /// Re-values a set of positions at their closing price as of <paramref name="date"/>, using
        /// already-fetched price histories (no network calls). Returns null if any non-cash position
        /// has no price on or before that date, so the caller can treat the whole total as unknown
        /// rather than silently under-counting a position as worth 0.
        /// </summary>
        internal static decimal? ValueAsOfDate(
            IEnumerable<HoldingPosition> positions, DateOnly date, decimal fx,
            AsOfPriceBook prices,
            bool includeRealEstate = true, bool includeLiability = true,
            // Per-position TWD rate given the price currency. When null, USD converts at `fx` and
            // everything else at 1, which is what the history/drawdown callers want. The day change
            // passes the summary's own PositionFx so both of its sides convert identically.
            Func<HoldingPosition, string?, decimal>? fxFor = null)
        {
            decimal Fx(HoldingPosition p, string? currency) =>
                fxFor?.Invoke(p, currency) ?? (currency == "USD" ? fx : 1m);

            var dayKey = date.ToString("yyyy-MM-dd");
            decimal total = 0;

            foreach (var p in positions)
            {
                var policy = AssetValuationPolicy.For(p.AssetType);
                if (!policy.IsMarketPriced)
                {
                    // Real estate is the one holding that isn't liquid; a liability the one that counts negative.
                    var excluded = (!includeRealEstate && !policy.CountsAsLiquid) || (!includeLiability && policy.Sign < 0);
                    if (!excluded) total += policy.Sign * p.Quantity * Fx(p, p.Currency);
                    continue;
                }

                decimal price = 0;
                var priceCurrency = p.Currency;

                if (p.AssetType == AssetType.STOCK)
                {
                    if (prices.Stocks.TryGetValue(p.Symbol, out var series))
                        price = series.CloseOnOrBefore(date) ?? 0;
                }
                else // FUND
                {
                    if (!string.IsNullOrWhiteSpace(p.Symbol) && prices.Funds.TryGetValue(p.Symbol, out var fund))
                    {
                        priceCurrency = fund.Currency;
                        price = fund.Prices.CloseOnOrBefore(dayKey) ?? 0;
                    }
                    if (price <= 0) price = p.UnitPrice ?? 0; // fallback, mirrors the live valuation loop
                }

                if (price <= 0) return null;
                total += price * p.Quantity * Fx(p, priceCurrency);
            }

            return total;
        }
    }
}
