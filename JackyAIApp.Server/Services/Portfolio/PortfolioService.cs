using JackyAIApp.Server.Data;
using JackyAIApp.Server.Data.Models.Portfolio;
using JackyAIApp.Server.DTO.Portfolio;
using JackyAIApp.Server.Services.Finance.DataProviders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace JackyAIApp.Server.Services.Portfolio
{
    public class PortfolioService(
        AzureSQLDBContext db,
        IMarketDataProviderFactory providerFactory,
        IHttpClientFactory httpClientFactory,
        IMemoryCache cache,
        ILogger<PortfolioService> logger) : IPortfolioService
    {
        private const string BOT_FX_URL = "https://rate.bot.com.tw/xrt/flcsv/0/day";
        private const string FX_CACHE_KEY = "portfolio_usd_twd_rate";

        public async Task<TransactionDto> AddTransactionAsync(string userId, AddTransactionRequest req, CancellationToken ct = default)
        {
            var tx = new PortfolioTransaction
            {
                UserId = userId,
                Symbol = req.Symbol.ToUpperInvariant(),
                Market = req.Market.ToUpperInvariant(),
                CompanyName = req.CompanyName,
                Type = req.Type,
                Date = req.Date.ToUniversalTime(),
                Quantity = req.Quantity,
                Price = req.Price,
                Currency = req.Currency.ToUpperInvariant(),
                Fee = req.Fee,
                Note = req.Note,
            };
            db.PortfolioTransactions.Add(tx);
            await db.SaveChangesAsync(ct);
            return MapToDto(tx);
        }

        public async Task<List<TransactionDto>> GetTransactionsAsync(string userId, CancellationToken ct = default)
        {
            var txs = await db.PortfolioTransactions
                .Where(t => t.UserId == userId)
                .OrderByDescending(t => t.Date)
                .ToListAsync(ct);
            return txs.Select(MapToDto).ToList();
        }

        public async Task DeleteTransactionAsync(string userId, int id, CancellationToken ct = default)
        {
            var tx = await db.PortfolioTransactions
                .FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId, ct)
                ?? throw new KeyNotFoundException($"Transaction {id} not found");
            db.PortfolioTransactions.Remove(tx);
            await db.SaveChangesAsync(ct);
        }

        public async Task<PortfolioSummaryDto> GetPortfolioSummaryAsync(string userId, CancellationToken ct = default)
        {
            var txs = await db.PortfolioTransactions
                .Where(t => t.UserId == userId)
                .OrderBy(t => t.Date)
                .ToListAsync(ct);

            if (txs.Count == 0)
                return new PortfolioSummaryDto();

            var holdingMap = DeriveHoldings(txs);
            var usdTwdRate = await FetchUsdTwdRateAsync(ct);

            var holdings = new List<HoldingDto>();
            decimal twStockTWD = 0, usStockTWD = 0;

            foreach (var (key, holding) in holdingMap)
            {
                if (holding.Quantity <= 0) continue;

                var (market, symbol) = key;
                decimal currentPrice = 0;
                bool priceFailed = false;

                try
                {
                    var region = providerFactory.DetectRegion(symbol);
                    var provider = providerFactory.GetMarketDataProvider(region);
                    var data = await provider.FetchAsync(symbol, ct);
                    currentPrice = data.HistoricalPrices.LastOrDefault()?.Close ?? 0;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to fetch price for {symbol}", symbol);
                    priceFailed = true;
                }

                var marketValueLocal = currentPrice * holding.Quantity;
                var multiplier = market == "US" ? usdTwdRate : 1m;
                var marketValueTWD = marketValueLocal * multiplier;
                var costBasisTWD = holding.TotalCostTWD(usdTwdRate);
                var unrealizedPnl = marketValueTWD - costBasisTWD;
                var unrealizedPct = costBasisTWD > 0 ? unrealizedPnl / costBasisTWD * 100 : 0;

                if (market == "TW") twStockTWD += marketValueTWD;
                else usStockTWD += marketValueTWD;

                holdings.Add(new HoldingDto
                {
                    Symbol = symbol,
                    Market = market,
                    CompanyName = holding.CompanyName,
                    Currency = holding.Currency,
                    Quantity = holding.Quantity,
                    AvgCost = holding.Quantity > 0 ? holding.TotalCost / holding.Quantity : 0,
                    CurrentPrice = currentPrice,
                    MarketValueLocal = marketValueLocal,
                    MarketValueTWD = marketValueTWD,
                    CostBasisTWD = costBasisTWD,
                    UnrealizedPnlTWD = unrealizedPnl,
                    UnrealizedPnlPct = Math.Round(unrealizedPct, 2),
                    PriceFetchFailed = priceFailed,
                });
            }

            var total = twStockTWD + usStockTWD;
            var totalCost = holdings.Sum(h => h.CostBasisTWD);
            var totalPnl = holdings.Sum(h => h.UnrealizedPnlTWD);
            var totalPct = totalCost > 0 ? totalPnl / totalCost * 100 : 0;

            return new PortfolioSummaryDto
            {
                TotalNetWorthTWD = total,
                TwStockTWD = twStockTWD,
                UsStockTWD = usStockTWD,
                UsdTwdRate = usdTwdRate,
                TotalUnrealizedPnlTWD = totalPnl,
                TotalUnrealizedPnlPct = Math.Round(totalPct, 2),
                Holdings = holdings.OrderByDescending(h => h.MarketValueTWD).ToList(),
            };
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        private static Dictionary<(string market, string symbol), HoldingAccumulator> DeriveHoldings(
            IEnumerable<PortfolioTransaction> txs)
        {
            var map = new Dictionary<(string, string), HoldingAccumulator>();

            foreach (var tx in txs)
            {
                var key = (tx.Market, tx.Symbol);
                if (!map.TryGetValue(key, out var acc))
                {
                    acc = new HoldingAccumulator(tx.CompanyName, tx.Currency);
                    map[key] = acc;
                }

                if (tx.Type == TransactionType.BUY)
                {
                    acc.Quantity += tx.Quantity;
                    acc.TotalCost += tx.Quantity * tx.Price + tx.Fee;
                }
                else if (tx.Type == TransactionType.SELL)
                {
                    // Reduce avg cost proportionally
                    if (acc.Quantity > 0)
                    {
                        var avgCost = acc.TotalCost / acc.Quantity;
                        acc.TotalCost -= avgCost * tx.Quantity;
                    }
                    acc.Quantity -= tx.Quantity;
                    if (acc.Quantity < 0) acc.Quantity = 0;
                    if (acc.TotalCost < 0) acc.TotalCost = 0;
                }
            }

            return map;
        }

        private async Task<decimal> FetchUsdTwdRateAsync(CancellationToken ct)
        {
            if (cache.TryGetValue(FX_CACHE_KEY, out decimal cached) && cached > 0)
                return cached;

            try
            {
                var client = httpClientFactory.CreateClient();
                var csv = await client.GetStringAsync(BOT_FX_URL, ct);
                var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines.Skip(1))
                {
                    var cols = line.Split(',');
                    if (cols.Length < 14) continue;
                    if (cols[0].Trim() != "USD") continue;
                    if (decimal.TryParse(cols[13].Trim(), out var sell) && sell > 0)
                    {
                        cache.Set(FX_CACHE_KEY, sell, TimeSpan.FromHours(4));
                        return sell;
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to fetch USD/TWD rate, using fallback 32");
            }

            return 32m; // fallback
        }

        private static TransactionDto MapToDto(PortfolioTransaction tx) => new()
        {
            Id = tx.Id,
            Symbol = tx.Symbol,
            Market = tx.Market,
            CompanyName = tx.CompanyName,
            Type = tx.Type,
            Date = tx.Date,
            Quantity = tx.Quantity,
            Price = tx.Price,
            Currency = tx.Currency,
            Fee = tx.Fee,
            Note = tx.Note,
        };

        private class HoldingAccumulator(string companyName, string currency)
        {
            public string CompanyName { get; } = companyName;
            public string Currency { get; } = currency;
            public decimal Quantity { get; set; }
            public decimal TotalCost { get; set; }

            public decimal TotalCostTWD(decimal usdTwdRate) =>
                Currency == "USD" ? TotalCost * usdTwdRate : TotalCost;
        }
    }
}
