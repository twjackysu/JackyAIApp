using JackyAIApp.Server.Common;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.Data.Models.Portfolio;
using JackyAIApp.Server.Data.Models.SQL;
using JackyAIApp.Server.DTO.Portfolio;
using JackyAIApp.Server.Services.Finance.DataProviders;
using JackyAIApp.Server.Services.Market;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;

namespace JackyAIApp.Server.Services.Portfolio
{
    public class PortfolioService(
        AzureSQLDBContext db,
        IHoldingSnapshotReader snapshotReader,
        IPortfolioPriceLoader priceLoader,
        IDividendService dividendData,
        IStockCategoryService categoryService,
        ILogger<PortfolioService> logger,
        TimeProvider? timeProvider = null,
        // Optional for the same reason TimeProvider is: tests construct this class directly. The
        // container always supplies the real one (it is registered), and a null here simply means
        // funds fall back to the mid-market rate — the behaviour before 集保 rates existed.
        ITdccFxService? tdccFx = null,
        ICathayFxService? cathayFx = null,
        // Optional: tests have no request to report timings on.
        ServerTiming? timing = null,
        // Optional for the same reason: without it the long-range charts keep to the ~6 months of
        // prices the providers hand out, as before older history was stored.
        IDeepHistoryService? deepHistory = null,
        // Optional for the same reason: without it market holidays are not known, and the day
        // change compares with the last weekday, as before the calendar.
        ITradingCalendar? tradingCalendar = null) : IPortfolioService
    {
        private readonly ITradingCalendar _tradingCalendar = tradingCalendar ?? WeekdayTradingCalendar.Instance;

        /// <summary>The USD/TWD rate saved on a snapshot, or <see cref="PortfolioPriceLoader.FallbackUsdTwdRate"/>
        /// when it has none. Used to rebuild history as of that snapshot; the current valuation uses the
        /// live rate instead.</summary>
        private static decimal SnapshotFx(HoldingSnapshot snapshot) =>
            snapshot.UsdTwdRate > 0 ? snapshot.UsdTwdRate : PortfolioPriceLoader.FallbackUsdTwdRate;

        private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

        /// <summary>"Today" for every date-range walk in this service (net worth history,
        /// drawdown, benchmark comparison, XIRR, day-change) — overridable via the constructor's
        /// <c>timeProvider</c> parameter so tests can pin a fixed date instead of chasing the real
        /// calendar (a relative-to-real-now test fixture drifts out of whatever window a test
        /// expects as time passes; see PortfolioDrawdownTests/PortfolioBenchmarkComparisonTests).</summary>
        private DateOnly Today => DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);

        /// <summary>Runs <paramref name="work"/>, recording how long it took in this request's
        /// Server-Timing header when there is a request to report on.</summary>
        private Task<T> Timed<T>(string name, Func<Task<T>> work, Func<T, string>? describe = null) =>
            timing?.MeasureAsync(name, work, describe) ?? work();

        private Task Timed(string name, Func<Task> work) =>
            timing?.MeasureAsync(name, work) ?? work();

        // ── Save / read holdings ─────────────────────────────────────────────

        public async Task SaveHoldingsAsync(string userId, SaveHoldingsRequest req, CancellationToken ct = default)
        {
            var effectiveDate = req.EffectiveDate ?? Today;
            var usdTwdRate = await priceLoader.FetchUsdTwdRateAsync(ct);

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
                    // CASH, LIABILITY, and REAL_ESTATE are symbol-less by design (amount only).
                    .Where(p => p.Quantity > 0 && (p.AssetType is AssetType.CASH or AssetType.LIABILITY or AssetType.REAL_ESTATE
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
                        // CASH/LIABILITY/REAL_ESTATE have no cost-basis concept, so no cost currency either.
                        CostCurrency = p.AssetType is AssetType.CASH or AssetType.LIABILITY or AssetType.REAL_ESTATE
                            ? null
                            : (string.IsNullOrWhiteSpace(p.CostCurrency)
                                ? (string.IsNullOrWhiteSpace(p.Currency) ? "TWD" : p.Currency)
                                : p.CostCurrency).ToUpperInvariant(),
                        Quantity = p.Quantity,
                        // Fund NAV is fetched live; UnitPrice kept only as a fallback if ever supplied.
                        UnitPrice = p.AssetType == AssetType.FUND ? p.UnitPrice : null,
                        // Optional cost basis drives P&L for stocks/funds. Never set for CASH/LIABILITY/
                        // REAL_ESTATE — debt has no cost basis, and real estate's Quantity already IS
                        // its current value (see ValuationMethod), so neither should enter P&L or XIRR.
                        CostBasis = p.AssetType is AssetType.CASH or AssetType.LIABILITY or AssetType.REAL_ESTATE ? null : p.CostBasis,
                        ValuationMethod = p.AssetType == AssetType.REAL_ESTATE ? p.ValuationMethod : null,
                    })
                    .ToList(),
            };

