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
        private const string STOCK_CACHE_PREFIX = "portfolio_stock_";
        private static readonly TimeSpan StockCacheTtl = TimeSpan.FromMinutes(30);

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
                    // Defensive: a stock/fund row is meaningless without a symbol, even if the client skipped validation.
                    .Where(p => p.Quantity > 0 && (p.AssetType == AssetType.CASH || !string.IsNullOrWhiteSpace(p.Symbol)))
                    .Select(p => new HoldingPosition
                    {
                        AssetType = p.AssetType,
                        Symbol = (p.Symbol ?? "").ToUpperInvariant(),
                        Name = p.Name ?? "",
                        Market = (p.Market ?? "").ToUpperInvariant(),
                        Currency = string.IsNullOrWhiteSpace(p.Currency) ? "TWD" : p.Currency.ToUpperInvariant(),
                        // Cost may be paid in a different currency than the NAV/price (e.g. USD-priced
                        // fund bought with TWD). Default to the pricing currency when unspecified.
                        CostCurrency = p.AssetType == AssetType.CASH
                            ? null
                            : (string.IsNullOrWhiteSpace(p.CostCurrency)
                                ? (string.IsNullOrWhiteSpace(p.Currency) ? "TWD" : p.Currency)
                                : p.CostCurrency).ToUpperInvariant(),
                        Quantity = p.Quantity,
                        // Fund NAV is fetched live; UnitPrice kept only as a fallback if ever supplied.
                        UnitPrice = p.AssetType == AssetType.FUND ? p.UnitPrice : null,
                        // Optional cost basis drives P&L for both stocks and funds (not cash).
                        CostBasis = p.AssetType == AssetType.CASH ? null : p.CostBasis,
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
                        CostCurrency = p.CostCurrency,
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
                        CostCurrency = p.CostCurrency,
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
            decimal equityExposureTWD = 0; // Σ market value × exposure factor (leverage/bond-adjusted)

            // Prefetch all stock prices and fund NAVs in parallel (cached) before the value loop,
            // instead of awaiting one network call per position serially.
            var stockTask = FetchStocksAsync(
                snapshot.Positions.Where(p => p.AssetType == AssetType.STOCK).Select(p => p.Symbol), ct);
            var fundTask = FetchFundsAsync(
                snapshot.Positions.Where(p => p.AssetType == AssetType.FUND).Select(p => p.Symbol), ct);
            await Task.WhenAll(stockTask, fundTask);
            var stockMap = stockTask.Result;
            var fundMap = fundTask.Result;

            foreach (var p in snapshot.Positions)
            {
                decimal currentPrice;
                bool priceFailed = false;
                // Pricing currency: stocks/cash use the stored currency; funds use the live NAV
                // currency from FundRich (authoritative), not a manually-picked value.
                var priceCurrency = p.Currency;

                if (p.AssetType == AssetType.STOCK)
                {
                    if (stockMap.TryGetValue(p.Symbol, out var data))
                        currentPrice = data.HistoricalPrices.LastOrDefault()?.Close ?? 0;
                    else
                    {
                        currentPrice = 0;
                        priceFailed = true;
                    }
                }
                else if (p.AssetType == AssetType.FUND)
                {
                    // Prefer live NAV from FundRich; fall back to the value stored at save time.
                    currentPrice = p.UnitPrice ?? 0;
                    if (!string.IsNullOrWhiteSpace(p.Symbol))
                    {
                        if (fundMap.TryGetValue(p.Symbol, out var nav))
                        {
                            priceCurrency = nav.Currency;
                            var latest = nav.Prices.LastOrDefault()?.Close;
                            if (latest is > 0)
                                currentPrice = latest.Value;
                            else if (currentPrice <= 0)
                                priceFailed = true;
                        }
                        else if (currentPrice <= 0)
                        {
                            priceFailed = true;
                        }
                    }
                }
                else // CASH
                {
                    currentPrice = 1;
                }

                // A non-cash position with no usable price has no real market value.
                // Treat it as "price unavailable" so we don't book a phantom loss of the full cost.
                if (p.AssetType != AssetType.CASH && currentPrice <= 0)
                    priceFailed = true;

                var fxMul = priceCurrency == "USD" ? fx : 1m;
                var valueLocal = p.AssetType == AssetType.CASH
                    ? p.Quantity
                    : currentPrice * p.Quantity;
                var valueTWD = valueLocal * fxMul;

                // Cost is converted by its own currency, which may differ from the pricing currency.
                var costFxMul = (p.CostCurrency ?? p.Currency) == "USD" ? fx : 1m;

                decimal? posCostTWD = null, posPnlTWD = null, posPnlPct = null;
                if (p.AssetType != AssetType.CASH && p.CostBasis.HasValue)
                {
                    posCostTWD = p.CostBasis.Value * costFxMul;
                    // Only compute P&L when we have a real price; otherwise valueTWD is 0
                    // and we'd report a fake loss equal to the entire cost basis.
                    if (!priceFailed)
                    {
                        posPnlTWD = valueTWD - posCostTWD;
                        posPnlPct = posCostTWD > 0 ? Math.Round(posPnlTWD.Value / posCostTWD.Value * 100, 2) : 0;
                        costBasisTWD += posCostTWD.Value;
                        pnlTWD += posPnlTWD.Value;
                    }
                }

                switch (p.AssetType)
                {
                    case AssetType.STOCK when p.Market == "US": usStock += valueTWD; break;
                    case AssetType.STOCK: twStock += valueTWD; break;
                    case AssetType.CASH: cash += valueTWD; break;
                    case AssetType.FUND: fund += valueTWD; break;
                }

                // Market exposure: leveraged ETFs amplify, bond/cash contribute nothing.
                var exposureFactor = PortfolioExposure.Factor(p.AssetType, p.Name, p.Symbol, p.Market);
                equityExposureTWD += valueTWD * exposureFactor;

                positions.Add(new PositionDto
                {
                    AssetType = p.AssetType,
                    Symbol = p.Symbol,
                    Name = p.Name,
                    Market = p.Market,
                    Currency = priceCurrency,
                    CostCurrency = p.CostCurrency,
                    Quantity = p.Quantity,
                    CurrentPrice = currentPrice,
                    MarketValueLocal = valueLocal,
                    MarketValueTWD = valueTWD,
                    CostBasisTWD = posCostTWD,
                    UnrealizedPnlTWD = posPnlTWD,
                    UnrealizedPnlPct = posPnlPct,
                    ExposureFactor = exposureFactor,
                    PriceFetchFailed = priceFailed,
                });
            }

            var netWorthTWD = twStock + usStock + cash + fund;

            var annualizedReturnPct = await ComputeAnnualizedReturnAsync(
                userId, fx, costBasisTWD + pnlTWD, ct);

            return new PortfolioSummaryDto
            {
                EffectiveDate = snapshot.EffectiveDate,
                UsdTwdRate = fx,
                TotalNetWorthTWD = netWorthTWD,
                TwStockTWD = twStock,
                UsStockTWD = usStock,
                CashTWD = cash,
                FundTWD = fund,
                CostBasisTWD = costBasisTWD,
                UnrealizedPnlTWD = pnlTWD,
                UnrealizedPnlPct = costBasisTWD > 0 ? Math.Round(pnlTWD / costBasisTWD * 100, 2) : 0,
                AnnualizedReturnPct = annualizedReturnPct,
                MarketExposureTWD = equityExposureTWD,
                ExposureRatioPct = netWorthTWD > 0 ? Math.Round(equityExposureTWD / netWorthTWD * 100, 1) : 0,
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

            var priceMap = (await FetchStocksAsync(stockSymbols, ct))
                .ToDictionary(kv => kv.Key, kv => kv.Value.HistoricalPrices);

            // Fund NAV histories — gives funds a real daily curve instead of a flat line.
            var fundIds = snapshots
                .SelectMany(s => s.Positions)
                .Where(p => p.AssetType == AssetType.FUND && !string.IsNullOrWhiteSpace(p.Symbol))
                .Select(p => p.Symbol)
                .Distinct()
                .ToList();

            var fundNavMap = await FetchFundsAsync(fundIds, ct);

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
                        var fundFxMul = fxMul; // default to stored currency until NAV tells us otherwise
                        if (!string.IsNullOrWhiteSpace(p.Symbol) &&
                            fundNavMap.TryGetValue(p.Symbol, out var navHist))
                        {
                            fundFxMul = navHist.Currency == "USD" ? fx : 1m;
                            // Both sides are ISO yyyy-MM-dd strings → ordinal compare is chronological.
                            var dayKey = date.ToString("yyyy-MM-dd");
                            var navClose = navHist.Prices
                                .Where(x => string.CompareOrdinal(x.Date, dayKey) <= 0)
                                .OrderByDescending(x => x.Date, StringComparer.Ordinal)
                                .FirstOrDefault()?.Close ?? 0;
                            if (navClose > 0) nav = navClose;
                        }
                        if (nav > 0) fund += nav * p.Quantity * fundFxMul;
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

            var data = await FetchStockDataAsync(symbol, ct);
            if (data == null) return null;

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

        /// <summary>
        /// Money-weighted annualized return (XIRR) on invested capital. Changes in recorded cost
        /// basis between snapshots are treated as dated contributions; the current value of
        /// cost-bearing holdings is the terminal inflow. Returns null when not computable.
        /// </summary>
        private async Task<decimal?> ComputeAnnualizedReturnAsync(
            string userId, decimal fallbackFx, decimal terminalValueTWD, CancellationToken ct)
        {
            if (terminalValueTWD <= 0) return null;

            var snaps = await db.HoldingSnapshots
                .Include(s => s.Positions)
                .Where(s => s.UserId == userId)
                .OrderBy(s => s.EffectiveDate)
                .ToListAsync(ct);
            if (snaps.Count == 0) return null;

            var flows = new List<(DateOnly, double)>();
            decimal prevCost = 0;
            foreach (var s in snaps)
            {
                var snapFx = s.UsdTwdRate > 0 ? s.UsdTwdRate : fallbackFx;
                var snapCost = s.Positions
                    .Where(p => p.AssetType != AssetType.CASH && p.CostBasis.HasValue)
                    .Sum(p => p.CostBasis!.Value * ((p.CostCurrency ?? p.Currency) == "USD" ? snapFx : 1m));
                var delta = snapCost - prevCost;
                if (delta != 0) flows.Add((s.EffectiveDate, (double)(-delta))); // contribution = outflow
                prevCost = snapCost;
            }

            flows.Add((DateOnly.FromDateTime(DateTime.UtcNow), (double)terminalValueTWD));

            var rate = Xirr.Compute(flows);
            if (rate is null || double.IsNaN(rate.Value) || double.IsInfinity(rate.Value)) return null;
            return Math.Round((decimal)(rate.Value * 100), 2);
        }

        /// <summary>
        /// Fetch one stock's market data, cached for 30 min so the same symbol isn't refetched
        /// across the summary, the net-worth chart and the detail drawer. Returns null on failure.
        /// </summary>
        private async Task<DTO.Finance.MarketData?> FetchStockDataAsync(string symbol, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(symbol)) return null;

            var key = STOCK_CACHE_PREFIX + symbol;
            if (cache.TryGetValue(key, out DTO.Finance.MarketData? cached) && cached != null)
                return cached;

            try
            {
                var region = providerFactory.DetectRegion(symbol);
                var provider = providerFactory.GetMarketDataProvider(region);
                var data = await provider.FetchAsync(symbol, ct);
                // Only cache a usable result; an empty fetch shouldn't suppress the next retry.
                if (data.HistoricalPrices.Count > 0)
                    cache.Set(key, data, StockCacheTtl);
                return data;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to fetch price for {Symbol}", symbol);
                return null;
            }
        }

        /// <summary>Fetch every distinct stock symbol in parallel; failed symbols are omitted.</summary>
        private async Task<Dictionary<string, DTO.Finance.MarketData>> FetchStocksAsync(
            IEnumerable<string> symbols, CancellationToken ct)
        {
            var distinct = symbols.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().ToList();
            var results = await Task.WhenAll(
                distinct.Select(async s => (symbol: s, data: await FetchStockDataAsync(s, ct))));
            return results
                .Where(r => r.data != null)
                .ToDictionary(r => r.symbol, r => r.data!);
        }

        /// <summary>Fetch every distinct fund's NAV history in parallel (FundDataService caches each).</summary>
        private async Task<Dictionary<string, FundNavHistory>> FetchFundsAsync(
            IEnumerable<string> fundIds, CancellationToken ct)
        {
            var distinct = fundIds.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().ToList();
            var results = await Task.WhenAll(
                distinct.Select(async f => (fundId: f, nav: await fundData.GetFundNavHistoryAsync(f, ct))));
            return results
                .Where(r => r.nav != null)
                .ToDictionary(r => r.fundId, r => r.nav!);
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
    }
}
