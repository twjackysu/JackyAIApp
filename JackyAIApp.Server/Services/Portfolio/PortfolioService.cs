using JackyAIApp.Server.Common;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.Data.Models.Portfolio;
using JackyAIApp.Server.Data.Models.SQL;
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
        ILogger<PortfolioService> logger,
        TimeProvider? timeProvider = null,
        // Optional for the same reason TimeProvider is: tests construct this class directly. The
        // container always supplies the real one (it is registered), and a null here simply means
        // funds fall back to the mid-market rate — the behaviour before 集保 rates existed.
        ITdccFxService? tdccFx = null) : IPortfolioService
    {
        private const string FX_URL = "https://open.er-api.com/v6/latest/USD";
        private const string FX_CACHE_KEY = "portfolio_usd_twd_rate";
        private const string STOCK_CACHE_PREFIX = "portfolio_stock_";

        private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

        /// <summary>"Today" for every date-range walk in this service (net worth history,
        /// drawdown, benchmark comparison, XIRR, day-change) — overridable via the constructor's
        /// <c>timeProvider</c> parameter so tests can pin a fixed date instead of chasing the real
        /// calendar (a relative-to-real-now test fixture drifts out of whatever window a test
        /// expects as time passes; see PortfolioDrawdownTests/PortfolioBenchmarkComparisonTests).</summary>
        private DateOnly Today => DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);

        // ── Save / read holdings ─────────────────────────────────────────────

        public async Task SaveHoldingsAsync(string userId, SaveHoldingsRequest req, CancellationToken ct = default)
        {
            var effectiveDate = req.EffectiveDate ?? Today;
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
        }

        public async Task<SaveHoldingsRequest> GetLatestHoldingsAsync(string userId, CancellationToken ct = default)
        {
            var snapshot = await LatestSnapshotAsync(userId, ct);
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
            var snapshot = await LatestSnapshotAsync(userId, ct);
            if (snapshot == null) return new PortfolioSummaryDto();

            // Defensive clamp rather than rejecting the request — this is a display-affecting
            // client-supplied value (not persisted anywhere server-side), not a validated business
            // input. 0 would mean free trades, > 1 is not a discount at all.
            feeDiscountRate = Math.Clamp(feeDiscountRate, 0.01m, 1m);

            // Current valuation always uses the live rate; the rate stored on each snapshot is only
            // for reconstructing historical net worth (a stale fallback-32 shouldn't stick forever).
            var fx = await FetchUsdTwdRateAsync(ct);
            var positions = new List<PositionDto>();
            decimal twStock = 0, usStock = 0, cash = 0, fund = 0, liability = 0, realEstate = 0;
            decimal costBasisTWD = 0, pnlTWD = 0, estimatedSellCostTWD = 0;
            decimal equityExposureTWD = 0; // Σ market value × exposure factor (leverage/bond-adjusted)

            // Prefetch all stock prices and fund NAVs in parallel (cached) before the value loop,
            // instead of awaiting one network call per position serially. Current valuation + day-
            // change only ever look at the last couple of trading days (see ValueAsOfDate below), so
            // this is a shallow (latest-only) fetch — a never-before-added symbol shouldn't cost a
            // 6-month history pull just to show today's market value.
            var stockTask = FetchStocksAsync(
                snapshot.Positions.Where(p => p.AssetType == AssetType.STOCK).Select(p => p.Symbol), deep: false, ct);
            var fundTask = FetchFundsAsync(
                snapshot.Positions.Where(p => p.AssetType == AssetType.FUND).Select(p => p.Symbol), ct);
            await Task.WhenAll(stockTask, fundTask);
            var stockMap = stockTask.Result;
            var fundMap = fundTask.Result;

            // 集保 settlement rates for the fund currencies actually held. A fund bought through
            // 基富通 is converted back to TWD at the rate 集保 negotiates with 華南銀行, so that is
            // what the holder really receives and what 基富通's own app displays — valuing it at a
            // mid-market rate instead put this app ~0.5% away from the number the user was
            // reconciling against. Only funds; stocks do not settle through 集保.
            var tdccRates = new Dictionary<string, decimal>();
            if (tdccFx != null)
            {
                var fundCurrencies = fundMap.Values
                    .Select(n => n.Currency)
                    .Where(c => !string.IsNullOrWhiteSpace(c) && c != "TWD")
                    .Distinct()
                    .ToList();
                foreach (var currency in fundCurrencies)
                {
                    var rate = await tdccFx.GetBuyRateAsync(currency, Today, ct);
                    if (rate is > 0) tdccRates[currency] = rate.Value;
                }
            }

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

                // Funds convert at 集保's published rate; everything else stays on the mid-market
                // rate. Falls back to mid-market when 集保 has nothing for that currency — an FX
                // lookup must never be what stops a portfolio from being valued.
                var fxMul = 1m;
                if (!string.IsNullOrWhiteSpace(priceCurrency) && priceCurrency != "TWD")
                {
                    if (p.AssetType == AssetType.FUND && tdccRates.TryGetValue(priceCurrency, out var fundFx))
                        fxMul = fundFx;
                    else if (priceCurrency == "USD")
                        fxMul = fx;
                }
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
                var costCurrency = p.CostCurrency ?? p.Currency;
                var costFxMul = 1m;
                if (!string.IsNullOrWhiteSpace(costCurrency) && costCurrency != "TWD")
                {
                    if (p.AssetType == AssetType.FUND && tdccRates.TryGetValue(costCurrency, out var fundCostFx))
                        costFxMul = fundCostFx;
                    else if (costCurrency == "USD")
                        costFxMul = fx;
                }

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
                    FxRateSource = fxMul == 1m
                        ? null
                        : (p.AssetType == AssetType.FUND && tdccRates.ContainsValue(fxMul)
                            ? FxRateSources.Tdcc
                            : "MID"),
                    PriceFetchFailed = priceFailed,
                    ValuationMethod = p.ValuationMethod,
                });
            }

            // liability is already negative (see the valuation loop above), so this is a plain sum —
            // no special-casing needed for net worth to correctly net out debt.
            var netWorthTWD = twStock + usStock + cash + fund + realEstate + liability;

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
                var tagRows = await db.StockSymbolInfos
                    .Where(s => taggedSymbols.Contains(s.Symbol) && s.Tags != null)
                    .Select(s => new { s.Symbol, s.Tags })
                    .ToListAsync(ct);
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
            var prevTradingDay = PreviousTradingDay(Today);
            decimal? dayChangeTWD = null, dayChangePct = null;
            if (HasNewerPriceThan(snapshot.Positions, prevTradingDay, stockMap, fundMap))
            {
                var prevNetWorthTWD = ValueAsOfDate(snapshot.Positions, prevTradingDay, fx, stockMap, fundMap);
                if (prevNetWorthTWD is > 0)
                {
                    dayChangeTWD = netWorthTWD - prevNetWorthTWD.Value;
                    // % denominator is gross liquid assets (excl. real estate, not net of
                    // liability) — same basis as UnrealizedPnlPct/TotalReturnPct below, per user
                    // request: real estate and debt shouldn't dilute/distort how big a price move
                    // reads as a percentage of the portfolio that's actually exposed to it.
                    var prevLiquidGrossTWD = ValueAsOfDate(
                        snapshot.Positions, prevTradingDay, fx, stockMap, fundMap, liquidGrossOnly: true);
                    if (prevLiquidGrossTWD is > 0)
                        dayChangePct = Math.Round(dayChangeTWD.Value / prevLiquidGrossTWD.Value * 100, 2);
                }
            }

            // Shared denominator for the three headline percentages (day change above, unrealized
            // and total-return below): gross liquid assets only — real estate is manually-valued
            // and shouldn't dilute a price-driven %, and liability isn't netted out so a mortgage
            // doesn't inflate the ratio the way "÷ (assets - debt)" would.
            var liquidGrossAssetsTWD = twStock + usStock + cash + fund;
            // Liquid assets net of debt — the actual equity backing a leveraged position, and the
            // only sensible base for the two leverage gauges below. Real estate is excluded on
            // purpose: a house can't be liquidated to meet a margin call, so counting it would
            // dilute exactly the risk these gauges exist to surface. `liability` is already
            // negative here (see the sign-flip invariant at the single valuation point above).
            var liquidNetWorthTWD = liquidGrossAssetsTWD + liability;

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
            var snapshot = await LatestSnapshotAsync(userId, ct);
            var owns = snapshot?.Positions.Any(p => p.AssetType == AssetType.STOCK && p.Symbol == symbol) ?? false;

            if (owns)
            {
                cache.Remove(STOCK_CACHE_PREFIX + "deep_" + symbol);
                cache.Remove(STOCK_CACHE_PREFIX + "latest_" + symbol);

                var region = providerFactory.DetectRegion(symbol);
                var provider = providerFactory.GetMarketDataProvider(region);
                await provider.InvalidateAsync(symbol, ct);
            }

            return await GetPortfolioSummaryAsync(userId, feeDiscountRate, ct);
        }

        // ── Net worth history ────────────────────────────────────────────────

        public async Task<List<NetWorthDataPoint>> GetNetWorthHistoryAsync(
            string userId, int days, NetWorthGroupBy groupBy = NetWorthGroupBy.AssetType, CancellationToken ct = default)
        {
            days = Math.Clamp(days, 7, 180);

            var snapshots = await AllSnapshotsAsync(userId, ct);

            if (snapshots.Count == 0) return [];

            var today = Today;
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
            // Deep fetch — this walks up to `days` (clamped to 180) of history per symbol below.
            var priceMap = (await FetchStocksAsync(stockSymbols, deep: true, ct))
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

            var snapshots = await AllSnapshotsAsync(userId, ct);
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
            var stockMap = await FetchStocksAsync(stockSymbols, deep: true, ct);

            var fundIds = snapshots
                .SelectMany(s => s.Positions)
                .Where(p => p.AssetType == AssetType.FUND && !string.IsNullOrWhiteSpace(p.Symbol))
                .Select(p => p.Symbol)
                .Distinct()
                .ToList();
            var fundMap = await FetchFundsAsync(fundIds, ct);

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

                var fx = active.UsdTwdRate > 0 ? active.UsdTwdRate : 32m;
                var total = ValueAsOfDate(active.Positions, date, fx, stockMap, fundMap);
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

            if (points.Count == 0) return new DrawdownAnalysisDto();

            var result = new DrawdownAnalysisDto
            {
                Points = points,
                CurrentDrawdownPct = points[^1].DrawdownPct,
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

            var snapshots = await AllSnapshotsAsync(userId, ct);
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
            var stockMap = await FetchStocksAsync(stockSymbols, deep: true, ct);

            // Symbols that failed to price (unknown/unpriceable) are silently dropped rather than
            // failing the whole request — the UI shows whichever requested benchmarks actually came
            // back, same fail-soft spirit as the rest of this class.
            var benchmarkPriceLists = symbols
                .Select(s => (Symbol: s,
                    Prices: stockMap.TryGetValue(s, out var data) ? data.HistoricalPrices.OrderBy(x => x.Date).ToList() : []))
                .Where(b => b.Prices.Count > 0)
                .ToList();
            if (benchmarkPriceLists.Count == 0)
                return new BenchmarkComparisonDto { Benchmarks = symbols.Select(s => new BenchmarkSeriesDto { Symbol = s }).ToList() };

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
            var fundMap = await FetchFundsAsync(fundIds, ct);

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

            // Same cost-tracked universe as ComputeAnnualizedReturn's terminal value (costBasisTWD +
            // pnlTWD == market value of positions that actually have a recorded cost) — a position
            // with no cost basis filled in has no matching contribution flow driving the benchmark
            // side, so including it here would drift the two curves apart for reasons unrelated to
            // performance.
            decimal? InvestedValueAsOfDate(HoldingSnapshot snap, DateOnly date)
            {
                var dt = date.ToDateTime(TimeOnly.MinValue);
                var dayKey = date.ToString("yyyy-MM-dd");
                var fx = snap.UsdTwdRate > 0 ? snap.UsdTwdRate : 32m;
                decimal total = 0;
                foreach (var p in snap.Positions)
                {
                    if (!p.CostBasis.HasValue) continue;
                    if (p.AssetType == AssetType.STOCK)
                    {
                        if (!stockMap.TryGetValue(p.Symbol, out var data)) return null;
                        var price = data.HistoricalPrices
                            .Where(x => x.Date.Date <= dt).OrderByDescending(x => x.Date)
                            .FirstOrDefault()?.Close ?? 0;
                        if (price <= 0) return null;
                        total += price * p.Quantity * (p.Currency == "USD" ? fx : 1m);
                    }
                    else if (p.AssetType == AssetType.FUND && !string.IsNullOrWhiteSpace(p.Symbol) &&
                             fundMap.TryGetValue(p.Symbol, out var nav))
                    {
                        var price = nav.Prices
                            .Where(x => string.CompareOrdinal(x.Date, dayKey) <= 0)
                            .OrderByDescending(x => x.Date, StringComparer.Ordinal)
                            .FirstOrDefault()?.Close ?? (p.UnitPrice ?? 0);
                        if (price <= 0) continue;
                        total += price * p.Quantity * (nav.Currency == "USD" ? fx : 1m);
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
                    ? (priorSnapshot.UsdTwdRate > 0 ? priorSnapshot.UsdTwdRate : 32m) : 1m)) ?? 0;
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
                            ? (active.UsdTwdRate > 0 ? active.UsdTwdRate : 32m) : 1m));
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

        // ── Lookup + price history ───────────────────────────────────────────

        public async Task<StockLookupDto?> LookupStockAsync(string market, string symbol, CancellationToken ct = default)
        {
            // Only the current price is shown here — shallow fetch.
            var data = await FetchStockDataAsync(symbol, deep: false, ct);
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

            // Chart needs the full series — deep fetch.
            var data = await FetchStockDataAsync(symbol, deep: true, ct);
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
                // Technical indicators (MAs, S/R) need real history — deep fetch.
                var data = await FetchStockDataAsync(symbol, deep: true, ct);
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

        private static readonly TimeSpan TwListedCompanyRefreshInterval = TimeSpan.FromHours(24);

        private async Task<List<StockSearchItem>> SearchTwStocksAsync(string keyword, CancellationToken ct)
        {
            await RefreshTwListedCompaniesIfStaleAsync(ct);

            // DB-backed now instead of calling TWStockLib's isin.twse.com.tw lookup on every
            // keystroke — that endpoint is slow and has real outage/maintenance windows, and a
            // failed call used to surface as "查無股票" for every symbol (see
            // RefreshTwListedCompaniesIfStaleAsync's fail-soft comment for how that's avoided now).
            var kw = keyword.ToUpperInvariant();
            var companies = await db.TwListedCompanies.ToListAsync(ct);
            return companies
                .Where(s => s.Symbol.StartsWith(kw, StringComparison.OrdinalIgnoreCase) || s.Name.Contains(keyword))
                .OrderBy(s => s.Symbol.StartsWith(kw, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(s => s.Symbol, StringComparer.Ordinal)
                .Take(15)
                .Select(s => new StockSearchItem { Symbol = s.Symbol, Name = s.Name, Market = "TW" })
                .ToList();
        }

        /// <summary>Refreshes the whole TwListedCompanies table from TWSE when the newest row is
        /// more than 24h old (or the table is empty). A successful fetch overwrites every row —
        /// TWSE's own isin.twse.com.tw lookup already returns the whole table per call, so there's
        /// nothing to diff. A failed fetch (outage/maintenance window) just leaves the existing
        /// rows in place rather than clearing them — search falls back to a slightly stale list
        /// instead of returning nothing.</summary>
        private async Task RefreshTwListedCompaniesIfStaleAsync(CancellationToken ct)
        {
            var newestRefresh = await db.TwListedCompanies.MaxAsync(c => (DateTime?)c.LastRefreshedAt, ct);
            var isStale = newestRefresh == null || newestRefresh < DateTime.UtcNow - TwListedCompanyRefreshInterval;
            if (!isStale) return;

            try
            {
                var allResult = await stockService.GetAllStockListAsync(ct: ct);
                if (!allResult.IsSuccess)
                {
                    logger.LogWarning(
                        "TW listed-company refresh failed: {ErrorCode} - {ErrorMessage}, serving existing DB rows",
                        allResult.ErrorCode, allResult.ErrorMessage);
                    return;
                }

                var now = DateTime.UtcNow;
                var fresh = allResult.Value!.Values
                    .Select(s => new TwListedCompany { Symbol = s.Symbol, Name = s.Name, LastRefreshedAt = now })
                    .ToList();

                // Plain load-then-RemoveRange rather than ExecuteDeleteAsync: keeps behavior
                // identical across the in-memory provider (used by tests) and SQL Server (prod) —
                // the table is only ~2000 rows, so loading it first to delete is not a real cost.
                var existing = await db.TwListedCompanies.ToListAsync(ct);
                db.TwListedCompanies.RemoveRange(existing);
                await db.TwListedCompanies.AddRangeAsync(fresh, ct);
                await db.SaveChangesAsync(ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "TW listed-company refresh threw, serving existing DB rows");
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
        /// Per-ex-date TWD dividends earned by the COST-TRACKED stock/fund positions only, for the
        /// window [startDate, today]. Deliberately not <see cref="ComputeDividendsAsync"/>: that one
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
                    var fxMul = currency == "USD" ? (snap.UsdTwdRate > 0 ? snap.UsdTwdRate : 32m) : 1m;

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
            var today = Today;

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
            Dictionary<string, DTO.Finance.MarketData> stockMap, Dictionary<string, FundNavHistory> fundMap,
            // When true: REAL_ESTATE and LIABILITY contribute nothing (skipped entirely rather
            // than added/subtracted) — used for the day-change %'s denominator, which the user
            // wants relative to gross liquid assets only, not diluted by real estate or netted
            // against debt. Full net worth (the default) still needs both for its own callers.
            bool liquidGrossOnly = false)
        {
            var dt = date.ToDateTime(TimeOnly.MinValue);
            var dayKey = date.ToString("yyyy-MM-dd");
            decimal total = 0;

            foreach (var p in positions)
            {
                if (p.AssetType == AssetType.REAL_ESTATE)
                {
                    if (!liquidGrossOnly) total += p.Quantity * (p.Currency == "USD" ? fx : 1m);
                    continue;
                }
                if (p.AssetType == AssetType.CASH)
                {
                    total += p.Quantity * (p.Currency == "USD" ? fx : 1m);
                    continue;
                }
                if (p.AssetType == AssetType.LIABILITY)
                {
                    if (!liquidGrossOnly) total -= p.Quantity * (p.Currency == "USD" ? fx : 1m);
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
        ///
        /// <paramref name="deep"/> controls how much history is actually fetched from source when
        /// the symbol isn't already cached/persisted: pass false (latest-only) for callers that
        /// just need the current price (summary valuation, day-change, stock lookup) — a
        /// never-before-seen symbol then only costs a shallow fetch (~10 days for TW) instead of
        /// the full ~6-month window. Pass true for anything that consumes more than the single most
        /// recent price (price-history chart, net-worth chart, AI technical summary).
        /// </summary>
        private async Task<DTO.Finance.MarketData?> FetchStockDataAsync(string symbol, bool deep, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(symbol)) return null;

            // Separate cache keys for deep vs latest-only — a shallow result must never be served
            // back to a caller that needs the full series (see PersistentTwHistoricalProvider's own
            // matching split for why: it's not just a perf nicety, a stale shallow cache hit would
            // silently starve a chart/indicator caller of history it thinks it has).
            var key = STOCK_CACHE_PREFIX + (deep ? "deep_" : "latest_") + symbol;
            if (cache.TryGetValue(key, out DTO.Finance.MarketData? cached) && cached != null)
                return cached;

            try
            {
                var region = providerFactory.DetectRegion(symbol);
                var provider = providerFactory.GetMarketDataProvider(region);
                // TW's provider chain already includes today's-price overlay as its outermost layer
                // (TwTodayOverlayMarketDataProvider, see FinanceServiceExtensions) — every caller of
                // IMarketDataProvider gets it, not just Portfolio, so nothing extra happens here.
                var data = deep ? await provider.FetchAsync(symbol, ct) : await provider.FetchLatestAsync(symbol, ct);

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

        /// <summary>Fetch every distinct stock symbol in parallel; failed symbols are omitted.</summary>
        private async Task<Dictionary<string, DTO.Finance.MarketData>> FetchStocksAsync(
            IEnumerable<string> symbols, bool deep, CancellationToken ct)
        {
            var distinct = symbols.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().ToList();
            var results = await Task.WhenAll(
                distinct.Select(async s => (symbol: s, data: await FetchStockDataAsync(s, deep, ct))));
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
