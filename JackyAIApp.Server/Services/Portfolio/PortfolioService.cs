using JackyAIApp.Server.Common;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.Data.Models.Portfolio;
using JackyAIApp.Server.DTO;
using JackyAIApp.Server.DTO.Portfolio;
using JackyAIApp.Server.Services;
using JackyAIApp.Server.Services.Finance.DataProviders;
using JackyAIApp.Server.Services.OpenAI;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using System.Text;

namespace JackyAIApp.Server.Services.Portfolio
{
    public class PortfolioService(
        AzureSQLDBContext db,
        IMarketDataProviderFactory providerFactory,
        IFundDataService fundData,
        IDividendService dividendData,
        IStockCategoryService categoryService,
        TWStockLib.Services.IStockMarketService stockService,
        IHttpClientFactory httpClientFactory,
        IMemoryCache cache,
        IOpenAIPromptService openAI,
        ICreditService creditService,
        ILogger<PortfolioService> logger) : IPortfolioService
    {
        private const string FX_URL = "https://open.er-api.com/v6/latest/USD";
        private const string FX_CACHE_KEY = "portfolio_usd_twd_rate";
        private const string STOCK_CACHE_PREFIX = "portfolio_stock_";

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
                    // CASH and LIABILITY are symbol-less by design (amount only).
                    .Where(p => p.Quantity > 0 && (p.AssetType == AssetType.CASH || p.AssetType == AssetType.LIABILITY
                        || !string.IsNullOrWhiteSpace(p.Symbol)))
                    .Select(p => new HoldingPosition
                    {
                        AssetType = p.AssetType,
                        Symbol = (p.Symbol ?? "").ToUpperInvariant(),
                        Name = p.Name ?? "",
                        Market = (p.Market ?? "").ToUpperInvariant(),
                        Currency = string.IsNullOrWhiteSpace(p.Currency) ? "TWD" : p.Currency.ToUpperInvariant(),
                        // Cost may be paid in a different currency than the NAV/price (e.g. USD-priced
                        // fund bought with TWD). Default to the pricing currency when unspecified.
                        // CASH/LIABILITY have no cost-basis concept, so no cost currency either.
                        CostCurrency = p.AssetType is AssetType.CASH or AssetType.LIABILITY
                            ? null
                            : (string.IsNullOrWhiteSpace(p.CostCurrency)
                                ? (string.IsNullOrWhiteSpace(p.Currency) ? "TWD" : p.Currency)
                                : p.CostCurrency).ToUpperInvariant(),
                        Quantity = p.Quantity,
                        // Fund NAV is fetched live; UnitPrice kept only as a fallback if ever supplied.
                        UnitPrice = p.AssetType == AssetType.FUND ? p.UnitPrice : null,
                        // Optional cost basis drives P&L for stocks/funds. Never set for CASH/LIABILITY —
                        // debt has no cost basis and must never enter P&L or XIRR.
                        CostBasis = p.AssetType is AssetType.CASH or AssetType.LIABILITY ? null : p.CostBasis,
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

            // Current valuation always uses the live rate; the rate stored on each snapshot is only
            // for reconstructing historical net worth (a stale fallback-32 shouldn't stick forever).
            var fx = await FetchUsdTwdRateAsync(ct);
            var positions = new List<PositionDto>();
            decimal twStock = 0, usStock = 0, cash = 0, fund = 0, liability = 0;
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
                else // CASH or LIABILITY — always "priced" at 1, no external lookup needed.
                {
                    currentPrice = 1;
                }

                // A priceable (non-cash, non-liability) position with no usable price has no real
                // market value. Treat it as "price unavailable" so we don't book a phantom loss of
                // the full cost.
                if (p.AssetType is not (AssetType.CASH or AssetType.LIABILITY) && currentPrice <= 0)
                    priceFailed = true;

                var fxMul = priceCurrency == "USD" ? fx : 1m;
                // LIABILITY is the one place a position's value goes negative — this is the sole
                // sign-flip point; every downstream sum (net worth, history, exports) just adds
                // valueTWD and is correct with no special-casing.
                var valueLocal = p.AssetType switch
                {
                    AssetType.CASH => p.Quantity,
                    AssetType.LIABILITY => -p.Quantity,
                    _ => currentPrice * p.Quantity,
                };
                var valueTWD = valueLocal * fxMul;

                // Cost is converted by its own currency, which may differ from the pricing currency.
                var costFxMul = (p.CostCurrency ?? p.Currency) == "USD" ? fx : 1m;

                decimal? posCostTWD = null, posPnlTWD = null, posPnlPct = null;
                // LIABILITY.CostBasis is always null (enforced at save time), so it naturally
                // never enters P&L/XIRR without needing an explicit exclusion here.
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
                    // valueTWD is already negative here — this is display total (owed amount),
                    // shown positive by negating at the DTO boundary below.
                    case AssetType.LIABILITY: liability += valueTWD; break;
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

            // liability is already negative (see the valuation loop above), so this is a plain sum —
            // no special-casing needed for net worth to correctly net out debt.
            var netWorthTWD = twStock + usStock + cash + fund + liability;

            // Total (含息) return: walk the whole snapshot timeline so a dividend counts against
            // whatever quantity was actually held on its ex-date, not today's quantity.
            var allSnapshots = await AllSnapshotsAsync(userId, ct);
            var (totalDividendsTWD, dividendsByPosition, dividendFlows) =
                await ComputeDividendsAsync(allSnapshots, fx, fundMap, ct);
            foreach (var p in positions)
            {
                if (dividendsByPosition.TryGetValue((p.AssetType, p.Symbol, p.Market), out var div))
                    p.DividendsTWD = div;
            }
            var totalReturnPnlTWD = pnlTWD + totalDividendsTWD;

            // XIRR is 含息 (total return): dividends are real cash inflows, so leaving them out
            // would understate the annualized return relative to what the user actually earned.
            var annualizedReturnPct = ComputeAnnualizedReturn(
                allSnapshots, fx, costBasisTWD + pnlTWD, dividendFlows);

            // Day change: re-value today's exact holdings at the previous trading day's close,
            // reusing the price histories already fetched above (no extra network calls). Holding
            // quantities constant isolates price movement — same-day trades and FX drift are not
            // modeled, matching the "just the total, skip the breakdown" scope this was asked for.
            //
            // Guard: "today's" price is whatever the provider's most recent published bar is, which
            // — before today's TWSE/NAV data has actually landed for the day — is still yesterday's
            // close. In that case "today" and "previous trading day" resolve to the exact same data
            // point and would silently show a guaranteed $0/0.00% change that reads as "flat" rather
            // than "no new data yet". Only compute a day change once at least one holding has a
            // price genuinely newer than the previous trading day.
            var prevTradingDay = PreviousTradingDay(DateOnly.FromDateTime(DateTime.UtcNow));
            decimal? dayChangeTWD = null, dayChangePct = null;
            if (HasNewerPriceThan(snapshot.Positions, prevTradingDay, stockMap, fundMap))
            {
                var prevNetWorthTWD = ValueAsOfDate(snapshot.Positions, prevTradingDay, fx, stockMap, fundMap);
                if (prevNetWorthTWD is > 0)
                {
                    dayChangeTWD = netWorthTWD - prevNetWorthTWD.Value;
                    dayChangePct = Math.Round(dayChangeTWD.Value / prevNetWorthTWD.Value * 100, 2);
                }
            }

            return new PortfolioSummaryDto
            {
                EffectiveDate = snapshot.EffectiveDate,
                UsdTwdRate = fx,
                TotalNetWorthTWD = netWorthTWD,
                TwStockTWD = twStock,
                UsStockTWD = usStock,
                CashTWD = cash,
                FundTWD = fund,
                // Display as a positive "amount owed"; liability itself is negative for the net-worth sum.
                LiabilityTWD = -liability,
                CostBasisTWD = costBasisTWD,
                UnrealizedPnlTWD = pnlTWD,
                UnrealizedPnlPct = costBasisTWD > 0 ? Math.Round(pnlTWD / costBasisTWD * 100, 2) : 0,
                TotalDividendsTWD = totalDividendsTWD,
                TotalReturnPnlTWD = totalReturnPnlTWD,
                TotalReturnPct = costBasisTWD > 0 ? Math.Round(totalReturnPnlTWD / costBasisTWD * 100, 2) : 0,
                AnnualizedReturnPct = annualizedReturnPct,
                MarketExposureTWD = equityExposureTWD,
                ExposureRatioPct = netWorthTWD > 0 ? Math.Round(equityExposureTWD / netWorthTWD * 100, 1) : 0,
                DayChangeTWD = dayChangeTWD,
                DayChangePct = dayChangePct,
                Positions = positions.OrderByDescending(p => p.MarketValueTWD).ToList(),
            };
        }

        // ── Net worth history ────────────────────────────────────────────────

        public async Task<List<NetWorthDataPoint>> GetNetWorthHistoryAsync(
            string userId, int days, NetWorthGroupBy groupBy = NetWorthGroupBy.AssetType, CancellationToken ct = default)
        {
            days = Math.Clamp(days, 7, 180);

            var snapshots = await AllSnapshotsAsync(userId, ct);

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

            // Sorted ascending by date (defensively re-sorted here even though upstream fetchers
            // already do this, since the cursor walk below depends on it) so each symbol's "price
            // as of date" lookup can advance with a forward-only cursor instead of re-filtering +
            // re-sorting the whole history on every single day of the loop below.
            var priceMap = (await FetchStocksAsync(stockSymbols, ct))
                .ToDictionary(kv => kv.Key, kv => kv.Value.HistoricalPrices.OrderBy(x => x.Date).ToList());
            var stockCursors = priceMap.Keys.ToDictionary(k => k, _ => 0);

            // Fund NAV histories — gives funds a real daily curve instead of a flat line.
            var fundIds = snapshots
                .SelectMany(s => s.Positions)
                .Where(p => p.AssetType == AssetType.FUND && !string.IsNullOrWhiteSpace(p.Symbol))
                .Select(p => p.Symbol)
                .Distinct()
                .ToList();

            // FetchFundsAsync's results are cached (FundDataService, up to 6h) and shared with other
            // callers — build separate sorted copies here rather than reordering navHist.Prices
            // in place, which would mutate the shared cached instance.
            var fundNavMap = await FetchFundsAsync(fundIds, ct);
            var fundPriceMap = fundNavMap.ToDictionary(
                kv => kv.Key,
                kv => kv.Value.Prices.OrderBy(x => x.Date, StringComparer.Ordinal).ToList());
            var fundCursors = fundNavMap.Keys.ToDictionary(k => k, _ => 0);

            // Only resolved when actually needed (groupBy: category) — one AI-classification call
            // per distinct STOCK/FUND symbol, warmed concurrently up front just like the price
            // fetches above rather than sequentially inside the day loop. CASH/LIABILITY don't need
            // a lookup (mapped directly by the switch below); most calls are cache hits after the
            // first time a symbol is ever seen (StockCategoryService persists to StockSymbolInfo).
            var categoryMap = new Dictionary<string, PortfolioCategory>();
            if (groupBy == NetWorthGroupBy.Category)
            {
                var classifiable = snapshots
                    .SelectMany(s => s.Positions)
                    .Where(p => p.AssetType is AssetType.STOCK or AssetType.FUND && !string.IsNullOrWhiteSpace(p.Symbol))
                    .GroupBy(p => p.Symbol)
                    .Select(g => g.Last()) // most recent name/market info for that symbol
                    .ToList();
                var resolved = await Task.WhenAll(classifiable.Select(async p =>
                {
                    var (category, _) = await categoryService.GetCategoryAsync(p.AssetType, p.Symbol, p.Name, p.Market, ct);
                    return (p.Symbol, Category: category);
                }));
                categoryMap = resolved.ToDictionary(r => r.Symbol, r => r.Category);
            }

            // Snapshots are sorted ascending by EffectiveDate (AllSnapshotsAsync), and `date` only
            // ever increases below — walk a single cursor forward instead of re-scanning the whole
            // snapshot list ("active = latest with EffectiveDate <= date") on every day.
            var snapshotIdx = 0;

            var result = new List<NetWorthDataPoint>();
            for (var date = startDate; date <= today; date = date.AddDays(1))
            {
                if (date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday) continue;

                while (snapshotIdx + 1 < snapshots.Count && snapshots[snapshotIdx + 1].EffectiveDate <= date)
                    snapshotIdx++;
                var active = snapshots[snapshotIdx].EffectiveDate <= date ? snapshots[snapshotIdx] : null;
                if (active == null) continue;

                var fx = active.UsdTwdRate > 0 ? active.UsdTwdRate : 32m;
                var groups = new Dictionary<string, decimal>();
                decimal total = 0;
                var dt = date.ToDateTime(TimeOnly.MinValue);
                var dayKey = date.ToString("yyyy-MM-dd");

                void Add(string key, decimal valueTWD)
                {
                    groups[key] = groups.GetValueOrDefault(key) + valueTWD;
                    total += valueTWD;
                }

                foreach (var p in active.Positions)
                {
                    var fxMul = p.Currency == "USD" ? fx : 1m;

                    if (p.AssetType == AssetType.CASH)
                    {
                        Add(GroupKey(groupBy, p, categoryMap, PortfolioCategory.Cash), p.Quantity * fxMul);
                    }
                    else if (p.AssetType == AssetType.LIABILITY)
                    {
                        // Negative — the sole sign-flip point, same invariant as the summary
                        // endpoint's valuation loop. Every downstream sum (here: `total`) just adds.
                        Add(GroupKey(groupBy, p, categoryMap, PortfolioCategory.Liability), -(p.Quantity * fxMul));
                    }
                    else if (p.AssetType == AssetType.FUND)
                    {
                        decimal nav = p.UnitPrice ?? 0; // fallback: flat value stored at save time
                        var fundFxMul = fxMul; // default to stored currency until NAV tells us otherwise
                        if (!string.IsNullOrWhiteSpace(p.Symbol) &&
                            fundNavMap.TryGetValue(p.Symbol, out var navHist))
                        {
                            fundFxMul = navHist.Currency == "USD" ? fx : 1m;
                            var prices = fundPriceMap[p.Symbol];
                            if (prices.Count > 0)
                            {
                                var idx = fundCursors[p.Symbol];
                                // Both sides are ISO yyyy-MM-dd strings → ordinal compare is chronological.
                                while (idx + 1 < prices.Count &&
                                       string.CompareOrdinal(prices[idx + 1].Date, dayKey) <= 0)
                                    idx++;
                                fundCursors[p.Symbol] = idx;
                                if (string.CompareOrdinal(prices[idx].Date, dayKey) <= 0 && prices[idx].Close > 0)
                                    nav = prices[idx].Close;
                            }
                        }
                        if (nav > 0)
                            Add(GroupKey(groupBy, p, categoryMap, PortfolioCategory.Other), nav * p.Quantity * fundFxMul);
                    }
                    else // STOCK
                    {
                        if (!priceMap.TryGetValue(p.Symbol, out var prices) || prices.Count == 0) continue;
                        var idx = stockCursors[p.Symbol];
                        while (idx + 1 < prices.Count && prices[idx + 1].Date.Date <= dt)
                            idx++;
                        stockCursors[p.Symbol] = idx;
                        if (prices[idx].Date.Date > dt) continue; // no price at/before this date yet
                        var close = prices[idx].Close;
                        if (close <= 0) continue;
                        Add(GroupKey(groupBy, p, categoryMap, PortfolioCategory.Other), close * p.Quantity * fxMul);
                    }
                }

                // Only "== 0" (not "<= 0"): a day where nothing could be priced lands on exactly
                // 0 and should be skipped as no-data. A real negative total (debt exceeding assets)
                // is a valid data point and must not be dropped from the chart.
                if (total == 0) continue;

                result.Add(new NetWorthDataPoint
                {
                    Date = date.ToString("yyyy-MM-dd"),
                    TotalTWD = total,
                    Groups = groups.Select(kv => new NetWorthGroupValue { Key = kv.Key, ValueTWD = kv.Value }).ToList(),
                });
            }

            return result;
        }

        /// <summary>Group key for one position under the requested groupBy mode. `fallbackCategory`
        /// is only used in Category mode, for asset types that skip the categoryMap lookup (CASH/
        /// LIABILITY are mapped directly to Cash/Liability without ever entering categoryMap; a
        /// STOCK/FUND symbol somehow missing from a pre-warmed categoryMap falls back to Other
        /// rather than throwing).</summary>
        private static string GroupKey(
            NetWorthGroupBy groupBy, HoldingPosition p,
            Dictionary<string, PortfolioCategory> categoryMap, PortfolioCategory fallbackCategory)
        {
            return groupBy switch
            {
                NetWorthGroupBy.Category => p.AssetType switch
                {
                    AssetType.CASH => nameof(PortfolioCategory.Cash),
                    AssetType.LIABILITY => nameof(PortfolioCategory.Liability),
                    _ => (categoryMap.TryGetValue(p.Symbol, out var cat) ? cat : fallbackCategory).ToString(),
                },
                NetWorthGroupBy.Currency => p.Currency == "USD" ? "USD" : "TWD",
                _ => p.AssetType switch
                {
                    AssetType.CASH => "Cash",
                    AssetType.LIABILITY => "Liability",
                    AssetType.FUND => "Fund",
                    _ => p.Market == "US" ? "UsStock" : "TwStock", // STOCK
                },
            };
        }

        // ── Lookup + price history ───────────────────────────────────────────

        public async Task<StockLookupDto?> LookupStockAsync(string market, string symbol, CancellationToken ct = default)
        {
            var data = await FetchStockDataAsync(symbol, ct);
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
            var snapshot = await LatestSnapshotAsync(userId, ct);
            var sym = symbol.ToUpperInvariant();
            var isFund = market.ToUpperInvariant() == "FUND";

            var pos = snapshot?.Positions.FirstOrDefault(p => isFund
                ? p.AssetType == AssetType.FUND && p.Symbol == sym
                : p.AssetType == AssetType.STOCK && p.Market == market.ToUpperInvariant() && p.Symbol == sym);
            if (pos == null) return null;

            var rawAvg = pos.CostBasis.HasValue && pos.Quantity > 0
                ? pos.CostBasis.Value / pos.Quantity
                : (decimal?)null;

            // The chart's prices are in the pricing currency, but the cost may have been paid in
            // another (e.g. USD-priced fund bought with TWD). Convert the average cost into the
            // chart currency so the cost line and "% vs 成本" line up with the price axis.
            var fx = await FetchUsdTwdRateAsync(ct);
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

            var data = await FetchStockDataAsync(symbol, ct);
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

        // ── AI technical summary (朱家泓觀點) ─────────────────────────────────

        public async Task<(PortfolioAiSummaryDto? Result, string? Error)> GetAiTechSummaryAsync(
            string userId, string market, string symbol, CancellationToken ct = default)
        {
            var snapshot = await LatestSnapshotAsync(userId, ct);
            var sym = symbol.ToUpperInvariant();
            var isFund = market.ToUpperInvariant() == "FUND";
            var pos = snapshot?.Positions.FirstOrDefault(p => isFund
                ? p.AssetType == AssetType.FUND && p.Symbol == sym
                : p.AssetType == AssetType.STOCK && p.Market == market.ToUpperInvariant() && p.Symbol == sym);
            if (pos == null) return (null, "找不到此持倉");

            if (!await creditService.ConsumeCreditsAsync(userId, CreditCosts.PortfolioAiSummary, "portfolio_ai_summary", "Portfolio AI technical summary"))
            {
                return (null, $"Insufficient credits. Required: {CreditCosts.PortfolioAiSummary}.");
            }

            List<decimal> closes;
            List<decimal>? highs = null, lows = null;
            List<long>? vols = null;
            string currency;

            if (isFund)
            {
                var nav = await fundData.GetFundNavHistoryAsync(pos.Symbol, ct);
                if (nav == null || nav.Prices.Count == 0) return (null, "無歷史價格資料");
                closes = nav.Prices.Select(p => p.Close).ToList();
                currency = nav.Currency;
            }
            else
            {
                var data = await FetchStockDataAsync(symbol, ct);
                if (data == null || data.HistoricalPrices.Count == 0) return (null, "無歷史價格資料");
                var hp = data.HistoricalPrices; // ascending by date
                closes = hp.Select(p => p.Close).ToList();
                highs = hp.Select(p => p.High).ToList();
                lows = hp.Select(p => p.Low).ToList();
                vols = hp.Select(p => p.Volume).ToList();
                currency = pos.Currency;
            }

            var context = BuildTaContext(pos.Name, sym, isFund ? "基金" : pos.Market, currency, closes, highs, lows, vols);
            var userMessage = $"請以朱家泓技術分析觀點，總結「{sym} {pos.Name}」。以下為技術數據：\n\n{context}";

            return await openAI.GetCompletionAsync<PortfolioAiSummaryDto>(
                "Prompt/Portfolio/ZhuTechAnalysisSystem.txt", userMessage, NewModels.GPT_4_1_NANO);
        }

        /// <summary>Builds a compact technical-indicator brief (MAs, alignment, S/R, volume) for the LLM.</summary>
        private static string BuildTaContext(string name, string symbol, string market, string currency,
            List<decimal> closes, List<decimal>? highs, List<decimal>? lows, List<long>? vols)
        {
            decimal Ma(int n) => closes.Count >= n ? Math.Round(closes.TakeLast(n).Average(), 2) : 0m;
            var last = Math.Round(closes[^1], 2);
            var sb = new StringBuilder();
            sb.AppendLine($"標的：{symbol} {name}（{market}，計價 {currency}）");
            sb.AppendLine($"資料筆數：{closes.Count} 個交易日（由舊至新）");
            sb.AppendLine($"最新收盤/淨值：{last}");

            decimal ma5 = Ma(5), ma10 = Ma(10), ma20 = Ma(20), ma60 = Ma(60);
            sb.AppendLine($"5日均：{ma5}；10日均：{ma10}；20日均(月線/生命線)：{ma20}；60日均(季線)：{ma60}");

            var arrange = "資料不足";
            if (ma5 > 0 && ma10 > 0 && ma20 > 0 && ma60 > 0)
                arrange = ma5 > ma10 && ma10 > ma20 && ma20 > ma60 ? "均線多頭排列 (5>10>20>60)"
                        : ma5 < ma10 && ma10 < ma20 && ma20 < ma60 ? "均線空頭排列 (5<10<20<60)"
                        : "均線糾結/交錯（盤整）";
            sb.AppendLine($"均線排列：{arrange}");
            if (ma20 > 0)
                sb.AppendLine($"股價相對月線：{(last >= ma20 ? "站上" : "跌破")}月線，乖離 {Math.Round((last - ma20) / ma20 * 100, 1)}%");

            var win = Math.Min(20, closes.Count);
            var recent = closes.TakeLast(win).ToList();
            var hi20 = Math.Round(highs != null ? highs.TakeLast(win).Max() : recent.Max(), 2);
            var lo20 = Math.Round(lows != null ? lows.TakeLast(win).Min() : recent.Min(), 2);
            sb.AppendLine($"近{win}日高點(壓力)：{hi20}；近{win}日低點(支撐)：{lo20}");

            var win60 = Math.Min(60, closes.Count);
            var hi60 = Math.Round((highs ?? closes).TakeLast(win60).Max(), 2);
            var lo60 = Math.Round((lows ?? closes).TakeLast(win60).Min(), 2);
            sb.AppendLine($"近{win60}日高：{hi60}；近{win60}日低：{lo60}");

            sb.AppendLine($"最近5筆收盤：{string.Join(", ", closes.TakeLast(5).Select(x => Math.Round(x, 2)))}");

            if (vols != null && vols.Count >= 6)
            {
                var v5 = vols.TakeLast(5).Average();
                var vPrev = vols.Count >= 10 ? vols.SkipLast(5).TakeLast(5).Average() : v5;
                sb.AppendLine($"近5日均量：{(long)v5}；前5日均量：{(long)vPrev}（{(v5 >= vPrev ? "量增" : "量縮")}）");
            }

            return sb.ToString();
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
                var allResult = await stockService.GetAllStockListAsync(ct: ct);
                if (!allResult.IsSuccess)
                {
                    logger.LogWarning(
                        "TW stock list fetch failed for {Keyword}: {ErrorCode} - {ErrorMessage}",
                        keyword, allResult.ErrorCode, allResult.ErrorMessage);
                    return [];
                }

                var kw = keyword.ToUpperInvariant();
                return allResult.Value!.Values
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
                var client = httpClientFactory.CreateClient("ExternalReadOnly");
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

        /// <summary>Every snapshot for a user, oldest first — the full timeline, not just the latest.</summary>
        private Task<List<HoldingSnapshot>> AllSnapshotsAsync(string userId, CancellationToken ct) =>
            db.HoldingSnapshots
                .Include(s => s.Positions)
                .Where(s => s.UserId == userId)
                .OrderBy(s => s.EffectiveDate)
                .ToListAsync(ct);

        /// <summary>
        /// Cash dividends actually earned while holding each stock/fund position, walking the whole
        /// snapshot timeline so a dividend counts against whatever quantity was held on its ex-date
        /// (not today's quantity). Dividends before the user's first snapshot are excluded — the app
        /// has no way to know what was held before that date, same honesty policy as XIRR.
        /// </summary>
        private async Task<(
            decimal TotalTWD,
            Dictionary<(AssetType AssetType, string Symbol, string Market), decimal> ByPosition,
            List<(DateOnly Date, decimal AmountTWD)> Flows)>
            ComputeDividendsAsync(
                List<HoldingSnapshot> orderedSnapshots, decimal fx,
                Dictionary<string, FundNavHistory> fundMap, CancellationToken ct)
        {
            var empty = new Dictionary<(AssetType, string, string), decimal>();
            var noFlows = new List<(DateOnly, decimal)>();
            if (orderedSnapshots.Count == 0) return (0, empty, noFlows);

            var firstDate = orderedSnapshots[0].EffectiveDate;
            var today = DateOnly.FromDateTime(DateTime.UtcNow);

            var keys = orderedSnapshots
                .SelectMany(s => s.Positions)
                .Where(p => (p.AssetType == AssetType.STOCK || p.AssetType == AssetType.FUND)
                    && !string.IsNullOrWhiteSpace(p.Symbol))
                .Select(p => (p.AssetType, Symbol: p.Symbol, Market: p.Market))
                .Distinct()
                .ToList();
            if (keys.Count == 0) return (0, empty, noFlows);

            // One dividend-history fetch per distinct position, in parallel (each individually cached).
            var fetched = await Task.WhenAll(keys.Select(async key => (
                Key: key,
                Records: key.AssetType == AssetType.STOCK
                    ? await dividendData.GetStockDividendsAsync(key.Symbol, key.Market, ct)
                    : await dividendData.GetFundDividendsAsync(key.Symbol, ct)
            )));

            var byPosition = new Dictionary<(AssetType, string, string), decimal>();
            var flows = new List<(DateOnly, decimal)>();
            decimal total = 0;

            foreach (var (key, records) in fetched)
            {
                decimal keyTotal = 0;
                foreach (var record in records)
                {
                    if (record.ExDate < firstDate || record.ExDate > today) continue;

                    // Whatever was actively held on the ex-date — not today's quantity.
                    var activeSnapshot = orderedSnapshots.LastOrDefault(s => s.EffectiveDate <= record.ExDate);
                    var position = activeSnapshot?.Positions.FirstOrDefault(p =>
                        p.AssetType == key.AssetType && p.Symbol == key.Symbol && p.Market == key.Market);
                    if (position == null || position.Quantity <= 0) continue;

                    // FundRich's dividend history has no currency field; fall back to the fund's NAV currency.
                    var currency = key.AssetType == AssetType.FUND
                        ? (fundMap.TryGetValue(key.Symbol, out var nav) ? nav.Currency : "TWD")
                        : record.Currency;
                    var fxMul = currency == "USD" ? fx : 1m;

                    var amountTWD = position.Quantity * record.AmountPerUnit * fxMul;
                    keyTotal += amountTWD;
                    flows.Add((record.ExDate, amountTWD));
                }

                if (keyTotal == 0) continue;
                byPosition[(key.AssetType, key.Symbol, key.Market)] = keyTotal;
                total += keyTotal;
            }

            return (total, byPosition, flows);
        }

        // XIRR annualizes whatever return happened over the observed window. Below this many days
        // of history, even a small, perfectly real gain extrapolates into an absurd percentage
        // (e.g. a 2% gain over 5 days compounds to 4-digit "annualized" — mathematically correct,
        // practically meaningless). This also protects new adopters: this app has no transaction
        // log, so a first-ever snapshot's entire cost basis is booked as a "contribution" dated on
        // that snapshot's day — but that cost may represent years of actual prior purchases. Until
        // enough real snapshot history accumulates, the implied holding period is far too short to
        // annualize honestly, so we simply don't show a number rather than a misleading one.
        private const int MinXirrHistoryDays = 30;

        /// <summary>
        /// Money-weighted annualized return (XIRR) on invested capital — 含息 (total return), since
        /// dividends are real cash inflows. Changes in recorded cost basis between snapshots are
        /// treated as dated contributions; each dividend is a dated inflow on its ex-date; the
        /// current value of cost-bearing holdings is the terminal inflow. Returns null when not
        /// computable, or when there isn't yet enough real history to annualize without being
        /// misleading.
        /// </summary>
        private static decimal? ComputeAnnualizedReturn(
            List<HoldingSnapshot> orderedSnapshots, decimal fallbackFx, decimal terminalValueTWD,
            List<(DateOnly Date, decimal AmountTWD)> dividendFlows)
        {
            if (terminalValueTWD <= 0) return null;
            if (orderedSnapshots.Count == 0) return null;

            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            if (today.DayNumber - orderedSnapshots[0].EffectiveDate.DayNumber < MinXirrHistoryDays) return null;

            var flows = new List<(DateOnly, double)>();
            decimal prevCost = 0;
            foreach (var s in orderedSnapshots)
            {
                var snapFx = s.UsdTwdRate > 0 ? s.UsdTwdRate : fallbackFx;
                var snapCost = s.Positions
                    .Where(p => p.AssetType != AssetType.CASH && p.CostBasis.HasValue)
                    .Sum(p => p.CostBasis!.Value * ((p.CostCurrency ?? p.Currency) == "USD" ? snapFx : 1m));
                var delta = snapCost - prevCost;
                if (delta != 0) flows.Add((s.EffectiveDate, (double)(-delta))); // contribution = outflow
                prevCost = snapCost;
            }

            foreach (var (date, amountTWD) in dividendFlows)
                flows.Add((date, (double)amountTWD)); // dividend = inflow

            flows.Add((today, (double)terminalValueTWD));

            var rate = Xirr.Compute(flows);
            if (rate is null || double.IsNaN(rate.Value) || double.IsInfinity(rate.Value)) return null;
            return Math.Round((decimal)(rate.Value * 100), 2);
        }

        /// <summary>Most recent weekday before <paramref name="from"/> (skips Sat/Sun only, not market holidays).</summary>
        private static DateOnly PreviousTradingDay(DateOnly from)
        {
            var d = from.AddDays(-1);
            while (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) d = d.AddDays(-1);
            return d;
        }

        /// <summary>
        /// True if at least one stock/fund position has a published price dated after
        /// <paramref name="cutoff"/> — i.e. the data provider has actually posted something newer
        /// than the previous trading day. Used to distinguish "the market genuinely hasn't moved"
        /// from "there's simply no fresher bar yet", which would otherwise both look like a $0 change.
        /// </summary>
        private static bool HasNewerPriceThan(
            IEnumerable<HoldingPosition> positions, DateOnly cutoff,
            Dictionary<string, DTO.Finance.MarketData> stockMap, Dictionary<string, FundNavHistory> fundMap)
        {
            foreach (var p in positions)
            {
                if (p.AssetType == AssetType.STOCK && stockMap.TryGetValue(p.Symbol, out var data))
                {
                    var latest = data.HistoricalPrices.LastOrDefault()?.Date;
                    if (latest.HasValue && DateOnly.FromDateTime(latest.Value) > cutoff) return true;
                }
                else if (p.AssetType == AssetType.FUND && !string.IsNullOrWhiteSpace(p.Symbol) &&
                         fundMap.TryGetValue(p.Symbol, out var nav))
                {
                    var latestStr = nav.Prices.LastOrDefault()?.Date;
                    if (latestStr != null && DateOnly.TryParse(latestStr, out var latestDate) && latestDate > cutoff)
                        return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Re-values a set of positions at their closing price as of <paramref name="date"/>, using
        /// already-fetched price histories (no network calls). Returns null if any non-cash position
        /// has no price on or before that date, so the caller can treat the whole total as unknown
        /// rather than silently under-counting a position as worth 0.
        /// </summary>
        private static decimal? ValueAsOfDate(
            IEnumerable<HoldingPosition> positions, DateOnly date, decimal fx,
            Dictionary<string, DTO.Finance.MarketData> stockMap, Dictionary<string, FundNavHistory> fundMap)
        {
            var dt = date.ToDateTime(TimeOnly.MinValue);
            var dayKey = date.ToString("yyyy-MM-dd");
            decimal total = 0;

            foreach (var p in positions)
            {
                if (p.AssetType == AssetType.CASH)
                {
                    total += p.Quantity * (p.Currency == "USD" ? fx : 1m);
                    continue;
                }
                if (p.AssetType == AssetType.LIABILITY)
                {
                    total -= p.Quantity * (p.Currency == "USD" ? fx : 1m);
                    continue;
                }

                decimal price = 0;
                var priceCurrency = p.Currency;

                if (p.AssetType == AssetType.STOCK)
                {
                    if (stockMap.TryGetValue(p.Symbol, out var data))
                        price = data.HistoricalPrices
                            .Where(x => x.Date.Date <= dt)
                            .OrderByDescending(x => x.Date)
                            .FirstOrDefault()?.Close ?? 0;
                }
                else // FUND
                {
                    if (!string.IsNullOrWhiteSpace(p.Symbol) && fundMap.TryGetValue(p.Symbol, out var nav))
                    {
                        priceCurrency = nav.Currency;
                        price = nav.Prices
                            .Where(x => string.CompareOrdinal(x.Date, dayKey) <= 0)
                            .OrderByDescending(x => x.Date, StringComparer.Ordinal)
                            .FirstOrDefault()?.Close ?? 0;
                    }
                    if (price <= 0) price = p.UnitPrice ?? 0; // fallback, mirrors the live valuation loop
                }

                if (price <= 0) return null;
                total += price * p.Quantity * (priceCurrency == "USD" ? fx : 1m);
            }

            return total;
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

                // TWStockLibHistoricalProvider only ever returns finalized daily bars, so during the
                // 9:00-13:30 session "the latest close" is really still yesterday's — overlay today's
                // realtime quote on top so Portfolio valuation reflects the live price intraday.
                // Never mutate `data` itself: it may be the same object instance cached (up to 4h) by
                // CachedMarketDataProvider and shared with the Finance/indicator module.
                if (region == MarketRegion.TW)
                {
                    var quote = await GetTwRealtimeQuoteAsync(symbol, ct);
                    var livePrice = quote != null ? EffectiveLivePrice(quote) : null;
                    if (quote != null && livePrice is > 0)
                        data = WithRealtimeOverlay(data, quote, livePrice.Value);
                }

                // Only cache a usable result; an empty fetch shouldn't suppress the next retry.
                if (data.HistoricalPrices.Count > 0)
                    cache.Set(key, data, CacheTtl.Portfolio.StockPrice);
                return data;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to fetch price for {Symbol}", symbol);
                return null;
            }
        }

        /// <summary>TSE/TPEx regular session, Mon-Fri 09:00-13:30. Taiwan has no DST, so a fixed
        /// UTC+8 offset is exact — no TimeZoneInfo lookup (whose ID differs Windows-vs-Linux)
        /// needed. Does not account for exchange holidays: a holiday just means the realtime quote
        /// below comes back stale/NOT_FOUND, which is silently ignored in favor of the historical
        /// close, so there is no user-visible harm from the gap.</summary>
        private static bool IsTwTradingHours()
        {
            var taipeiNow = DateTime.UtcNow.AddHours(8);
            if (taipeiNow.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) return false;
            var t = taipeiNow.TimeOfDay;
            return t >= new TimeSpan(9, 0, 0) && t <= new TimeSpan(13, 30, 0);
        }

        /// <summary>Realtime quote for a TW symbol during the trading session, or null outside
        /// trading hours / on any failure (never throws — callers always have the historical-close
        /// fallback).</summary>
        private async Task<TWStockLib.Models.StockQuote?> GetTwRealtimeQuoteAsync(string symbol, CancellationToken ct)
        {
            if (stockService is null || !IsTwTradingHours()) return null;

            // Short-cached (see CacheTtl.Finance.TwRealtimeQuote) so this and
            // TWStockLibHistoricalProvider's own quote lookup (for company name) share one network
            // hit instead of each fetching the same symbol's quote independently.
            var key = CacheTtl.Finance.TwRealtimeQuoteCacheKeyPrefix + symbol;
            if (cache.TryGetValue(key, out TWStockLib.Models.StockQuote? cached))
                return cached;

            try
            {
                var tse = await stockService.GetRealtimeQuoteAsync(symbol, TWStockLib.Models.MarketType.TSE, ct);
                var quote = tse.IsSuccess ? tse.Value : null;
                if (quote == null)
                {
                    var otc = await stockService.GetRealtimeQuoteAsync(symbol, TWStockLib.Models.MarketType.OTC, ct);
                    quote = otc.IsSuccess ? otc.Value : null;
                }

                if (quote != null)
                    cache.Set(key, quote, CacheTtl.Finance.TwRealtimeQuote);
                return quote;
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Realtime quote fetch failed for {Symbol}, falling back to historical close", symbol);
                return null;
            }
        }

        /// <summary>TWSE's realtime "last matched price" (<c>StockQuote.LastPrice</c>) is frequently
        /// empty mid-session — it only reflects the most recent trade tick, and TWSE's snapshot can
        /// lag between ticks even for a heavily-traded stock. Falls back to the bid/ask midpoint
        /// (or whichever side is present) so the overlay still fires; returns null only when the
        /// quote carries no usable price at all.</summary>
        private static decimal? EffectiveLivePrice(TWStockLib.Models.StockQuote quote)
        {
            if (quote.LastPrice is > 0) return quote.LastPrice;

            var bid = quote.Top5BuyPrice.Length > 0 ? quote.Top5BuyPrice[0] : 0;
            var ask = quote.Top5SellPrice.Length > 0 ? quote.Top5SellPrice[0] : 0;
            if (bid > 0 && ask > 0) return Math.Round((bid + ask) / 2, 2);
            if (bid > 0) return bid;
            if (ask > 0) return ask;
            return null;
        }

        /// <summary>Returns a shallow clone of <paramref name="source"/> with today's bar
        /// replaced/appended from a realtime quote, so <c>HistoricalPrices.LastOrDefault()</c>
        /// reflects the live price. Never mutates <paramref name="source"/> itself.</summary>
        private static DTO.Finance.MarketData WithRealtimeOverlay(
            DTO.Finance.MarketData source, TWStockLib.Models.StockQuote quote, decimal livePrice)
        {
            var today = DateTime.Today;
            var todayBar = new DTO.Finance.DailyPrice
            {
                Date = today,
                Open = quote.OpeningPrice ?? livePrice,
                High = quote.HighestPrice ?? livePrice,
                Low = quote.LowestPrice ?? livePrice,
                Close = livePrice,
                Volume = quote.TotalVolume ?? 0,
            };

            var prices = new List<DTO.Finance.DailyPrice>(source.HistoricalPrices);
            if (prices.Count > 0 && prices[^1].Date.Date == today)
                prices[^1] = todayBar;
            else
                prices.Add(todayBar);

            return new DTO.Finance.MarketData
            {
                StockCode = source.StockCode,
                CompanyName = source.CompanyName,
                HistoricalPrices = prices,
                Fundamentals = source.Fundamentals,
                Chips = source.Chips,
                FetchedAt = DateTime.UtcNow,
            };
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
                // BOT's flcsv endpoint now serves an anti-bot "Challenge Validation" HTML page to
                // non-browser clients, so it always fell back to 32. Use a stable key-less JSON feed.
                var client = httpClientFactory.CreateClient("ExternalReadOnly");
                var json = await client.GetStringAsync(FX_URL, ct);
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("rates", out var rates) &&
                    rates.TryGetProperty("TWD", out var twd) &&
                    twd.TryGetDecimal(out var rate) && rate > 0)
                {
                    cache.Set(FX_CACHE_KEY, rate, CacheTtl.Portfolio.Fx);
                    return rate;
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
