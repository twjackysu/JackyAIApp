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

        public async Task<List<NetWorthDataPoint>> GetNetWorthHistoryAsync(string userId, int days, CancellationToken ct = default)
        {
            days = Math.Clamp(days, 7, 180);

            var txs = await db.PortfolioTransactions
                .Where(t => t.UserId == userId)
                .OrderBy(t => t.Date)
                .ToListAsync(ct);

            if (txs.Count == 0) return [];

            // Try to serve from stored snapshots first (populated by daily job)
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var startDate = today.AddDays(-days);

            var snapshots = await db.NetWorthSnapshots
                .Where(s => s.UserId == userId && s.Date >= startDate)
                .OrderBy(s => s.Date)
                .ToListAsync(ct);

            // If we have ≥80% coverage use snapshots (avoids heavy price re-fetch)
            var expectedTradingDays = days * 5 / 7; // rough estimate
            if (snapshots.Count >= expectedTradingDays * 0.8)
            {
                return snapshots.Select(s => new NetWorthDataPoint
                {
                    Date = s.Date.ToString("yyyy-MM-dd"),
                    TotalTWD = s.TotalTWD,
                    TwStockTWD = s.TwStockTWD,
                    UsStockTWD = s.UsStockTWD,
                }).ToList();
            }

            // Otherwise reconstruct from price history (backfill)
            var usdTwdRate = await FetchUsdTwdRateAsync(ct);
            var symbols = txs.Select(t => (t.Market, t.Symbol)).Distinct().ToList();

            // Fetch all price histories sequentially (Yahoo throttle-safe)
            var priceMap = new Dictionary<(string market, string symbol), List<DTO.Finance.DailyPrice>>();
            foreach (var sym in symbols)
            {
                try
                {
                    var region = providerFactory.DetectRegion(sym.Symbol);
                    var provider = providerFactory.GetMarketDataProvider(region);
                    var data = await provider.FetchAsync(sym.Symbol, ct);
                    priceMap[sym] = data.HistoricalPrices;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Price history unavailable for {Symbol}", sym.Symbol);
                }
            }

            var result = new List<NetWorthDataPoint>();
            for (var date = startDate; date <= today; date = date.AddDays(1))
            {
                if (date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday) continue;

                var holdingMap = DeriveHoldings(txs.Where(t => DateOnly.FromDateTime(t.Date) <= date));
                decimal twTWD = 0, usTWD = 0;

                foreach (var (key, holding) in holdingMap)
                {
                    if (holding.Quantity <= 0) continue;
                    if (!priceMap.TryGetValue(key, out var prices)) continue;

                    var dateTime = date.ToDateTime(TimeOnly.MinValue);
                    var price = prices
                        .Where(p => p.Date.Date <= dateTime)
                        .OrderByDescending(p => p.Date)
                        .FirstOrDefault()?.Close ?? 0;

                    if (price <= 0) continue;

                    var value = price * holding.Quantity;
                    if (key.market == "US") usTWD += value * usdTwdRate;
                    else twTWD += value;
                }

                if (twTWD + usTWD > 0)
                    result.Add(new NetWorthDataPoint
                    {
                        Date = date.ToString("yyyy-MM-dd"),
                        TotalTWD = twTWD + usTWD,
                        TwStockTWD = twTWD,
                        UsStockTWD = usTWD,
                    });
            }

            return result;
        }

        public async Task TakeSnapshotAsync(string userId, CancellationToken ct = default)
        {
            var summary = await GetPortfolioSummaryAsync(userId, ct);
            if (summary.TotalNetWorthTWD <= 0) return;

            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var existing = await db.NetWorthSnapshots
                .FirstOrDefaultAsync(s => s.UserId == userId && s.Date == today, ct);

            if (existing != null)
            {
                existing.TotalTWD = summary.TotalNetWorthTWD;
                existing.TwStockTWD = summary.TwStockTWD;
                existing.UsStockTWD = summary.UsStockTWD;
                existing.UsdTwdRate = summary.UsdTwdRate;
            }
            else
            {
                db.NetWorthSnapshots.Add(new NetWorthSnapshot
                {
                    UserId = userId,
                    Date = today,
                    TotalTWD = summary.TotalNetWorthTWD,
                    TwStockTWD = summary.TwStockTWD,
                    UsStockTWD = summary.UsStockTWD,
                    UsdTwdRate = summary.UsdTwdRate,
                });
            }

            await db.SaveChangesAsync(ct);
        }

        public async Task<StockLookupDto?> LookupStockAsync(string market, string symbol, CancellationToken ct = default)
        {
            try
            {
                var region = providerFactory.DetectRegion(symbol);
                var provider = providerFactory.GetMarketDataProvider(region);
                var data = await provider.FetchAsync(symbol, ct);
                var lastPrice = data.HistoricalPrices.LastOrDefault()?.Close ?? 0;
                var currency = market.ToUpperInvariant() == "US" ? "USD" : "TWD";
                return new StockLookupDto
                {
                    Symbol = symbol.ToUpperInvariant(),
                    CompanyName = data.CompanyName,
                    CurrentPrice = lastPrice,
                    Currency = currency,
                };
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Lookup failed for {Market}:{Symbol}", market, symbol);
                return null;
            }
        }

        public async Task<HoldingPriceHistoryDto?> GetHoldingPriceHistoryAsync(
            string userId, string market, string symbol, CancellationToken ct = default)
        {
            var txs = await db.PortfolioTransactions
                .Where(t => t.UserId == userId &&
                            t.Market == market.ToUpperInvariant() &&
                            t.Symbol == symbol.ToUpperInvariant())
                .OrderBy(t => t.Date)
                .ToListAsync(ct);

            if (txs.Count == 0) return null;

            var allUserTxs = await db.PortfolioTransactions
                .Where(t => t.UserId == userId)
                .OrderBy(t => t.Date)
                .ToListAsync(ct);

            var holdingMap = DeriveHoldings(allUserTxs);
            var key = (market.ToUpperInvariant(), symbol.ToUpperInvariant());
            if (!holdingMap.TryGetValue(key, out var acc) || acc.Quantity <= 0) return null;

            var region = providerFactory.DetectRegion(symbol);
            var provider = providerFactory.GetMarketDataProvider(region);
            var data = await provider.FetchAsync(symbol, ct);

            return new HoldingPriceHistoryDto
            {
                Symbol = symbol.ToUpperInvariant(),
                CompanyName = acc.CompanyName,
                Market = market.ToUpperInvariant(),
                Currency = acc.Currency,
                AvgCost = acc.Quantity > 0 ? acc.TotalCost / acc.Quantity : 0,
                Quantity = acc.Quantity,
                Prices = data.HistoricalPrices
                    .Select(p => new PricePoint
                    {
                        Date = p.Date.ToString("yyyy-MM-dd"),
                        Close = p.Close,
                        High = p.High,
                        Low = p.Low,
                    })
                    .ToList(),
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