            db.HoldingSnapshots.Add(snapshot);
            await db.SaveChangesAsync(ct);
            snapshotReader.Invalidate(userId);
        }

        public async Task<SaveHoldingsRequest> GetLatestHoldingsAsync(string userId, CancellationToken ct = default)
        {
            var snapshot = await snapshotReader.LatestAsync(userId, ct);
            if (snapshot == null)
                return new SaveHoldingsRequest { EffectiveDate = Today };

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
                        ValuationMethod = p.ValuationMethod,
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
                        ValuationMethod = p.ValuationMethod,
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
            snapshotReader.Invalidate(userId);
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

        public async Task<PortfolioSummaryDto> GetPortfolioSummaryAsync(
            string userId, decimal feeDiscountRate = 0.28m, CancellationToken ct = default)
        {
            var total = Stopwatch.StartNew();
            var allSnapshots = await Timed("snapshots", () => snapshotReader.AllAsync(userId, ct),
                s => $"{s.Count} snapshots / {s.Sum(x => x.Positions.Count)} positions");
            if (allSnapshots.Count == 0) return new PortfolioSummaryDto();
            var snapshot = allSnapshots[^1];

            // Defensive clamp rather than rejecting the request — this is a display-affecting
            // client-supplied value (not persisted anywhere server-side), not a validated business
            // input. 0 would mean free trades, > 1 is not a discount at all.
            feeDiscountRate = Math.Clamp(feeDiscountRate, 0.01m, 1m);

            // Current valuation always uses the live rate; the rate stored on each snapshot is only
            // for reconstructing historical net worth (a stale fallback-32 shouldn't stick forever).
            var fx = await Timed("fx", () => priceLoader.FetchUsdTwdRateAsync(ct));
            var positions = new List<PositionDto>();
            decimal twStock = 0, usStock = 0, cash = 0, fund = 0, liability = 0, realEstate = 0;
            decimal costBasisTWD = 0, pnlTWD = 0, estimatedSellCostTWD = 0;
            decimal equityExposureTWD = 0; // Σ market value × exposure factor (leverage/bond-adjusted)

            // Prefetch all stock prices and fund NAVs in parallel (cached) before the value loop,
            // covering both today's holdings and the previous trading day's holdings so sold positions
            // can be priced for the single-day liquid net worth change.
            var prevTradingDay = _tradingCalendar.PreviousTradingDay(Today);
            var prevSnapshot = allSnapshots.LastOrDefault(s => s.EffectiveDate <= prevTradingDay)
                ?? allSnapshots.FirstOrDefault();
            var allPositions = prevSnapshot != null
                ? snapshot.Positions.Concat(prevSnapshot.Positions).ToList()
                : snapshot.Positions;

            var stockTask = Timed("stocks", () => priceLoader.FetchStocksAsync(
                allPositions.Where(p => p.AssetType == AssetType.STOCK).Select(p => p.Symbol), deep: false, ct),
                m => $"{m.Count} symbols");
            var fundTask = Timed("funds", () => priceLoader.FetchFundsAsync(
                allPositions.Where(p => p.AssetType == AssetType.FUND).Select(p => p.Symbol), ct),
                m => $"{m.Count} funds");
            // Dividend histories don't depend on prices, so they load alongside them instead of
            // after. On a cold cache (every restart, and daily) they are one external call per
            // stock/fund ever held, which used to sit on the critical path behind the price fetch.
            var dividendTask = Timed("dividends", () => FetchDividendRecordsAsync(allSnapshots, ct),
                d => $"{d.Count} holdings");
            await Task.WhenAll(stockTask, fundTask);
            var stockMap = stockTask.Result;
            var fundMap = fundTask.Result;

            // 集保 settlement rates for the fund currencies actually held, and 國泰世華 board rates
            // for US stocks (keyed by the date of the close being priced — NOT today; 國泰 複委託
            // states 總預估現值 = 股數 × 最新價 × 昨收匯率, so the rate that belongs to the quote is
            // the one published for that quote's own day, not today's). Only funds settle through
            // 集保; prices come from one shared fetch above, so in practice every US holding lands
            // on the same date and cathayUsKeys is one lookup.
            //
            // 基富通 is converted back to TWD at the rate 集保 negotiates with 華南銀行, so that is
            // what the holder really receives and what 基富通's own app displays — valuing it at a
            // mid-market rate instead put this app ~0.5% away from the number the user was
            // reconciling against.
            //
            // The two rate sets don't depend on each other (one reads fundMap, the other stockMap,
            // both already fetched above), so their per-currency/per-date lookups all run together
            // via Task.WhenAll instead of two sequential foreach+await loops.
            List<string> tdccCurrencies = tdccFx == null
                ? []
                : fundMap.Values
                    .Select(n => n.Currency)
                    .Where(c => !string.IsNullOrWhiteSpace(c) && c != "TWD")
                    .Distinct()
                    .ToList();
            // allPositions, not just today's: a US stock sold since the previous trading day is still
            // valued on that side of the day change, and must be valued at the same rate.
            List<(string Currency, DateOnly Date)> cathayUsKeys = cathayFx == null
                ? []
                : allPositions
                    .Where(p => p.AssetType == AssetType.STOCK && p.Currency == "USD"
                        && stockMap.ContainsKey(p.Symbol))
                    .Select(p => stockMap[p.Symbol].HistoricalPrices.LastOrDefault()?.Date)
                    .Where(d => d.HasValue)
                    .Select(d => ("USD", DateOnly.FromDateTime(d!.Value)))
                    .Distinct()
                    .ToList();

            var tdccRatesTask = Task.WhenAll(tdccCurrencies.Select(async currency =>
                (Currency: currency, Rate: await tdccFx!.GetBuyRateAsync(currency, Today, ct))));
            var cathayRatesTask = Task.WhenAll(cathayUsKeys.Select(async key =>
                (Key: key, Rate: await cathayFx!.GetBuyRateAsync(key.Currency, key.Date, ct))));
            await Timed("fxrates", () => Task.WhenAll(tdccRatesTask, cathayRatesTask));

            var tdccRates = new Dictionary<string, decimal>();
            foreach (var (currency, rate) in tdccRatesTask.Result)
                if (rate is > 0) tdccRates[currency] = rate.Value;

            var cathayRatesByDate = new Dictionary<(string Currency, DateOnly Date), decimal>();
            foreach (var (key, rate) in cathayRatesTask.Result)
                if (rate is > 0) cathayRatesByDate[key] = rate.Value;

            // The 國泰世華 rate that goes with this holding's own quote, if we have one.
            decimal? CathayRateFor(HoldingPosition position, string currency)
            {
                if (position.AssetType != AssetType.STOCK || !stockMap.TryGetValue(position.Symbol, out var data))
                    return null;
                var priceDate = data.HistoricalPrices.LastOrDefault()?.Date;
                if (priceDate == null) return null;
                return cathayRatesByDate.TryGetValue((currency, DateOnly.FromDateTime(priceDate.Value)), out var r)
                    ? r
                    : null;
            }

            // The rate a position converts to TWD at. The valuation loop below and the previous
            // trading day's valuation for the day change both go through this, so the two sides of
            // the day change always use the same rate for the same position: a difference between
            // them is then price or holdings, never a mismatch of rate sources.
            //
            // Funds convert at the 集保 published rate; US stocks at the 國泰世華 rate for their
            // quote's day; everything else in USD at the mid-market rate. Falls back to mid-market
            // when a published rate is missing, since an FX lookup must never be what stops a
            // portfolio from being valued.
            // Also says where the rate came from (null when none applies, i.e. TWD or a currency with
            // no rate), so the response's FxRateSource is what was used, not a guess from the number.
            (decimal Rate, string? Source) ResolveFx(HoldingPosition position, string? currency)
            {
                if (string.IsNullOrWhiteSpace(currency) || currency == "TWD") return (1m, null);
                if (position.AssetType == AssetType.FUND && tdccRates.TryGetValue(currency, out var fundFx))
                    return (fundFx, FxRateSources.Tdcc);
                if (CathayRateFor(position, currency) is { } usFx) return (usFx, FxRateSources.Cathay);
                return currency == "USD" ? (fx, FxRateSources.Mid) : (1m, null);
            }
            decimal PositionFx(HoldingPosition position, string? priceCurrency) => ResolveFx(position, priceCurrency).Rate;

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
                else // CASH, LIABILITY, or REAL_ESTATE — always "priced" at 1, no external lookup needed.
                {
                    currentPrice = 1;
                }

                // A priceable (non-cash, non-liability, non-real-estate) position with no usable
                // price has no real market value. Treat it as "price unavailable" so we don't book a
                // phantom loss of the full cost.
                if (p.AssetType is not (AssetType.CASH or AssetType.LIABILITY or AssetType.REAL_ESTATE) && currentPrice <= 0)
                    priceFailed = true;

                var (fxMul, fxSource) = ResolveFx(p, priceCurrency);
                // LIABILITY is the one place a position's value goes negative — this is the sole
                // sign-flip point; every downstream sum (net worth, history, exports) just adds
                // valueTWD and is correct with no special-casing.
                var valueLocal = p.AssetType switch
                {
                    AssetType.CASH => p.Quantity,
                    AssetType.LIABILITY => -p.Quantity,
                    AssetType.REAL_ESTATE => p.Quantity,
                    _ => currentPrice * p.Quantity,
                };
                var valueTWD = valueLocal * fxMul;

                // Cost is converted by its own currency, which may differ from the pricing
                // currency — but by the same convention, so a fund whose cost was paid in USD is
                // not measured against a market value converted at a different rate.
                var costFxMul = PositionFx(p, p.CostCurrency ?? p.Currency);

                // Market exposure: leveraged ETFs amplify, bond/cash contribute nothing. Computed
                // here (rather than after the P&L block below) because the TW sell-cost estimate
                // reuses it: exposureFactor == 0 already means "bond/money-market ETF" per
                // PortfolioExposure's own classification (name keywords take priority over its
                // code-suffix fallback), including the "except leveraged/inverse" carve-out — a
                // leveraged name match returns its multiplier before the bond check ever runs, so
                // a leveraged bond ETF's factor is never 0. No separate detection needed here.
                var exposureFactor = PortfolioExposure.Factor(p.AssetType, p.Name, p.Symbol, p.Market);

                decimal? posCostTWD = null, posPnlTWD = null, posPnlPct = null;
                decimal posEstimatedSellCostTWD = 0;
                // LIABILITY.CostBasis and REAL_ESTATE.CostBasis are always null (enforced at save
                // time), so neither ever enters P&L/XIRR without needing an explicit exclusion here.
                if (p.AssetType != AssetType.CASH && p.CostBasis.HasValue)
                {
                    posCostTWD = p.CostBasis.Value * costFxMul;
                    // Only compute P&L when we have a real price; otherwise valueTWD is 0
                    // and we'd report a fake loss equal to the entire cost basis.
                    if (!priceFailed)
                    {
                        posPnlTWD = valueTWD - posCostTWD;

                        // Estimated sell-side transaction cost, TW stocks/ETFs only (US/funds have
                        // different fee schedules and aren't modeled here). Buy-side fee is
                        // deliberately NOT deducted: CostBasis is manually entered by the user and
                        // typically already reflects what they actually paid (broker statements
                        // include the buy commission), so subtracting it again here would
                        // double-count it. Brokerage commission applies uniformly regardless of
                        // instrument; the 證交稅 rate depends on it — 0% bond/money-market ETF
                        // (exposureFactor == 0), 0.1% other TW ETF (code prefix "00" is TWSE's own
                        // ETF numbering convention, not a heuristic), 0.3% individual stock.
                        if (p.AssetType == AssetType.STOCK && p.Market == "TW")
                        {
                            var taxRate = exposureFactor == 0m ? 0m : (p.Symbol.StartsWith("00") ? 0.001m : 0.003m);
                            var feeRate = 0.001425m * feeDiscountRate;
                            // Floor to whole dollars — brokers round transaction costs down, not to cents.
                            posEstimatedSellCostTWD = Math.Floor(valueTWD * feeRate) + Math.Floor(valueTWD * taxRate);
                            posPnlTWD -= posEstimatedSellCostTWD;
                        }

                        posPnlPct = posCostTWD > 0 ? Math.Round(posPnlTWD.Value / posCostTWD.Value * 100, 2) : 0;
                        costBasisTWD += posCostTWD.Value;
                        pnlTWD += posPnlTWD.Value;
                        estimatedSellCostTWD += posEstimatedSellCostTWD;
                    }
                }

                switch (p.AssetType)
                {
                    case AssetType.STOCK when p.Market == "US": usStock += valueTWD; break;
                    case AssetType.STOCK: twStock += valueTWD; break;
                    case AssetType.CASH: cash += valueTWD; break;
                    case AssetType.FUND: fund += valueTWD; break;
                    case AssetType.REAL_ESTATE: realEstate += valueTWD; break;
                    // valueTWD is already negative here — this is display total (owed amount),
                    // shown positive by negating at the DTO boundary below.
                    case AssetType.LIABILITY: liability += valueTWD; break;
                }

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
                    EstimatedSellCostTWD = posEstimatedSellCostTWD,
                    ExposureFactor = exposureFactor,
                    FxRateUsed = fxMul == 1m ? null : fxMul,
                    FxRateSource = fxSource,
                    PriceFetchFailed = priceFailed,
                    ValuationMethod = p.ValuationMethod,
                });
            }

            // liability is already negative (see the valuation loop above), so this is a plain sum —
            // no special-casing needed for net worth to correctly net out debt.
            var netWorthTWD = twStock + usStock + cash + fund + realEstate + liability;

            // Total (含息) return: walk the whole snapshot timeline so a dividend counts against
            // whatever quantity was actually held on its ex-date, not today's quantity.
            var (totalDividendsTWD, dividendsByPosition, dividendFlows) =
                ComputeDividends(allSnapshots, fx, fundMap, await dividendTask);
            foreach (var p in positions)
            {
                if (dividendsByPosition.TryGetValue((p.AssetType, p.Symbol, p.Market), out var div))
                    p.DividendsTWD = div;
            }
            var totalReturnPnlTWD = pnlTWD + totalDividendsTWD;

            // AI theme tags: read-only lookup against whatever's already classified/persisted —
            // deliberately NOT categoryService.GetCategoryAsync, which lazily triggers an AI call
            // per never-seen symbol; the summary is the hot path and must not pay AI latency/cost.
            // Unclassified symbols just show no tags until a 產業/子類別 chart view classifies them.
            var taggedSymbols = positions
                .Where(p => !string.IsNullOrWhiteSpace(p.Symbol))
                .Select(p => p.Symbol)
                .Distinct()
                .ToList();
            if (taggedSymbols.Count > 0)
            {
                var tagRows = await Timed("tags", () => db.StockSymbolInfos
                    .Where(s => taggedSymbols.Contains(s.Symbol) && s.Tags != null)
                    .Select(s => new { s.Symbol, s.Tags })
                    .ToListAsync(ct));
                var tagMap = tagRows.ToDictionary(r => r.Symbol, r => StockCategoryService.SplitTags(r.Tags));
                foreach (var p in positions)
                {
                    if (tagMap.TryGetValue(p.Symbol, out var tags))
                        p.Tags = tags.ToList();
                }
            }

            // XIRR is 含息 (total return): dividends are real cash inflows, so leaving them out
            // would understate the annualized return relative to what the user actually earned.
            var annualizedReturnPct = ComputeAnnualizedReturn(
                allSnapshots, fx, costBasisTWD + pnlTWD, dividendFlows, Today);

            // Shared denominator for unrealized and total-return %: gross liquid assets only —
            // real estate is manually-valued and shouldn't dilute a price-driven %, and liability
            // isn't netted out so a mortgage doesn't inflate the ratio the way "÷ (assets - debt)" would.
            var liquidGrossAssetsTWD = twStock + usStock + cash + fund;
            // Liquid assets net of debt — the actual equity backing a leveraged position, and the
            // foundation for liquid net assets (流動淨資產 / 流動資產淨值). Real estate is excluded
            // on purpose: a house can't be liquidated to meet immediate obligations. `liability`
            // is already negative here (see the sign-flip invariant at the single valuation point above).
            var liquidNetWorthTWD = liquidGrossAssetsTWD + liability;

            // Day change: measures single-day rise or fall in liquid net assets (流動淨資產 = liquid gross
            // assets + liability, excluding real estate) vs the previous trading day.
            //
            // Positions baseline: uses the active snapshot on or before the previous trading day (falling
            // back to today's snapshot if no prior snapshot exists). If the user sold stock into cash
            // or deposited funds, the change in liquid net assets is reflected even if the current snapshot
            // has no stock positions.
            //
            // Guard: when holdings haven't changed between the two days, require at least one held position
            // to have a published price newer than the previous trading day's close. Otherwise, before
            // today's market data lands, today and previous trading day resolve to identical prices and
            // would misleadingly show $0 / 0.00% rather than "no new data yet".
            decimal? dayChangeTWD = null, dayChangePct = null;
            if (prevSnapshot != null)
            {
                var holdingsChanged = snapshot.Id != prevSnapshot.Id
                    && !ArePositionsEqual(snapshot.Positions, prevSnapshot.Positions);
                var hasNewerPrice = HasNewerPriceThan(allPositions, prevTradingDay, stockMap, fundMap);

                if (holdingsChanged || hasNewerPrice)
                {
                    // Valued at today's rates through the same PositionFx as today's side, not at
                    // the rate stored on the earlier snapshot. That stored rate is the mid-market
                    // rate from whenever the snapshot was saved (possibly weeks ago) and a different
                    // source from the 國泰/集保 rates on today's side; mixing them put the gap
                    // between two unrelated rates into every day's figure. With no trade and no
                    // price move, US$30k of stock read as a NT$15,000 loss. Holding FX constant
                    // leaves price moves and holdings changes, which is what this figure is for.
                    var asOfPrices = AsOfPriceBook.From(stockMap, fundMap);
                    var prevLiquidNetWorthTWD = ValueAsOfDate(
                        prevSnapshot.Positions, prevTradingDay, fx, asOfPrices,
                        includeRealEstate: false, includeLiability: true, fxFor: PositionFx);

                    if (prevLiquidNetWorthTWD.HasValue)
                    {
                        dayChangeTWD = liquidNetWorthTWD - prevLiquidNetWorthTWD.Value;

                        // % of the previous day's gross liquid assets (excl. real estate, not net of
                        // liability): the same basis as UnrealizedPnlPct/TotalReturnPct and the
                        // 流動資產 headline above them, as the user asked for. Dividing by net
                        // assets instead inflates the % whenever there is debt, out of line with the
                        // two percentages shown beside it.
                        var prevLiquidGrossTWD = ValueAsOfDate(
                            prevSnapshot.Positions, prevTradingDay, fx, asOfPrices,
                            includeRealEstate: false, includeLiability: false, fxFor: PositionFx);
                        if (prevLiquidGrossTWD is > 0)
                            dayChangePct = Math.Round(dayChangeTWD.Value / prevLiquidGrossTWD.Value * 100, 2);
                    }
                }
            }

            timing?.Add("total", total.Elapsed);
            return new PortfolioSummaryDto
            {
                EffectiveDate = snapshot.EffectiveDate,
                UsdTwdRate = fx,
                TotalNetWorthTWD = netWorthTWD,
                TwStockTWD = twStock,
                UsStockTWD = usStock,
                CashTWD = cash,
                FundTWD = fund,
                RealEstateTWD = realEstate,
                // Display as a positive "amount owed"; liability itself is negative for the net-worth sum.
                LiabilityTWD = -liability,
                // Gross liquid assets — excludes real estate, not net of liability. Denominator
                // for the three headline %s above/below; also surfaced directly so the UI can show
                // it next to 流動資產淨值 (which IS net of liability).
                LiquidGrossAssetsTWD = liquidGrossAssetsTWD,
                CostBasisTWD = costBasisTWD,
                UnrealizedPnlTWD = pnlTWD,
                UnrealizedPnlPct = liquidGrossAssetsTWD > 0
                    ? Math.Round(pnlTWD / liquidGrossAssetsTWD * 100, 2) : 0,
                EstimatedSellCostTWD = estimatedSellCostTWD,
                TotalDividendsTWD = totalDividendsTWD,
                TotalReturnPnlTWD = totalReturnPnlTWD,
                TotalReturnPct = liquidGrossAssetsTWD > 0
                    ? Math.Round(totalReturnPnlTWD / liquidGrossAssetsTWD * 100, 2) : 0,
                AnnualizedReturnPct = annualizedReturnPct,
                MarketExposureTWD = equityExposureTWD,
                // Deployment level: how much of what's actually held carries market risk, with no
                // regard for how it was financed. Debt is deliberately NOT netted out here — that's
                // the whole point of having this alongside the leverage multiple below, which
                // answers the different question of how big the position is relative to own money.
                ExposureOfLiquidAssetsPct = liquidGrossAssetsTWD > 0
                    ? Math.Round(equityExposureTWD / liquidGrossAssetsTWD * 100, 1)
                    : null,
                LiquidExposureMultiple = liquidNetWorthTWD > 0
                    ? Math.Round(equityExposureTWD / liquidNetWorthTWD, 2)
                    : null,
                // Fraction of a market-wide decline that would wipe liquid net worth out. Solving
                // `netWorth + exposure * f = 0` for f gives f = -netWorth / exposure, so this is
                // exactly (liquid net worth ÷ exposure) — the reciprocal of the exposure ratio
                // above, which is the whole point: exposure/equity has its singularity at equity=0
                // (the most dangerous moment, where it explodes then flips sign and reads as a
                // meaningless negative), while equity/exposure divides by a quantity that can't go
                // negative and only degenerates at exposure=0 — the *safest* state, trivially
                // rendered as "no market exposure". Negative values are meaningful here: already
                // insolvent. Assumes every position moves together at beta 1, so it's a
                // deliberately conservative approximation, not a precise risk figure.
                DrawdownBufferPct = equityExposureTWD > 0
                    ? Math.Round(liquidNetWorthTWD / equityExposureTWD * 100, 1)
                    : null,
                DayChangeTWD = dayChangeTWD,
                DayChangePct = dayChangePct,
                Positions = positions.OrderByDescending(p => p.MarketValueTWD).ToList(),
            };
        }

        /// <summary>
        /// Manual retry for a position stuck showing "報價失敗" — busts both this request-scoped
        /// in-memory cache entry and the provider's own persisted sticky state (e.g. a TW symbol
        /// wrongly resolved to the other market once and never re-checked since, see
        /// <see cref="Finance.DataProviders.PersistentTwHistoricalProvider.InvalidateAsync"/>) for
        /// just this one symbol, then returns a freshly recomputed summary. Only STOCK positions
        /// route through <see cref="IMarketDataProviderFactory"/> at all (FUND pricing goes through
        /// <see cref="IFundDataService"/> instead), so a symbol not currently held as a STOCK is a
        /// silent no-op rather than an error — this also stops the endpoint being usable to force
        /// arbitrary external-API fetches for symbols the caller doesn't even hold.
        /// </summary>
        public async Task<PortfolioSummaryDto> RefreshPositionPriceAsync(
            string userId, string symbol, decimal feeDiscountRate = 0.28m, CancellationToken ct = default)
        {
            symbol = (symbol ?? "").ToUpperInvariant();
            var snapshot = await snapshotReader.LatestAsync(userId, ct);
            var owns = snapshot?.Positions.Any(p => p.AssetType == AssetType.STOCK && p.Symbol == symbol) ?? false;

            if (owns)
                await priceLoader.InvalidateStockAsync(symbol, ct);

            return await GetPortfolioSummaryAsync(userId, feeDiscountRate, ct);
        }

        // ── Net worth history ────────────────────────────────────────────────

        public Task<List<NetWorthDataPoint>> GetNetWorthHistoryAsync(
            string userId, int days, NetWorthGroupBy groupBy = NetWorthGroupBy.AssetType, CancellationToken ct = default)
        {
            days = Math.Clamp(days, 7, 180);
            var today = Today;
            return GetNetWorthHistoryCoreAsync(userId, today.AddDays(-days), today, groupBy, ct);
        }

        /// <summary>
        /// Net worth history over an explicit date window, for callers that let the user pick the
        /// start and end rather than a preset span.
        ///
        /// Lenient by design — every input resolves to some sensible window rather than an error,
        /// because these come straight from date inputs the user is editing:
        /// <list type="bullet">
        ///   <item>no <paramref name="to"/>, or one in the future → ends today</item>
        ///   <item>no <paramref name="from"/> → one calendar month before the end</item>
        ///   <item>from after to → swapped</item>
        ///   <item>from before the user's first snapshot → starts at the first snapshot</item>
        ///   <item>longer than one year → the most recent year of it</item>
        /// </list>
        /// </summary>
        public Task<List<NetWorthDataPoint>> GetNetWorthHistoryAsync(
            string userId, DateOnly? from, DateOnly? to, NetWorthGroupBy groupBy = NetWorthGroupBy.AssetType,
            CancellationToken ct = default)
        {
            var today = Today;
            if (from is { } f && to is { } t && f > t) (from, to) = (t, f);

            var end = to is { } requestedEnd && requestedEnd < today ? requestedEnd : today;
            var start = from ?? end.AddMonths(-1);
            if (start > end) start = end; // a start in the future: show the one day there is
            // At most one calendar year. The chart's own inputs already keep within it; this is the
            // limit for any other caller, so an arbitrary from/to can't make one request walk years
            // of daily valuations.
            var earliest = end.AddYears(-1);
            if (start < earliest) start = earliest;

            return GetNetWorthHistoryCoreAsync(userId, start, end, groupBy, ct);
        }

        private async Task<List<NetWorthDataPoint>> GetNetWorthHistoryCoreAsync(
            string userId, DateOnly startDate, DateOnly endDate, NetWorthGroupBy groupBy, CancellationToken ct)
        {
            var snapshots = await snapshotReader.AllAsync(userId, ct);

            if (snapshots.Count == 0) return [];

            // Nothing exists before the first snapshot, so a window reaching further back starts there.
            var firstDate = snapshots[0].EffectiveDate;
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
            // Deep fetch — the day loop below walks the whole requested window per symbol.
            var priceMap = (await priceLoader.FetchStocksAsync(stockSymbols, deep: true, ct))
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
            var fundNavMap = await priceLoader.FetchFundsAsync(fundIds, ct);
            var fundPriceMap = fundNavMap.ToDictionary(
                kv => kv.Key,
                kv => kv.Value.Prices.OrderBy(x => x.Date, StringComparer.Ordinal).ToList());
            var fundCursors = fundNavMap.Keys.ToDictionary(k => k, _ => 0);

            // Only resolved when actually needed (groupBy: category/subCategory) — one AI-
            // classification call per distinct STOCK/FUND symbol, warmed concurrently up front just
            // like the price fetches above rather than sequentially inside the day loop. CASH/
            // LIABILITY don't need a lookup (mapped directly by the switch below); most calls are
            // cache hits after the first time a symbol is ever seen (StockCategoryService persists
            // to StockSymbolInfo).
            var categoryMap = new Dictionary<string, PortfolioCategory>();
            var subCategoryMap = new Dictionary<string, string>();
            if (groupBy is NetWorthGroupBy.Category or NetWorthGroupBy.SubCategory)
            {
                var classifiable = snapshots
                    .SelectMany(s => s.Positions)
                    .Where(p => p.AssetType is AssetType.STOCK or AssetType.FUND && !string.IsNullOrWhiteSpace(p.Symbol))
                    .GroupBy(p => p.Symbol)
                    .Select(g => g.Last()) // most recent name/market info for that symbol
                    .ToList();
                var resolved = await Task.WhenAll(classifiable.Select(async p =>
                {
                    var (category, subCategory, _) = await categoryService.GetCategoryAsync(p.AssetType, p.Symbol, p.Name, p.Market, ct);
                    return (p.Symbol, Category: category, SubCategory: subCategory);
                }));
                categoryMap = resolved.ToDictionary(r => r.Symbol, r => r.Category);
                // Falls back to the (Traditional Chinese) category name when the AI didn't return a
                // subCategory (e.g. classification failed, or genuinely Other) — better than an
                // empty/undefined series key.
                subCategoryMap = resolved.ToDictionary(
                    r => r.Symbol, r => string.IsNullOrWhiteSpace(r.SubCategory) ? "未分類" : r.SubCategory);
            }

            // Snapshots are sorted ascending by EffectiveDate (IHoldingSnapshotReader.AllAsync), and `date` only
            // ever increases below — walk a single cursor forward instead of re-scanning the whole
            // snapshot list ("active = latest with EffectiveDate <= date") on every day.
            var snapshotIdx = 0;

            var result = new List<NetWorthDataPoint>();
            for (var date = startDate; date <= endDate; date = date.AddDays(1))
            {
                if (date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday) continue;

                while (snapshotIdx + 1 < snapshots.Count && snapshots[snapshotIdx + 1].EffectiveDate <= date)
                    snapshotIdx++;
                var active = snapshots[snapshotIdx].EffectiveDate <= date ? snapshots[snapshotIdx] : null;
                if (active == null) continue;

                var fx = SnapshotFx(active);
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
                        Add(GroupKey(groupBy, p, categoryMap, subCategoryMap, PortfolioCategory.Cash), p.Quantity * fxMul);
                    }
                    else if (p.AssetType == AssetType.REAL_ESTATE)
                    {
                        Add(GroupKey(groupBy, p, categoryMap, subCategoryMap, PortfolioCategory.Property), p.Quantity * fxMul);
                    }
                    else if (p.AssetType == AssetType.LIABILITY)
                    {
                        // Negative — the sole sign-flip point, same invariant as the summary
                        // endpoint's valuation loop. Every downstream sum (here: `total`) just adds.
                        Add(GroupKey(groupBy, p, categoryMap, subCategoryMap, PortfolioCategory.Liability), -(p.Quantity * fxMul));
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
                            Add(GroupKey(groupBy, p, categoryMap, subCategoryMap, PortfolioCategory.Other), nav * p.Quantity * fundFxMul);
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
                        Add(GroupKey(groupBy, p, categoryMap, subCategoryMap, PortfolioCategory.Other), close * p.Quantity * fxMul);
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

        // ── Drawdown analysis ────────────────────────────────────────────────

        /// <summary>
        /// Max-drawdown analysis over the requested window: daily curve of net worth vs. its running
        /// peak, plus the single worst peak-to-trough episode (magnitude, dates, recovery status).
        /// No groupBy/AI-classification involved, so this isn't subject to
        /// <see cref="GetNetWorthHistoryAsync"/>'s 180-day cap (that cap exists for its groupBy modes,
        /// not the price-fetch itself) — allows up to ~3 years so a real drawdown episode isn't cut
        /// off mid-window. Reuses <see cref="ValueAsOfDate"/> (day-change's per-date valuator) instead
        /// of re-deriving the STOCK/FUND/CASH/LIABILITY/REAL_ESTATE valuation rules a third time.
        /// </summary>
        public async Task<DrawdownAnalysisDto> GetDrawdownAnalysisAsync(
            string userId, int days, CancellationToken ct = default)
        {
            days = Math.Clamp(days, 30, 1095);

            var snapshots = await snapshotReader.AllAsync(userId, ct);
            if (snapshots.Count == 0) return new DrawdownAnalysisDto();

            var today = Today;
            var firstDate = snapshots[0].EffectiveDate;
            var startDate = today.AddDays(-days);
            if (startDate < firstDate) startDate = firstDate;

            var stockSymbols = snapshots
                .SelectMany(s => s.Positions)
                .Where(p => p.AssetType == AssetType.STOCK)
                .Select(p => p.Symbol)
                .Distinct()
                .ToList();
            var stockMap = await priceLoader.FetchStocksAsync(stockSymbols, deep: true, ct);
            var depth = await ExtendHistoryAsync(stockMap, snapshots, [], startDate, ct);
            stockMap = depth.Stocks;

            var fundIds = snapshots
                .SelectMany(s => s.Positions)
                .Where(p => p.AssetType == AssetType.FUND && !string.IsNullOrWhiteSpace(p.Symbol))
                .Select(p => p.Symbol)
                .Distinct()
                .ToList();
            var fundMap = await priceLoader.FetchFundsAsync(fundIds, ct);
            var asOfPrices = AsOfPriceBook.From(stockMap, fundMap);

            var snapshotIdx = 0;
            var points = new List<DrawdownPointDto>();
            decimal runningPeak = 0;
            var runningPeakDate = startDate;

            for (var date = startDate; date <= today; date = date.AddDays(1))
            {
                if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) continue;

                while (snapshotIdx + 1 < snapshots.Count && snapshots[snapshotIdx + 1].EffectiveDate <= date)
                    snapshotIdx++;
                var active = snapshots[snapshotIdx].EffectiveDate <= date ? snapshots[snapshotIdx] : null;
                if (active == null) continue;

                var fx = SnapshotFx(active);
                var total = ValueAsOfDate(active.Positions, date, fx, asOfPrices);
                // Null = a held position couldn't be priced as of this date yet (no-data), same gap
                // semantics as GetNetWorthHistoryAsync's own "== 0" skip. A real negative total
                // (debt exceeding assets) is > null and still a valid, kept data point.
                if (total is null) continue;

                if (total.Value > runningPeak)
                {
                    runningPeak = total.Value;
                    runningPeakDate = date;
                }

                var drawdownPct = runningPeak > 0
                    ? Math.Round((total.Value - runningPeak) / runningPeak * 100, 2)
                    : 0m;
                points.Add(new DrawdownPointDto
                {
                    Date = date.ToString("yyyy-MM-dd"),
                    TotalTWD = total.Value,
                    RunningPeakTWD = runningPeak,
                    RunningPeakDate = runningPeakDate.ToString("yyyy-MM-dd"),
                    DrawdownPct = drawdownPct,
                });
            }

            // Still reported with nothing to draw yet: the client is waiting on exactly that.
            if (points.Count == 0) return new DrawdownAnalysisDto { Backfill = depth.Backfill };

            var result = new DrawdownAnalysisDto
            {
                Points = points,
                CurrentDrawdownPct = points[^1].DrawdownPct,
                Backfill = depth.Backfill,
            };

            // Worst single episode: the point with the most negative drawdown (earliest on a tie),
            // then walk forward from its running-peak date to find when total first recovers to that
            // exact peak value again.
            var trough = points.OrderBy(p => p.DrawdownPct).ThenBy(p => p.Date, StringComparer.Ordinal).First();
            if (trough.DrawdownPct == 0)
            {
                result.MaxDrawdownPct = 0;
                return result; // net worth never dipped below a prior peak in this window
            }

            var peakValue = trough.RunningPeakTWD;
            var recoveryPoint = points
                .Where(p => string.CompareOrdinal(p.Date, trough.Date) > 0 && p.TotalTWD >= peakValue)
                .OrderBy(p => p.Date, StringComparer.Ordinal)
                .FirstOrDefault();

            var underwaterEndDate = recoveryPoint?.Date ?? points[^1].Date;
            var underwaterDays = (DateOnly.Parse(underwaterEndDate).ToDateTime(TimeOnly.MinValue)
                - DateOnly.Parse(trough.RunningPeakDate).ToDateTime(TimeOnly.MinValue)).Days;

            result.MaxDrawdownPct = trough.DrawdownPct;
            result.PeakDate = trough.RunningPeakDate;
            result.TroughDate = trough.Date;
            result.RecoveryDate = recoveryPoint?.Date;
            result.UnderwaterDays = underwaterDays;
            result.StillUnderwater = recoveryPoint == null;
            return result;
        }

        // ── Benchmark comparison ─────────────────────────────────────────────

        private static readonly string[] DefaultBenchmarkSymbols = ["0050"];

        /// <summary>
        /// "What if every contribution I made had bought each of <paramref name="benchmarkSymbols"/>
        /// instead of what I actually bought?" Reuses ComputeAnnualizedReturn's exact cash-flow
        /// proxy — Σ CostBasis of non-cash positions, delta vs. the previous snapshot = a dated
        /// contribution/withdrawal — so this stays consistent with the XIRR figure already shown
        /// elsewhere: same universe (cost-tracked STOCK/FUND only), same "what changed in recorded
        /// cost basis is what got added/pulled" assumption. That contribution flow doesn't depend on
        /// which benchmark it's compared against, so it's computed once per date and applied to
        /// every requested benchmark independently (each buys/holds units at its own daily price —
        /// symbols never affect each other's curve). The portfolio side is priced the same way, so
        /// every curve here is a directly comparable curve.
        ///
        /// The reported percentages are 含息 (total return) on BOTH sides. Price-only would
        /// systematically penalise a high-yield portfolio: a distribution knocks the price down on
        /// its ex-date, so 0056/00878-style holdings would show that drop as a loss while a lower-
        /// yielding 0050 gives up less price — scoring the user against a benchmark that is handed
        /// an advantage purely by paying out less. Dividends are counted as income retained (added
        /// to the growth factor on the day earned, not reinvested into more units), matching how
        /// 總覽's 總報酬 and XIRR already treat them. The plotted CURVES stay pure market value, so
        /// they still reconcile against a brokerage statement — which is why the line's rise and
        /// the headline % are not expected to match.
        /// </summary>
        public async Task<BenchmarkComparisonDto> GetBenchmarkComparisonAsync(
            string userId, int days, IReadOnlyList<string>? benchmarkSymbols = null, CancellationToken ct = default)
        {
            days = Math.Clamp(days, 30, 1095);
            var symbols = (benchmarkSymbols == null || benchmarkSymbols.Count == 0
                    ? DefaultBenchmarkSymbols
                    : benchmarkSymbols.Select(s => s.Trim().ToUpperInvariant()).Where(s => s.Length > 0))
                .Distinct()
                .ToList();
            if (symbols.Count == 0) symbols = [.. DefaultBenchmarkSymbols];

            var snapshots = await snapshotReader.AllAsync(userId, ct);
            if (snapshots.Count == 0)
                return new BenchmarkComparisonDto { Benchmarks = symbols.Select(s => new BenchmarkSeriesDto { Symbol = s }).ToList() };

            var today = Today;
            var firstDate = snapshots[0].EffectiveDate;
            var startDate = today.AddDays(-days);
            if (startDate < firstDate) startDate = firstDate;

            var stockSymbols = snapshots
                .SelectMany(s => s.Positions)
                .Where(p => p.AssetType == AssetType.STOCK)
                .Select(p => p.Symbol)
                .Distinct()
                .ToList();
            foreach (var s in symbols)
                if (!stockSymbols.Contains(s)) stockSymbols.Add(s);
            var stockMap = await priceLoader.FetchStocksAsync(stockSymbols, deep: true, ct);
            var depth = await ExtendHistoryAsync(stockMap, snapshots, symbols, startDate, ct);
            stockMap = depth.Stocks;

            // Symbols that failed to price (unknown/unpriceable) are silently dropped rather than
            // failing the whole request — the UI shows whichever requested benchmarks actually came
            // back, same fail-soft spirit as the rest of this class.
            var benchmarkPriceLists = symbols
                .Select(s => (Symbol: s,
                    Prices: stockMap.TryGetValue(s, out var data) ? data.HistoricalPrices.OrderBy(x => x.Date).ToList() : []))
                .Where(b => b.Prices.Count > 0)
                .ToList();
            if (benchmarkPriceLists.Count == 0)
                return new BenchmarkComparisonDto
                {
                    Benchmarks = symbols.Select(s => new BenchmarkSeriesDto { Symbol = s }).ToList(),
                    Backfill = depth.Backfill,
                };

            var cursors = new int[benchmarkPriceLists.Count];
            var units = new decimal[benchmarkPriceLists.Count];

            decimal BenchmarkPriceAsOf(int idx, DateOnly date)
            {
                var dt = date.ToDateTime(TimeOnly.MinValue);
                var prices = benchmarkPriceLists[idx].Prices;
                while (cursors[idx] + 1 < prices.Count && prices[cursors[idx] + 1].Date.Date <= dt)
                    cursors[idx]++;
                return prices[cursors[idx]].Date.Date <= dt ? prices[cursors[idx]].Close : 0;
            }

            var fundIds = snapshots
                .SelectMany(s => s.Positions)
                .Where(p => p.AssetType == AssetType.FUND && !string.IsNullOrWhiteSpace(p.Symbol))
                .Select(p => p.Symbol)
                .Distinct()
                .ToList();
            var fundMap = await priceLoader.FetchFundsAsync(fundIds, ct);

            // 含息 inputs for both sides (see the method's remarks for why price-only is unfair to
            // a high-yield portfolio). Benchmark market is inferred from the symbol shape, matching
            // the only two shapes this app's benchmark presets take: TW codes are numeric-leading
            // (0050), US tickers are not (QQQ/VOO/VT).
            var portfolioDivByDate =
                await CostTrackedDividendsByDateAsync(snapshots, fundMap, startDate, today, ct);
            var benchmarkDivRecords = await Task.WhenAll(benchmarkPriceLists.Select(b =>
                dividendData.GetStockDividendsAsync(
                    b.Symbol, char.IsDigit(b.Symbol[0]) ? "TW" : "US", ct)));
            var benchmarkDivByDate = benchmarkDivRecords
                .Select(records => records
                    .GroupBy(r => r.ExDate)
                    .ToDictionary(g => g.Key, g => g.Sum(r => r.AmountPerUnit)))
                .ToArray();

            // Sorted once per request (not per day × position); `stockMap` also holds the benchmark
            // symbols, which this valuation never asks about — a few extra sorts, not worth a filter.
            var asOfPrices = AsOfPriceBook.From(stockMap, fundMap);

            // Same cost-tracked universe as ComputeAnnualizedReturn's terminal value (costBasisTWD +
            // pnlTWD == market value of positions that actually have a recorded cost) — a position
            // with no cost basis filled in has no matching contribution flow driving the benchmark
            // side, so including it here would drift the two curves apart for reasons unrelated to
            // performance.
            decimal? InvestedValueAsOfDate(HoldingSnapshot snap, DateOnly date)
            {
                var dayKey = date.ToString("yyyy-MM-dd");
                var fx = SnapshotFx(snap);
                decimal total = 0;
                foreach (var p in snap.Positions)
                {
                    if (!p.CostBasis.HasValue) continue;
                    if (p.AssetType == AssetType.STOCK)
                    {
                        if (!asOfPrices.Stocks.TryGetValue(p.Symbol, out var series)) return null;
                        var price = series.CloseOnOrBefore(date) ?? 0;
                        if (price <= 0) return null;
                        total += price * p.Quantity * (p.Currency == "USD" ? fx : 1m);
                    }
                    else if (p.AssetType == AssetType.FUND && !string.IsNullOrWhiteSpace(p.Symbol) &&
                             asOfPrices.Funds.TryGetValue(p.Symbol, out var fund))
                    {
                        // No NAV on or before the day falls back to the saved unit price; a NAV that
                        // exists but is <= 0 does not (it skips the position) — hence the nullable.
                        var price = fund.Prices.CloseOnOrBefore(dayKey) ?? (p.UnitPrice ?? 0);
                        if (price <= 0) continue;
                        total += price * p.Quantity * (fund.Currency == "USD" ? fx : 1m);
                    }
                }
                return total;
            }

            var snapshotIdx = 0;

            // Seed the running cost-basis + benchmark unit count with whatever was already
            // invested before the window starts, valued at the window's own start-date price —
            // the same baseline InvestedValueAsOfDate implicitly uses for the portfolio side (it
            // just returns real market value at that date, regardless of when it was actually
            // bought). Without this, a snapshot dated before startDate is invisible to the day
            // loop below (it only reacts to `date == active.EffectiveDate`, which never fires for
            // a pre-window date), so the NEXT snapshot that happens to land inside the window
            // would wrongly "buy" its entire cumulative cost basis at that one later date instead
            // of just its own incremental delta — badly distorting the simulated return.
            var priorSnapshot = snapshots.LastOrDefault(s => s.EffectiveDate <= startDate);
            var prevCost = priorSnapshot?.Positions
                .Where(p => p.AssetType != AssetType.CASH && p.CostBasis.HasValue)
                .Sum(p => p.CostBasis!.Value * ((p.CostCurrency ?? p.Currency) == "USD"
                    ? SnapshotFx(priorSnapshot) : 1m)) ?? 0;
            if (prevCost > 0)
            {
                for (var i = 0; i < benchmarkPriceLists.Count; i++)
                {
                    var startPrice = BenchmarkPriceAsOf(i, startDate);
                    if (startPrice > 0) units[i] = prevCost / startPrice;
                }
            }

            var dates = new List<string>();
            var portfolioValues = new List<decimal>();
            var benchmarkValues = new List<decimal>[benchmarkPriceLists.Count];
            for (var i = 0; i < benchmarkPriceLists.Count; i++) benchmarkValues[i] = [];

            // Time-weighted return accumulators. A plain (last - first) / first over these curves
            // would count deposits as gains: both curves grow when money is added, so an account
            // that contributed 2.7M into a 7M portfolio reads as "+31%" having earned nothing.
            // TWR chains each period's growth factor with that period's contribution removed from
            // the base, which is exactly what makes a portfolio comparable to a benchmark.
            decimal portfolioTwr = 1m;
            var benchmarkTwr = new decimal[benchmarkPriceLists.Count];
            for (var i = 0; i < benchmarkTwr.Length; i++) benchmarkTwr[i] = 1m;
            decimal? prevPortfolioValue = null;
            var prevBenchmarkValue = new decimal?[benchmarkPriceLists.Count];
            // The contribution applied on the day currently being priced; already reflected in
            // both this day's portfolio value and this day's benchmark units.
            decimal contributionToday = 0;
            // Dividends earned but not yet folded into a growth factor. An ex-date can land on a
            // day this loop never records (a weekend, or a day where some symbol has no price), and
            // income dropped on such a day would silently vanish from the return — so it accrues
            // here and is applied on the next day that is actually recorded.
            decimal pendingPortfolioDiv = 0;
            var pendingBenchmarkDiv = new decimal[benchmarkPriceLists.Count];

            for (var date = startDate; date <= today; date = date.AddDays(1))
            {
                // Accrued before every `continue` below, and before this day's contribution is
                // applied: a distribution is earned by the units held going INTO the ex-date, so
                // yesterday's unit count is the correct one to value it at.
                pendingPortfolioDiv += portfolioDivByDate.GetValueOrDefault(date);
                for (var i = 0; i < benchmarkPriceLists.Count; i++)
                    pendingBenchmarkDiv[i] += units[i] * benchmarkDivByDate[i].GetValueOrDefault(date);

                if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) continue;

                while (snapshotIdx + 1 < snapshots.Count && snapshots[snapshotIdx + 1].EffectiveDate <= date)
                    snapshotIdx++;
                var active = snapshots[snapshotIdx].EffectiveDate <= date ? snapshots[snapshotIdx] : null;
                if (active == null) continue;

                // Apply this snapshot's contribution/withdrawal exactly once, the day it takes
                // effect, before pricing each benchmark for that same day — so a same-day
                // contribution is reflected in that day's benchmark values, matching how the
                // portfolio side already reflects it (the snapshot's positions price immediately).
                // The contribution amount is benchmark-independent; only the buy price (and hence
                // units bought) differs per symbol.
                contributionToday = 0;
                if (date == active.EffectiveDate)
                {
                    var snapCost = active.Positions
                        .Where(p => p.AssetType != AssetType.CASH && p.CostBasis.HasValue)
                        .Sum(p => p.CostBasis!.Value * ((p.CostCurrency ?? p.Currency) == "USD"
                            ? SnapshotFx(active) : 1m));
                    var delta = snapCost - prevCost;
                    prevCost = snapCost;
                    contributionToday = delta;

                    if (delta != 0)
                    {
                        for (var i = 0; i < benchmarkPriceLists.Count; i++)
                        {
                            var buyPrice = BenchmarkPriceAsOf(i, date);
                            if (buyPrice > 0) units[i] = Math.Max(0, units[i] + delta / buyPrice);
                        }
                    }
                }

                var portfolioTotal = InvestedValueAsOfDate(active, date);
                if (portfolioTotal is null) continue;

                // Keep every array index-aligned: a date only counts if the portfolio AND every
                // requested benchmark all have a price for it.
                var pricesToday = new decimal[benchmarkPriceLists.Count];
                var allPriced = true;
                for (var i = 0; i < benchmarkPriceLists.Count; i++)
                {
                    var price = BenchmarkPriceAsOf(i, date);
                    if (price <= 0) { allPriced = false; break; }
                    pricesToday[i] = price;
                }
                if (!allPriced) continue;

                dates.Add(date.ToString("yyyy-MM-dd"));
                portfolioValues.Add(portfolioTotal.Value);
                for (var i = 0; i < benchmarkPriceLists.Count; i++)
                    benchmarkValues[i].Add(Math.Round(units[i] * pricesToday[i], 2));

                // Chain this day's growth factor, with today's contribution added to the opening
                // base so the deposit itself never counts as a gain. A base at or below zero has
                // no meaningful growth factor, so that day is skipped rather than allowed to
                // produce a divide-by-zero or a sign flip.
                // Dividends are added to the numerator only: the income counts as return on the
                // day it lands, then leaves the base (it is cash in hand, not reinvested units),
                // so it is credited exactly once.
                var portfolioBase = (prevPortfolioValue ?? 0) + contributionToday;
                if (prevPortfolioValue != null && portfolioBase > 0)
                    portfolioTwr *= (portfolioTotal.Value + pendingPortfolioDiv) / portfolioBase;
                prevPortfolioValue = portfolioTotal.Value;
                pendingPortfolioDiv = 0;

                for (var i = 0; i < benchmarkPriceLists.Count; i++)
                {
                    var todayValue = units[i] * pricesToday[i];
                    var benchBase = (prevBenchmarkValue[i] ?? 0) + contributionToday;
                    if (prevBenchmarkValue[i] != null && benchBase > 0)
                        benchmarkTwr[i] *= (todayValue + pendingBenchmarkDiv[i]) / benchBase;
                    prevBenchmarkValue[i] = todayValue;
                    pendingBenchmarkDiv[i] = 0;
                }
            }

            var result = new BenchmarkComparisonDto
            {
                Dates = dates,
                PortfolioTWD = portfolioValues,
                Benchmarks = benchmarkPriceLists.Select((b, i) => new BenchmarkSeriesDto
                {
                    Symbol = b.Symbol,
                    ValuesTWD = benchmarkValues[i],
                }).ToList(),
                Backfill = depth.Backfill,
            };
            if (dates.Count == 0) return result;

            // Time-weighted, so these are genuine returns rather than "how much the pot grew" —
            // deposits move both curves but must not appear as performance. This is also what
            // makes the two figures directly comparable to each other.
            result.PortfolioReturnPct = Math.Round((portfolioTwr - 1m) * 100, 2);
            for (var i = 0; i < result.Benchmarks.Count; i++)
                result.Benchmarks[i].ReturnPct = Math.Round((benchmarkTwr[i] - 1m) * 100, 2);
            return result;
        }

        /// <summary>Group key for one position under the requested groupBy mode. `fallbackCategory`
        /// is only used in Category mode, for asset types that skip the categoryMap lookup (CASH/
        /// LIABILITY are mapped directly to Cash/Liability without ever entering categoryMap; a
        /// STOCK/FUND symbol somehow missing from a pre-warmed categoryMap falls back to Other
        /// rather than throwing).</summary>
        private static string GroupKey(
            NetWorthGroupBy groupBy, HoldingPosition p,
            Dictionary<string, PortfolioCategory> categoryMap, Dictionary<string, string> subCategoryMap,
            PortfolioCategory fallbackCategory)
        {
            return groupBy switch
            {
                NetWorthGroupBy.Category => p.AssetType switch
                {
                    AssetType.CASH => nameof(PortfolioCategory.Cash),
                    AssetType.LIABILITY => nameof(PortfolioCategory.Liability),
                    AssetType.REAL_ESTATE => nameof(PortfolioCategory.Property),
                    _ => (categoryMap.TryGetValue(p.Symbol, out var cat) ? cat : fallbackCategory).ToString(),
                },
                // Free-form (not the closed PortfolioCategory enum) — CASH/LIABILITY/REAL_ESTATE get
                // a plain Chinese label directly since there's no AI classification to look up for them.
                NetWorthGroupBy.SubCategory => p.AssetType switch
                {
                    AssetType.CASH => "現金",
                    AssetType.LIABILITY => "負債",
                    AssetType.REAL_ESTATE => "不動產",
                    _ => subCategoryMap.TryGetValue(p.Symbol, out var sub) ? sub : "未分類",
                },
                NetWorthGroupBy.Currency => p.Currency == "USD" ? "USD" : "TWD",
                // Verbatim per-holding name for STOCK/FUND; CASH/LIABILITY/REAL_ESTATE collapse into
                // one bucket each (same labels as SubCategory mode) instead of splitting by their
                // free-form Name — multiple cash/liability entries would otherwise clutter the
                // per-holding legend with entries the user doesn't think of as distinct "holdings".
                NetWorthGroupBy.Holding => p.AssetType switch
                {
                    AssetType.CASH => "現金",
                    AssetType.LIABILITY => "負債",
                    AssetType.REAL_ESTATE => "不動產",
                    _ => string.IsNullOrWhiteSpace(p.Name)
                        ? (string.IsNullOrWhiteSpace(p.Symbol) ? "未命名" : p.Symbol)
                        : p.Name,
                },
                _ => p.AssetType switch
                {
                    AssetType.CASH => "Cash",
                    AssetType.LIABILITY => "Liability",
                    // "Property", not "RealEstate": PortfolioCategory.RealEstate (category-mode key)
                    // is REIT stocks/ETFs, a different concept from this literal owned property —
                    // matches Category mode's own key for this AssetType (nameof(Property)) so both
                    // modes render the exact same series identity/color for it.
                    AssetType.REAL_ESTATE => "Property",
                    AssetType.FUND => "Fund",
                    _ => p.Market == "US" ? "UsStock" : "TwStock", // STOCK
                },
            };
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        /// <summary>
        /// Per-ex-date TWD dividends earned by the COST-TRACKED stock/fund positions only, for the
        /// window [startDate, today]. Deliberately not <see cref="ComputeDividends"/>: that one
        /// covers every position whether or not a cost basis was ever filled in, and the benchmark
        /// comparison prices only cost-tracked holdings on both sides. Crediting the portfolio curve
        /// with income from a position that contributes nothing to the benchmark side would drift
        /// the two apart for reasons unrelated to performance — the same universe argument that
        /// governs InvestedValueAsOfDate.
        ///
        /// Quantity and FX are read from the snapshot active on the ex-date, so a dividend counts
        /// against what was actually held when it was paid, not today's position.
        /// </summary>
        private async Task<Dictionary<DateOnly, decimal>> CostTrackedDividendsByDateAsync(
            List<HoldingSnapshot> orderedSnapshots, Dictionary<string, FundNavHistory> fundMap,
            DateOnly startDate, DateOnly today, CancellationToken ct)
        {
            var byDate = new Dictionary<DateOnly, decimal>();

            var keys = orderedSnapshots
                .SelectMany(s => s.Positions)
                .Where(p => (p.AssetType == AssetType.STOCK || p.AssetType == AssetType.FUND)
                    && p.CostBasis.HasValue && !string.IsNullOrWhiteSpace(p.Symbol))
                .Select(p => (p.AssetType, Symbol: p.Symbol, Market: p.Market))
                .Distinct()
                .ToList();
            if (keys.Count == 0) return byDate;

            var fetched = await Task.WhenAll(keys.Select(async key => (
                Key: key,
                Records: key.AssetType == AssetType.STOCK
                    ? await dividendData.GetStockDividendsAsync(key.Symbol, key.Market, ct)
                    : await dividendData.GetFundDividendsAsync(key.Symbol, ct)
            )));

            foreach (var (key, records) in fetched)
            {
                foreach (var record in records)
                {
                    if (record.ExDate < startDate || record.ExDate > today) continue;

                    var snap = orderedSnapshots.LastOrDefault(s => s.EffectiveDate <= record.ExDate);
                    var position = snap?.Positions.FirstOrDefault(p =>
                        p.AssetType == key.AssetType && p.Symbol == key.Symbol && p.Market == key.Market);
                    if (snap == null || position == null || position.Quantity <= 0 || !position.CostBasis.HasValue)
                        continue;

                    // FundRich's dividend history has no currency field; fall back to the fund's NAV currency.
                    var currency = key.AssetType == AssetType.FUND
                        ? (fundMap.TryGetValue(key.Symbol, out var nav) ? nav.Currency : "TWD")
                        : record.Currency;
                    var fxMul = currency == "USD" ? SnapshotFx(snap) : 1m;

                    byDate[record.ExDate] = byDate.GetValueOrDefault(record.ExDate)
                        + position.Quantity * record.AmountPerUnit * fxMul;
                }
            }

            return byDate;
        }

        /// <summary>
        /// Effective date of the snapshot where the position's CURRENT unbroken holding period
        /// begins, or null when it isn't held in the latest snapshot. Walks backwards from the most
        /// recent snapshot and stops at the first one where the position is missing or zeroed —
        /// that gap is a sale, and everything before it belongs to an earlier, closed holding
        /// period. Snapshots are not daily, so "unbroken" means present in every snapshot in the
        /// run, which is the most this model can know.
        /// </summary>
        private static DateOnly? CurrentHoldingStart(
            List<HoldingSnapshot> orderedSnapshots, (AssetType AssetType, string Symbol, string Market) key)
        {
            DateOnly? start = null;
            for (var i = orderedSnapshots.Count - 1; i >= 0; i--)
            {
                var position = orderedSnapshots[i].Positions.FirstOrDefault(p =>
                    p.AssetType == key.AssetType && p.Symbol == key.Symbol && p.Market == key.Market);
                if (position == null || position.Quantity <= 0) break;
                start = orderedSnapshots[i].EffectiveDate;
            }
            return start;
        }

        /// <summary>
        /// Dividend history for every stock/fund position that appears in any snapshot, one fetch
        /// per distinct position, in parallel (each individually cached). Input for
        /// <see cref="ComputeDividends"/>; split from it so the fetch can run alongside the price
        /// fetches, which it doesn't depend on.
        /// </summary>
        private async Task<List<((AssetType AssetType, string Symbol, string Market) Key, List<DividendRecord> Records)>>
            FetchDividendRecordsAsync(List<HoldingSnapshot> orderedSnapshots, CancellationToken ct)
        {
            var keys = orderedSnapshots
                .SelectMany(s => s.Positions)
                .Where(p => (p.AssetType == AssetType.STOCK || p.AssetType == AssetType.FUND)
                    && !string.IsNullOrWhiteSpace(p.Symbol))
                .Select(p => (p.AssetType, Symbol: p.Symbol, Market: p.Market))
                .Distinct()
                .ToList();
            if (keys.Count == 0) return [];

            var fetched = await Task.WhenAll(keys.Select(async key => (
                Key: key,
                Records: key.AssetType == AssetType.STOCK
                    ? await dividendData.GetStockDividendsAsync(key.Symbol, key.Market, ct)
                    : await dividendData.GetFundDividendsAsync(key.Symbol, ct)
            )));
            return fetched.ToList();
        }

        /// <summary>
        /// Cash dividends actually earned while holding each stock/fund position, walking the whole
        /// snapshot timeline so a dividend counts against whatever quantity was held on its ex-date
        /// (not today's quantity). Dividends before the user's first snapshot are excluded — the app
        /// has no way to know what was held before that date, same honesty policy as XIRR.
        ///
        /// TotalTWD and Flows are lifetime figures: income earned during a holding period that has
        /// since been closed was still really earned, and XIRR still needs it as a dated inflow.
        /// ByPosition is NOT — it is scoped to each position's CURRENT unbroken holding period,
        /// because it is displayed on the position itself. Sell a fund and buy it back weeks later
        /// and the old period's payout would otherwise appear on the new position the very day it
        /// is opened, which reads as income the new holding never produced.
        /// </summary>
        private (
            decimal TotalTWD,
            Dictionary<(AssetType AssetType, string Symbol, string Market), decimal> ByPosition,
            List<(DateOnly Date, decimal AmountTWD)> Flows)
            ComputeDividends(
                List<HoldingSnapshot> orderedSnapshots, decimal fx,
                Dictionary<string, FundNavHistory> fundMap,
                IReadOnlyList<((AssetType AssetType, string Symbol, string Market) Key, List<DividendRecord> Records)> fetched)
        {
            var empty = new Dictionary<(AssetType, string, string), decimal>();
            var noFlows = new List<(DateOnly, decimal)>();
            if (orderedSnapshots.Count == 0 || fetched.Count == 0) return (0, empty, noFlows);

            var firstDate = orderedSnapshots[0].EffectiveDate;
            var today = Today;

            var byPosition = new Dictionary<(AssetType, string, string), decimal>();
            var flows = new List<(DateOnly, decimal)>();
            decimal total = 0;

            foreach (var (key, records) in fetched)
            {
                // Start of the position's current unbroken run of being held, or null if it isn't
                // held in the latest snapshot at all. Only payouts on or after this date belong to
                // the position as it exists today; earlier ones belong to a closed holding period.
                var currentHoldingStart = CurrentHoldingStart(orderedSnapshots, key);
                decimal currentPeriodTotal = 0;

                foreach (var record in records)
                {
                    if (record.ExDate < firstDate || record.ExDate > today) continue;

                    // Whatever was actively held on the ex-date — not today's quantity. A position
                    // absent from the snapshot active on that date was not held then, which is what
                    // correctly drops payouts falling inside a gap between holding periods.
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
                    total += amountTWD;
                    flows.Add((record.ExDate, amountTWD));

                    if (currentHoldingStart != null && record.ExDate >= currentHoldingStart)
                        currentPeriodTotal += amountTWD;
                }

                if (currentPeriodTotal != 0)
                    byPosition[(key.AssetType, key.Symbol, key.Market)] = currentPeriodTotal;
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
            List<(DateOnly Date, decimal AmountTWD)> dividendFlows, DateOnly today)
        {
            if (terminalValueTWD <= 0) return null;
            if (orderedSnapshots.Count == 0) return null;

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

        private static bool ArePositionsEqual(ICollection<HoldingPosition> a, ICollection<HoldingPosition> b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a.Count != b.Count) return false;
            var dictA = a.GroupBy(p => (p.AssetType, Symbol: (p.Symbol ?? "").ToUpperInvariant(), Market: (p.Market ?? "").ToUpperInvariant(), Currency: (p.Currency ?? "").ToUpperInvariant()))
                         .ToDictionary(g => g.Key, g => g.Sum(p => p.Quantity));
            var dictB = b.GroupBy(p => (p.AssetType, Symbol: (p.Symbol ?? "").ToUpperInvariant(), Market: (p.Market ?? "").ToUpperInvariant(), Currency: (p.Currency ?? "").ToUpperInvariant()))
                         .ToDictionary(g => g.Key, g => g.Sum(p => p.Quantity));
            if (dictA.Count != dictB.Count) return false;
            foreach (var (k, v) in dictA)
            {
                if (!dictB.TryGetValue(k, out var bv) || bv != v) return false;
            }
            return true;
        }

        /// <summary>
        /// Re-values a set of positions at their closing price as of <paramref name="date"/>, using
        /// already-fetched price histories (no network calls). Returns null if any non-cash position
        /// has no price on or before that date, so the caller can treat the whole total as unknown
        /// rather than silently under-counting a position as worth 0.
        /// </summary>
        private static decimal? ValueAsOfDate(
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
                if (p.AssetType == AssetType.REAL_ESTATE)
                {
                    if (includeRealEstate) total += p.Quantity * Fx(p, p.Currency);
                    continue;
                }
                if (p.AssetType == AssetType.CASH)
                {
                    total += p.Quantity * Fx(p, p.Currency);
                    continue;
                }
                if (p.AssetType == AssetType.LIABILITY)
                {
                    if (includeLiability) total -= p.Quantity * Fx(p, p.Currency);
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

        private sealed record ExtendedHistory(Dictionary<string, DTO.Finance.MarketData> Stocks, HistoryBackfillDto? Backfill);

        /// <summary>How many benchmark symbols one request may put on the backfill queue; the
        /// symbols are typed by the caller, and each is a month-by-month fetch from the exchange.</summary>
        private const int MaxBenchmarkBackfills = 5;

        /// <summary>The first day the prices are needed from, for a stock held from
        /// <paramref name="heldFrom"/>: a few days earlier, so the day it was first held is priced
        /// from the close before it, not from nothing, across a weekend or a holiday.</summary>
        private const int HistoryMarginDays = 10;

        /// <summary>
        /// Adds the older prices that the background backfill has stored to <paramref name="stocks"/>
        /// — the providers' own history stops about six months back — and says how far the
        /// backfill has got. It never fetches anything itself: what is missing is queued for the
        /// background, so a 3-year chart still comes back as fast as a 3-month one, drawn from what
        /// is stored so far. Fail-soft like the rest of this class: if the extension fails, the
        /// chart is drawn from the prices that were already in hand.
        /// </summary>
        private async Task<ExtendedHistory> ExtendHistoryAsync(
            Dictionary<string, DTO.Finance.MarketData> stocks, IReadOnlyList<HoldingSnapshot> snapshots,
            IReadOnlyCollection<string> benchmarkSymbols, DateOnly startDate, CancellationToken ct)
        {
            // A window the providers already cover needs nothing from this.
            var deepWindowStart = Today.AddMonths(-(PersistentTwHistoricalProvider.LookbackMonths - 1));
            if (deepHistory is null || startDate >= deepWindowStart) return new ExtendedHistory(stocks, null);

            try
            {
                var wants = HistoryWants(snapshots, benchmarkSymbols, startDate);
                var result = await deepHistory.ExtendAsync(stocks, wants, startDate, ct);
                var status = result.Backfill;
                return new ExtendedHistory(result.Stocks, status is null ? null : new HistoryBackfillDto
                {
                    State = status.State,
                    CoveredFrom = status.CoveredFrom.ToString("yyyy-MM-dd"),
                    TargetFrom = status.TargetFrom.ToString("yyyy-MM-dd"),
                });
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Could not extend price history back to {StartDate}; using the prices in hand", startDate);
                return new ExtendedHistory(stocks, null);
            }
        }

        /// <summary>What each stock in the snapshots (and each benchmark) needs of its history to
        /// price the window from <paramref name="startDate"/>. A held stock is priced from the day it
        /// was first held, so there is no reason to fetch the years before that; a benchmark is
        /// priced for the whole window.</summary>
        internal static List<HistoryWant> HistoryWants(
            IReadOnlyList<HoldingSnapshot> snapshots, IReadOnlyCollection<string> benchmarkSymbols, DateOnly startDate)
        {
            var heldFrom = new Dictionary<string, DateOnly>(StringComparer.Ordinal);
            foreach (var snapshot in snapshots)
                foreach (var position in snapshot.Positions)
                {
                    if (position.AssetType != AssetType.STOCK || string.IsNullOrWhiteSpace(position.Symbol)) continue;
                    if (!heldFrom.TryGetValue(position.Symbol, out var first) || snapshot.EffectiveDate < first)
                        heldFrom[position.Symbol] = snapshot.EffectiveDate;
                }

            var benchmarks = benchmarkSymbols.Take(MaxBenchmarkBackfills).ToHashSet(StringComparer.Ordinal);
            var wants = new List<HistoryWant>();
            foreach (var symbol in heldFrom.Keys.Concat(benchmarks).Distinct(StringComparer.Ordinal))
            {
                DateOnly? held = heldFrom.TryGetValue(symbol, out var from) ? from : null;
                var needFrom = benchmarks.Contains(symbol) || held is null
                    ? startDate
                    : Max(startDate, held.Value.AddDays(-HistoryMarginDays));
                wants.Add(new HistoryWant(symbol, needFrom, held));
            }
            return wants;
        }

        private static DateOnly Max(DateOnly a, DateOnly b) => a >= b ? a : b;

    }
}
