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
        IFundDataService fundData,
        TWStockLib.Services.StockMarketService stockService,
        IHttpClientFactory httpClientFactory,
        IMemoryCache cache,
        ILogger<PortfolioService> logger) : IPortfolioService
    {
        private const string BOT_FX_URL = "https://rate.bot.com.tw/xrt/flcsv/0/day";
        private const string FX_CACHE_KEY = "portfolio_usd_twd_rate";

        // ── Save / read holdings ─────────────────────────────────────────────

        public async Task SaveHoldingsAsync(string userId, SaveHoldingsRequest req, CancellationToken ct = default)
        {
            var effectiveDate = req.EffectiveDate ?? DateOnly.FromDateTime(DateTime.UtcNow);
            var usdTwdRate = await FetchUsdTwdRateAsync(ct);

            // Replace any existing snapshot for that date (cascade deletes its positions)
            var existing = await db.HoldingSnapshots
                .FirstOrDefaultAsync(s => s.UserId == userId && s.EffectiveDate == effectiveDate, ct);
            if (existing != null)
                db.HoldingSnapshots.Remove(existing);

            var snapshot = new HoldingSnapshot
            {
                UserId = userId,
                EffectiveDate = effectiveDate,
                UsdTwdRate = usdTwdRate,
                Positions = req.Positions
                    .Where(p => p.Quantity > 0)
                    .Select(p => new HoldingPosition
                    {
                        AssetType = p.AssetType,
                        Symbol = (p.Symbol ?? "").ToUpperInvariant(),
                        Name = p.Name ?? "",
                        Market = (p.Market ?? "").ToUpperInvariant(),
                        Currency = string.IsNullOrWhiteSpace(p.Currency) ? "TWD" : p.Currency.ToUpperInvariant(),
                        Quantity = p.Quantity,
                        UnitPrice = p.AssetType == AssetType.FUND ? p.UnitPrice : null,
                        CostBasis = p.AssetType == AssetType.STOCK ? p.CostBasis : null,
                    })
                    .ToList(),
            };

            db.HoldingSnapshots.Add(snapshot);
            await db.SaveChangesAsync(ct);
        }

        public async Task<SaveHoldingsRequest> GetLatestHoldingsAsync(string userId, CancellationToken ct = default)
        {
            var snapshot = await LatestSnapshotAsync(userId, ct);
            if (snapshot == null)
                return new SaveHoldingsRequest { EffectiveDate = DateOnly.FromDateTime(DateTime.UtcNow) };

            return new SaveHoldingsRequest
            {
                EffectiveDate = snapshot.EffectiveDate,
                Positions = snapshot.Positions
                    .Select(p => new PositionInput
                    {
                        AssetType = p.AssetType,
                        Symbol = p.Symbol,
                        Name = p.Name,
                        Market = p.Market,
                        Currency = p.Currency,
                        Quantity = p.Quantity,
                        UnitPrice = p.UnitPrice,
                        CostBasis = p.CostBasis,
                    })
                    .ToList(),
            };
        }

        public async Task<SaveHoldingsRequest?> GetSnapshotHoldingsAsync(string userId, int snapshotId, CancellationToken ct = default)
        {
            var snapshot = await db.HoldingSnapshots
                .Include(s => s.Positions)
                .FirstOrDefaultAsync(s => s.Id == snapshotId && s.UserId == userId, ct);
            if (snapshot == null) return null;

            return new SaveHoldingsRequest
            {
                EffectiveDate = snapshot.EffectiveDate,
                Positions = snapshot.Positions
                    .Select(p => new PositionInput
                    {
                        AssetType = p.AssetType,
                        Symbol = p.Symbol,
                        Name = p.Name,
                        Market = p.Market,
                        Currency = p.Currency,
                        Quantity = p.Quantity,
                        UnitPrice = p.UnitPrice,
                        CostBasis = p.CostBasis,
                    })
                    .ToList(),
            };
        }

        public async Task<bool> DeleteSnapshotAsync(string userId, int snapshotId, CancellationToken ct = default)
        {
            var snapshot = await db.HoldingSnapshots
                .FirstOrDefaultAsync(s => s.Id == snapshotId && s.UserId == userId, ct);
            if (snapshot == null) return false;

            db.HoldingSnapshots.Remove(snapshot); // cascade deletes positions
            await db.SaveChangesAsync(ct);
            return true;
        }

        public async Task<List<UpdateHistoryItem>> GetUpdateHistoryAsync(string userId, CancellationToken ct = default)
        {
            return await db.HoldingSnapshots
                .Where(s => s.UserId == userId)
                .OrderByDescending(s => s.EffectiveDate)
                .Select(s => new UpdateHistoryItem
                {
                    SnapshotId = s.Id,
                    EffectiveDate = s.EffectiveDate.ToString("yyyy-MM-dd"),
                    PositionCount = s.Positions.Count,
                    UsdTwdRate = s.UsdTwdRate,
                })
                .ToListAsync(ct);
        }

        // ── Summary (current valuation) ──────────────────────────────────────

        public async Task<PortfolioSummaryDto> GetPortfolioSummaryAsync(string userId, CancellationToken ct = default)
        {
            var snapshot = await LatestSnapshotAsync(userId, ct);
            if (snapshot == null) return new PortfolioSummaryDto();

            var fx = snapshot.UsdTwdRate > 0 ? snapshot.UsdTwdRate : await FetchUsdTwdRateAsync(ct);
            var positions = new List<PositionDto>();
            decimal twStock = 0, usStock = 0, cash = 0, fund = 0;
            decimal costBasisTWD = 0, pnlTWD = 0;

            foreach (var p in snapshot.Positions)
            {
                decimal currentPrice;
                bool priceFailed = false;

                if (p.AssetType == AssetType.STOCK)
                {
                    currentPrice = 0;
                    try
                    {
                        var region = providerFactory.DetectRegion(p.Symbol);
                        var provider = providerFactory.GetMarketDataProvider(region);
                        var data = await provider.FetchAsync(p.Symbol, ct);
                        currentPrice = data.HistoricalPrices.LastOrDefault()?.Close ?? 0;
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Failed to fetch price for {Symbol}", p.Symbol);
                        priceFailed = true;
                    }
                }
                else if (p.AssetType == AssetType.FUND)
                {
                    // Prefer live NAV from FundRich; fall back to the value stored at save time.
                    currentPrice = p.UnitPrice ?? 0;
                    if (!string.IsNullOrWhiteSpace(p.Symbol))
                    {
                        var nav = await fundData.GetFundNavHistoryAsync(p.Symbol, ct);
                        var latest = nav?.Prices.LastOrDefault()?.Close;
                        if (latest is > 0)
                            currentPrice = latest.Value;
                        else if (currentPrice <= 0)
                            priceFailed = true;
                    }
                }
                else // CASH
                {
                    currentPrice = 1;
                }

                var fxMul = p.Currency == "USD" ? fx : 1m;
                var valueLocal = p.AssetType == AssetType.CASH
                    ? p.Quantity
                    : currentPrice * p.Quantity;
                var valueTWD = valueLocal * fxMul;

                decimal? posCostTWD = null, posPnlTWD = null, posPnlPct = null;
                if (p.AssetType == AssetType.STOCK && p.CostBasis.HasValue)
                {
                    posCostTWD = p.CostBasis.Value * fxMul;
                    posPnlTWD = valueTWD - posCostTWD;
                    posPnlPct = posCostTWD > 0 ? Math.Round(posPnlTWD.Value / posCostTWD.Value * 100, 2) : 0;
                    costBasisTWD += posCostTWD.Value;
                    pnlTWD += posPnlTWD.Value;
                }

                switch (p.AssetType)
                {
                    case AssetType.STOCK when p.Market == "US": usStock += valueTWD; break;
                    case AssetType.STOCK: twStock += valueTWD; break;
                    case AssetType.CASH: cash += valueTWD; break;
                    case AssetType.FUND: fund += valueTWD; break;
                }

                positions.Add(new PositionDto
                {
                    AssetType = p.AssetType,
                    Symbol = p.Symbol,
                    Name = p.Name,
                    Market = p.Market,
                    Currency = p.Currency,
                    Quantity = p.Quantity,
                    CurrentPrice = currentPrice,
                    MarketValueLocal = valueLocal,
                    MarketValueTWD = valueTWD,
                    CostBasisTWD = posCostTWD,
                    UnrealizedPnlTWD = posPnlTWD,
                    UnrealizedPnlPct = posPnlPct,
                    PriceFetchFailed = priceFailed,
                });
            }

            return new PortfolioSummaryDto
            {
                EffectiveDate = snapshot.EffectiveDate,
                UsdTwdRate = fx,
                TotalNetWorthTWD = twStock + usStock + cash + fund,
                TwStockTWD = twStock,
                UsStockTWD = usStock,
                CashTWD = cash,
                FundTWD = fund,
                CostBasisTWD = costBasisTWD,
                UnrealizedPnlTWD = pnlTWD,
                UnrealizedPnlPct = costBasisTWD > 0 ? Math.Round(pnlTWD / costBasisTWD * 100, 2) : 0,
                Positions = positions.OrderByDescending(p => p.MarketValueTWD).ToList(),
            };
        }

        // ── Net worth history ────────────────────────────────────────────────

        public async Task<List<NetWorthDataPoint>> GetNetWorthHistoryAsync(string userId, int days, CancellationToken ct = default)
        {
            days = Math.Clamp(days, 7, 180);

            var snapshots = await db.HoldingSnapshots
                .Include(s => s.Positions)
                .Where(s => s.UserId == userId)
                .OrderBy(s => s.EffectiveDate)
                .ToListAsync(ct);

            if (snapshots.Count == 0) return [];

            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var firstDate = snapshots[0].EffectiveDate;
            var startDate = today.AddDays(-days);
            if (startDate < firstDate) startDate = firstDate;

            // Fetch price history for every distinct stock symbol that appears in any snapshot
            var stockSymbols = snapshots
                .SelectMany(s => s.Positions)
                .Where(p => p.AssetType == AssetType.STOCK)
                .Select(p => p.Symbol)
                .Distinct()
                .ToList();

            var priceMap = new Dictionary<string, List<DTO.Finance.DailyPrice>>();
            foreach (var symbol in stockSymbols)
            {
                try
                {
                    var region = providerFactory.DetectRegion(symbol);
                    var provider = providerFactory.GetMarketDataProvider(region);
                    var data = await provider.FetchAsync(symbol, ct);
                    priceMap[symbol] = data.HistoricalPrices;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Price history unavailable for {Symbol}", symbol);
                }
            }

            // Fund NAV histories — gives funds a real daily curve instead of a flat line.
            var fundIds = snapshots
                .SelectMany(s => s.Positions)
                .Where(p => p.AssetType == AssetType.FUND && !string.IsNullOrWhiteSpace(p.Symbol))
                .Select(p => p.Symbol)
                .Distinct()
                .ToList();

            var fundNavMap = new Dictionary<string, List<DTO.Portfolio.PricePoint>>();
            foreach (var fid in fundIds)
            {
                var nav = await fundData.GetFundNavHistoryAsync(fid, ct);
                if (nav != null) fundNavMap[fid] = nav.Prices;
            }

            var result = new List<NetWorthDataPoint>();
            for (var date = startDate; date <= today; date = date.AddDays(1))
            {
                if (date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday) continue;

                // active snapshot = latest with EffectiveDate <= date
                var active = snapshots.LastOrDefault(s => s.EffectiveDate <= date);
                if (active == null) continue;

                var fx = active.UsdTwdRate > 0 ? active.UsdTwdRate : 32m;
                decimal twStock = 0, usStock = 0, cash = 0, fund = 0;

                foreach (var p in active.Positions)
                {
                    var fxMul = p.Currency == "USD" ? fx : 1m;

                    if (p.AssetType == AssetType.CASH)
                    {
                        cash += p.Quantity * fxMul;
                    }
                    else if (p.AssetType == AssetType.FUND)
                    {
                        decimal nav = p.UnitPrice ?? 0; // fallback: flat value stored at save time
                        if (!string.IsNullOrWhiteSpace(p.Symbol) &&
                            fundNavMap.TryGetValue(p.Symbol, out var navPrices))
                        {
                            // Both sides are ISO yyyy-MM-dd strings → ordinal compare is chronological.
                            var dayKey = date.ToString("yyyy-MM-dd");
                            var navClose = navPrices
                                .Where(x => string.CompareOrdinal(x.Date, dayKey) <= 0)
                                .OrderByDescending(x => x.Date, StringComparer.Ordinal)
                                .FirstOrDefault()?.Close ?? 0;
                            if (navClose > 0) nav = navClose;
                        }
                        if (nav > 0) fund += nav * p.Quantity * fxMul;
                    }
                    else // STOCK
                    {
                        if (!priceMap.TryGetValue(p.Symbol, out var prices)) continue;
                        var dt = date.ToDateTime(TimeOnly.MinValue);
                        var close = prices
                            .Where(x => x.Date.Date <= dt)
                            .OrderByDescending(x => x.Date)
                            .FirstOrDefault()?.Close ?? 0;
                        if (close <= 0) continue;
                        var v = close * p.Quantity * fxMul;
                        if (p.Market == "US") usStock += v; else twStock += v;
                    }
                }

                var total = twStock + usStock + cash + fund;
                if (total <= 0) continue;

                result.Add(new NetWorthDataPoint
                {
                    Date = date.ToString("yyyy-MM-dd"),
                    TotalTWD = total,
                    TwStockTWD = twStock,
                    UsStockTWD = usStock,
                    CashTWD = cash,
                    FundTWD = fund,
                });
            }

            return result;
        }

        // ── Lookup + price history ───────────────────────────────────────────

        public async Task<StockLookupDto?> LookupStockAsync(string market, string symbol, CancellationToken ct = default)
        {
            try
            {
                var region = providerFactory.DetectRegion(symbol);
                var provider = providerFactory.GetMarketDataProvider(region);
                var data = await provider.FetchAsync(symbol, ct);
                var lastPrice = data.HistoricalPrices.LastOrDefault()?.Close ?? 0;
                return new StockLookupDto
                {
                    Symbol = symbol.ToUpperInvariant(),
                    CompanyName = data.CompanyName,
                    CurrentPrice = lastPrice,
                    Currency = market.ToUpperInvariant() == "US" ? "USD" : "TWD",
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
            var snapshot = await LatestSnapshotAsync(userId, ct);
            var sym = symbol.ToUpperInvariant();
            var isFund = market.ToUpperInvariant() == "FUND";

            var pos = snapshot?.Positions.FirstOrDefault(p => isFund
                ? p.AssetType == AssetType.FUND && p.Symbol == sym
                : p.AssetType == AssetType.STOCK && p.Market == market.ToUpperInvariant() && p.Symbol == sym);
            if (pos == null) return null;

            var avgCost = pos.CostBasis.HasValue && pos.Quantity > 0
                ? pos.CostBasis.Value / pos.Quantity
                : (decimal?)null;

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
                    AvgCost = avgCost,
                    Prices = nav.Prices,
                };
            }

            var region = providerFactory.DetectRegion(symbol);
            var provider = providerFactory.GetMarketDataProvider(region);
            var data = await provider.FetchAsync(symbol, ct);

            return new HoldingPriceHistoryDto
            {
                Symbol = pos.Symbol,
                CompanyName = pos.Name,
                Market = pos.Market,
                Currency = pos.Currency,
                Quantity = pos.Quantity,
                AvgCost = avgCost,
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

        public Task<List<FundSearchItem>> SearchFundsAsync(string keyword, CancellationToken ct = default) =>
            fundData.SearchFundsAsync(keyword, ct);

        // ── Stock search (autocomplete by code or name) ──────────────────────

        public async Task<List<StockSearchItem>> SearchStocksAsync(string market, string keyword, CancellationToken ct = default)
        {
            keyword = (keyword ?? "").Trim();
            if (keyword.Length < 1) return [];
            return market.ToUpperInvariant() == "US"
                ? await SearchUsStocksAsync(keyword, ct)
                : await SearchTwStocksAsync(keyword, ct);
        }

        private async Task<List<StockSearchItem>> SearchTwStocksAsync(string keyword, CancellationToken ct)
        {
            try
            {
                // TWStockLib caches the full list for a day internally.
                var all = await stockService.GetAllStockList();
                var kw = keyword.ToUpperInvariant();
                return all.Values
                    .Where(s => s.Symbol.StartsWith(kw, StringComparison.OrdinalIgnoreCase) || s.Name.Contains(keyword))
                    .OrderBy(s => s.Symbol.StartsWith(kw, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                    .ThenBy(s => s.Symbol, StringComparer.Ordinal)
                    .Take(15)
                    .Select(s => new StockSearchItem { Symbol = s.Symbol, Name = s.Name, Market = "TW" })
                    .ToList();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "TW stock search failed for {Keyword}", keyword);
                return [];
            }
        }

        private static readonly HashSet<string> UsExchanges =
            new(StringComparer.OrdinalIgnoreCase) { "NMS", "NYQ", "NGM", "NCM", "ASE", "PCX", "BTS", "NaN" };

        private async Task<List<StockSearchItem>> SearchUsStocksAsync(string keyword, CancellationToken ct)
        {
            try
            {
                var client = httpClientFactory.CreateClient();
                var url = $"https://query1.finance.yahoo.com/v1/finance/search?q={Uri.EscapeDataString(keyword)}&quotesCount=12&newsCount=0";
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0");
                using var resp = await client.SendAsync(req, ct);
                resp.EnsureSuccessStatusCode();
                var json = await resp.Content.ReadAsStringAsync(ct);

                using var doc = System.Text.Json.JsonDocument.Parse(json);
                var items = new List<StockSearchItem>();
                if (doc.RootElement.TryGetProperty("quotes", out var quotes) &&
                    quotes.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    foreach (var q in quotes.EnumerateArray())
                    {
                        var type = q.TryGetProperty("quoteType", out var t) ? t.GetString() ?? "" : "";
                        if (type != "EQUITY" && type != "ETF") continue;
                        var exch = q.TryGetProperty("exchange", out var e) ? e.GetString() ?? "" : "";
                        if (exch.Length > 0 && !UsExchanges.Contains(exch)) continue;
                        var symbol = q.TryGetProperty("symbol", out var s) ? s.GetString() ?? "" : "";
                        if (string.IsNullOrWhiteSpace(symbol) || symbol.Contains('.')) continue; // skip non-US listings
                        var name = q.TryGetProperty("longname", out var ln) ? ln.GetString() ?? "" : "";
                        if (string.IsNullOrWhiteSpace(name) && q.TryGetProperty("shortname", out var sn))
                            name = sn.GetString() ?? "";
                        items.Add(new StockSearchItem { Symbol = symbol, Name = name, Market = "US" });
                        if (items.Count >= 12) break;
                    }
                }
                return items;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "US stock search failed for {Keyword}", keyword);
                return [];
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private async Task<HoldingSnapshot?> LatestSnapshotAsync(string userId, CancellationToken ct) =>
            await db.HoldingSnapshots
                .Include(s => s.Positions)
                .Where(s => s.UserId == userId)
                .OrderByDescending(s => s.EffectiveDate)
                .FirstOrDefaultAsync(ct);

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
    }
}
