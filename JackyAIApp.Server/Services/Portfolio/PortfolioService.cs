using JackyAIApp.Server.Common;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.Data.Models.Portfolio;
using JackyAIApp.Server.Data.Models.SQL;
using JackyAIApp.Server.DTO.Portfolio;
using JackyAIApp.Server.Services.Market;
using static JackyAIApp.Server.Services.Portfolio.PortfolioValuation;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;

namespace JackyAIApp.Server.Services.Portfolio
{
    public class PortfolioService(
        AzureSQLDBContext db,
        IHoldingSnapshotReader snapshotReader,
        IPortfolioPriceLoader priceLoader,
        IDividendService dividendData,
        TimeProvider timeProvider,
        ITdccFxService tdccFx,
        ICathayFxService cathayFx,
        ServerTiming timing,
        ITradingCalendar tradingCalendar) : IPortfolioService
    {
        /// <summary>"Today" for every date walk in this service (XIRR, day change, dividends) — the
        /// constructor's <c>timeProvider</c> lets tests pin a fixed date instead of chasing the real
        /// calendar (a relative-to-real-now test fixture drifts out of whatever window a test
        /// expects as time passes; see PortfolioDrawdownTests/PortfolioBenchmarkComparisonTests).</summary>
        private DateOnly Today => DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

        /// <summary>Runs <paramref name="work"/>, recording how long it took in this request's
        /// Server-Timing header.</summary>
        private Task<T> Timed<T>(string name, Func<Task<T>> work, Func<T, string>? describe = null) =>
            timing.MeasureAsync(name, work, describe);

        private Task Timed(string name, Func<Task> work) => timing.MeasureAsync(name, work);

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
                // Each type keeps only the fields it has a use for (see AssetValuationPolicy.Normalize).
                // A row of an asset type that doesn't exist (the API binds numbers too) is dropped
                // like an empty one, rather than stored for every later read to trip over.
                Positions = req.Positions
                    .Where(p => Enum.IsDefined(p.AssetType))
                    .Select(p => AssetValuationPolicy.For(p.AssetType).Normalize(p))
                    .OfType<HoldingPosition>()
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
            var prevTradingDay = tradingCalendar.PreviousTradingDay(Today);
            var prevSnapshot = allSnapshots.LastOrDefault(s => s.EffectiveDate <= prevTradingDay)
                ?? allSnapshots.FirstOrDefault();
            var allPositions = prevSnapshot != null
                ? snapshot.Positions.Concat(prevSnapshot.Positions).ToList()
                : snapshot.Positions;

            var stockTask = Timed("stocks", () => priceLoader.FetchStocksAsync(
                allPositions.SymbolsPricedBy(PriceSource.StockQuote), deep: false, ct),
                m => $"{m.Count} symbols");
            var fundTask = Timed("funds", () => priceLoader.FetchFundsAsync(
                allPositions.SymbolsPricedBy(PriceSource.FundNav), ct),
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
            var tdccCurrencies = fundMap.Values
                .Select(n => n.Currency)
                .Where(c => !string.IsNullOrWhiteSpace(c) && c != "TWD")
                .Distinct()
                .ToList();
            // allPositions, not just today's: a US stock sold since the previous trading day is still
            // valued on that side of the day change, and must be valued at the same rate.
            var cathayUsKeys = allPositions
                .Where(p => p.Currency == "USD"
                    && AssetValuationPolicy.For(p.AssetType).PublishedFx == PublishedFxSource.CathayOnQuoteDay)
                .Select(p => AssetValuationPolicy.For(p.AssetType).Latest(p, stockMap, fundMap)?.Day)
                .Where(d => d.HasValue)
                .Select(d => (Currency: "USD", Date: d!.Value))
                .Distinct()
                .ToList();

            var tdccRatesTask = Task.WhenAll(tdccCurrencies.Select(async currency =>
                (Currency: currency, Rate: await tdccFx.GetBuyRateAsync(currency, Today, ct))));
            var cathayRatesTask = Task.WhenAll(cathayUsKeys.Select(async key =>
                (Key: key, Rate: await cathayFx.GetBuyRateAsync(key.Currency, key.Date, ct))));
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
                var policy = AssetValuationPolicy.For(position.AssetType);
                if (policy.PublishedFx != PublishedFxSource.CathayOnQuoteDay) return null;
                if (policy.Latest(position, stockMap, fundMap)?.Day is not { } quoteDay) return null;
                return cathayRatesByDate.TryGetValue((currency, quoteDay), out var r) ? r : null;
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
                if (AssetValuationPolicy.For(position.AssetType).PublishedFx == PublishedFxSource.Tdcc
                    && tdccRates.TryGetValue(currency, out var fundFx))
                    return (fundFx, FxRateSources.Tdcc);
                if (CathayRateFor(position, currency) is { } usFx) return (usFx, FxRateSources.Cathay);
                return currency == "USD" ? (fx, FxRateSources.Mid) : (1m, null);
            }
            decimal PositionFx(HoldingPosition position, string? priceCurrency) => ResolveFx(position, priceCurrency).Rate;

            foreach (var p in snapshot.Positions)
            {
                var policy = AssetValuationPolicy.For(p.AssetType);
                decimal currentPrice;
                // Pricing currency: the stored one, except a fund's live NAV currency from FundRich
                // (authoritative), not a manually-picked value — see the policy's Latest.
                var priceCurrency = p.Currency;

                if (policy.IsMarketPriced)
                {
                    var latest = policy.Latest(p, stockMap, fundMap);
                    currentPrice = latest?.Price ?? 0;
                    if (latest is { } quote) priceCurrency = quote.Currency;
                }
                else // an amount, not a price × quantity — always "priced" at 1, no external lookup needed.
                {
                    currentPrice = 1;
                }

                // A market-priced position with no usable price has no real market value. Treat it
                // as "price unavailable" so we don't book a phantom loss of the full cost.
                var priceFailed = policy.IsMarketPriced && currentPrice <= 0;

                var (fxMul, fxSource) = ResolveFx(p, priceCurrency);
                // The policy's Sign is the one place a position's value goes negative (a liability) —
                // the sole sign-flip point; every downstream sum (net worth, history, exports) just
                // adds valueTWD and is correct with no special-casing.
                var valueLocal = policy.Sign * (policy.IsMarketPriced ? currentPrice * p.Quantity : p.Quantity);
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
                if (policy.HasCostBasis && p.CostBasis.HasValue)
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

                switch (policy.BucketOf(p))
                {
                    case AssetBucket.UsStock: usStock += valueTWD; break;
                    case AssetBucket.TwStock: twStock += valueTWD; break;
                    case AssetBucket.Cash: cash += valueTWD; break;
                    case AssetBucket.Fund: fund += valueTWD; break;
                    case AssetBucket.Property: realEstate += valueTWD; break;
                    // valueTWD is already negative here — this is display total (owed amount),
                    // shown positive by negating at the DTO boundary below.
                    case AssetBucket.Liability: liability += valueTWD; break;
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

            timing.Add("total", total.Elapsed);
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
        /// route through <see cref="Finance.DataProviders.IMarketDataProviderFactory"/> at all (FUND pricing goes through
        /// <see cref="IFundDataService"/> instead), so a symbol not currently held as a STOCK is a
        /// silent no-op rather than an error — this also stops the endpoint being usable to force
        /// arbitrary external-API fetches for symbols the caller doesn't even hold.
        /// </summary>
        public async Task<PortfolioSummaryDto> RefreshPositionPriceAsync(
            string userId, string symbol, decimal feeDiscountRate = 0.28m, CancellationToken ct = default)
        {
            symbol = (symbol ?? "").ToUpperInvariant();
            var snapshot = await snapshotReader.LatestAsync(userId, ct);
            var owns = snapshot?.Positions.Any(p =>
                AssetValuationPolicy.For(p.AssetType).PriceSource == PriceSource.StockQuote && p.Symbol == symbol) ?? false;

            if (owns)
                await priceLoader.InvalidateStockAsync(symbol, ct);

            return await GetPortfolioSummaryAsync(userId, feeDiscountRate, ct);
        }

        // ── Helpers ──────────────────────────────────────────────────────────

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
                .Where(p => AssetValuationPolicy.For(p.AssetType).PaysDividends && !string.IsNullOrWhiteSpace(p.Symbol))
                .Select(p => (p.AssetType, Symbol: p.Symbol, Market: p.Market))
                .Distinct()
                .ToList();
            if (keys.Count == 0) return [];

            var fetched = await Task.WhenAll(keys.Select(async key => (
                Key: key,
                Records: await dividendData.GetDividendsAsync(key.AssetType, key.Symbol, key.Market, ct)
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

                    var currency = AssetValuationPolicy.For(key.AssetType).DividendCurrency(record, key.Symbol, fundMap);
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
                    .Where(p => AssetValuationPolicy.For(p.AssetType).HasCostBasis && p.CostBasis.HasValue)
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
            return positions.Any(p => AssetValuationPolicy.For(p.AssetType).Latest(p, stockMap, fundMap)?.Day > cutoff);
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


    }
}
