using JackyAIApp.Server.DTO.Portfolio;

namespace JackyAIApp.Server.Services.Portfolio
{
    /// <summary>One symbol's price: the current one when adding a holding, and a held position's chart.</summary>
    public interface IHoldingPriceService
    {
        Task<StockLookupDto?> LookupStockAsync(string market, string symbol, CancellationToken ct = default);
        Task<HoldingPriceHistoryDto?> GetHoldingPriceHistoryAsync(string userId, string market, string symbol, CancellationToken ct = default);
    }

    public class HoldingPriceService(
        IHoldingSnapshotReader snapshots,
        IPortfolioPriceLoader prices,
        IFundDataService fundData) : IHoldingPriceService
    {
        public async Task<StockLookupDto?> LookupStockAsync(string market, string symbol, CancellationToken ct = default)
        {
            // Only the current price is shown here — shallow fetch.
            var data = await prices.FetchStockAsync(symbol, deep: false, ct);
            if (data == null) return null;

            var lastPrice = data.HistoricalPrices.LastOrDefault()?.Close ?? 0;
            return new StockLookupDto
            {
                Symbol = symbol.ToUpperInvariant(),
                CompanyName = data.CompanyName,
                CurrentPrice = lastPrice,
                Currency = market.ToUpperInvariant() == "US" ? "USD" : "TWD",
            };
        }

        public async Task<HoldingPriceHistoryDto?> GetHoldingPriceHistoryAsync(
            string userId, string market, string symbol, CancellationToken ct = default)
        {
            var pos = await snapshots.FindLatestPositionAsync(userId, market, symbol, ct);
            if (pos == null) return null;
            var isFund = market.ToUpperInvariant() == "FUND";

            var rawAvg = pos.CostBasis.HasValue && pos.Quantity > 0
                ? pos.CostBasis.Value / pos.Quantity
                : (decimal?)null;

            // The chart's prices are in the pricing currency, but the cost may have been paid in
            // another (e.g. USD-priced fund bought with TWD). Convert the average cost into the
            // chart currency so the cost line and "% vs 成本" line up with the price axis.
            var fx = await prices.FetchUsdTwdRateAsync(ct);
            var costCurrency = string.IsNullOrWhiteSpace(pos.CostCurrency) ? pos.Currency : pos.CostCurrency;
            decimal? AvgInCurrency(string chartCurrency) =>
                rawAvg == null
                    ? null
                    : rawAvg.Value * (costCurrency == "USD" ? fx : 1m) / (chartCurrency == "USD" ? fx : 1m);

            if (isFund)
            {
                var nav = await fundData.GetFundNavHistoryAsync(pos.Symbol, ct);
                if (nav == null) return null;
                return new HoldingPriceHistoryDto
                {
                    Symbol = pos.Symbol,
                    CompanyName = pos.Name,
                    Market = "FUND",
                    Currency = nav.Currency,
                    Quantity = pos.Quantity,
                    AvgCost = AvgInCurrency(nav.Currency),
                    Prices = nav.Prices,
                };
            }

            // Chart needs the full series — deep fetch.
            var data = await prices.FetchStockAsync(symbol, deep: true, ct);
            if (data == null) return null;

            return new HoldingPriceHistoryDto
            {
                Symbol = pos.Symbol,
                CompanyName = pos.Name,
                Market = pos.Market,
                Currency = pos.Currency,
                Quantity = pos.Quantity,
                AvgCost = AvgInCurrency(pos.Currency),
                Prices = data.HistoricalPrices
                    .Select(x => new PricePoint
                    {
                        Date = x.Date.ToString("yyyy-MM-dd"),
                        Close = x.Close,
                        High = x.High,
                        Low = x.Low,
                    })
                    .ToList(),
            };
        }
    }
}
