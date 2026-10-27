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

        public async Task<ServiceResult<bool>> SaveHoldingsAsync(string userId, SaveHoldingsRequest req, CancellationToken ct = default)
        {
            if (HoldingsLimits.Problem(req) is { } problem)
                return ServiceResult.Fail(ErrorCodes.BadRequest, problem);

            var effectiveDate = req.EffectiveDate ?? Today;

            // Replace any existing snapshot for that date (cascade deletes its positions)
            var existing = await db.HoldingSnapshots
                .FirstOrDefaultAsync(s => s.UserId == userId && s.EffectiveDate == effectiveDate, ct);
            if (existing != null)
                db.HoldingSnapshots.Remove(existing);
            else if (await db.HoldingSnapshots.CountAsync(s => s.UserId == userId, ct) >= HoldingsLimits.MaxSnapshots)
                return ServiceResult.Fail(ErrorCodes.BadRequest, HoldingsLimits.TooManySnapshotsMessage);

            var usdTwdRate = await priceLoader.FetchUsdTwdRateAsync(ct);

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
            return ServiceResult.Ok(true);
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

            // A display setting the client sends, so clamped rather than rejected: 0 would be free
            // trades, and above 1 is no discount at all.
            feeDiscountRate = Math.Clamp(feeDiscountRate, 0.01m, 1m);

            // The previous trading day's holdings are priced too, for the day change: a position sold
            // since still counts on that side.
            var previousTradingDay = tradingCalendar.PreviousTradingDay(Today);
            var previous = allSnapshots.LastOrDefault(s => s.EffectiveDate <= previousTradingDay)
                ?? allSnapshots.FirstOrDefault();
            var allPositions = previous != null
                ? snapshot.Positions.Concat(previous.Positions).ToList()
                : snapshot.Positions;

            // The live rate: the one saved with a snapshot only reconstructs that snapshot's own day.
            var fx = await Timed("fx", () => priceLoader.FetchUsdTwdRateAsync(ct));
            // Dividend histories don't depend on prices, so they load while the prices do.
            var dividendTask = Timed("dividends", () => FetchDividendRecordsAsync(allSnapshots, ct),
                d => $"{d.Count} holdings");
            var prices = await LoadPricesAsync(allPositions, fx, ct);

            var valued = snapshot.Positions.Select(p => PositionValuation.Value(p, prices, feeDiscountRate)).ToList();
            var totals = PortfolioTotals.Of(valued);
            var positions = valued.Select(v => v.Row).ToList();

            // 含息: each dividend counts against the quantity held on its ex-date, over the whole timeline.
            var (totalDividendsTWD, dividendsByPosition, dividendFlows) =
                ComputeDividends(allSnapshots, fx, prices.Funds, await dividendTask);
            foreach (var p in positions)
            {
                if (dividendsByPosition.TryGetValue((p.AssetType, p.Symbol, p.Market), out var dividends))
                    p.DividendsTWD = dividends;
            }
            await AttachTagsAsync(positions, ct);

            // XIRR is 含息 too: dividends are cash the user received.
            var annualizedReturnPct = ComputeAnnualizedReturn(
                allSnapshots, fx, totals.CostBasis + totals.UnrealizedPnl, dividendFlows, Today);
            var dayChange = DayChange(snapshot, previous, previousTradingDay, allPositions, prices, totals);

            timing.Add("total", total.Elapsed);
            return BuildSummary(snapshot, fx, totals, totalDividendsTWD, annualizedReturnPct, dayChange, positions);
        }

        /// <summary>
        /// The prices of <paramref name="positions"/> (stocks and fund NAVs, fetched together) and the rates
        /// they convert at: the 集保 rates of the fund currencies held, and the 國泰世華 rates for the days the
        /// US stock quotes belong to (in practice one day, prices coming from one fetch). See
        /// <see cref="PositionFxResolver"/> for which position converts at which.
        /// </summary>
        private async Task<PortfolioPrices> LoadPricesAsync(
            ICollection<HoldingPosition> positions, decimal midUsdTwd, CancellationToken ct)
        {
            var stockTask = Timed("stocks", () => priceLoader.FetchStocksAsync(
                positions.SymbolsPricedBy(PriceSource.StockQuote), deep: false, ct),
                m => $"{m.Count} symbols");
            var fundTask = Timed("funds", () => priceLoader.FetchFundsAsync(
                positions.SymbolsPricedBy(PriceSource.FundNav), ct),
                m => $"{m.Count} funds");
            await Task.WhenAll(stockTask, fundTask);
            var stocks = stockTask.Result;
            var funds = fundTask.Result;

            var tdccCurrencies = funds.Values
                .Select(n => n.Currency)
                .Where(c => !string.IsNullOrWhiteSpace(c) && c != "TWD")
                .Distinct()
                .ToList();
            var cathayKeys = positions
                .Where(p => p.Currency == "USD"
                    && AssetValuationPolicy.For(p.AssetType).PublishedFx == PublishedFxSource.CathayOnQuoteDay)
                .Select(p => AssetValuationPolicy.For(p.AssetType).Latest(p, stocks, funds)?.Day)
                .Where(d => d.HasValue)
                .Select(d => (Currency: "USD", Date: d!.Value))
                .Distinct()
                .ToList();

            var tdccTask = Task.WhenAll(tdccCurrencies.Select(async currency =>
                (Currency: currency, Rate: await tdccFx.GetBuyRateAsync(currency, Today, ct))));
            var cathayTask = Task.WhenAll(cathayKeys.Select(async key =>
                (Key: key, Rate: await cathayFx.GetBuyRateAsync(key.Currency, key.Date, ct))));
            await Timed("fxrates", () => Task.WhenAll(tdccTask, cathayTask));

            var tdccRates = tdccTask.Result.Where(r => r.Rate is > 0).ToDictionary(r => r.Currency, r => r.Rate!.Value);
            var cathayRates = cathayTask.Result.Where(r => r.Rate is > 0).ToDictionary(r => r.Key, r => r.Rate!.Value);
            return new PortfolioPrices(stocks, funds,
                new PositionFxResolver(midUsdTwd, tdccRates, cathayRates, stocks, funds));
        }

        /// <summary>
        /// The AI theme tags already stored for the symbols held. Never classifies a new symbol (that is an
        /// AI call, and the summary is the hot path): an unclassified one shows no tags until a 產業/子類別
        /// chart classifies it.
        /// </summary>
        private async Task AttachTagsAsync(List<PositionDto> positions, CancellationToken ct)
        {
            var symbols = positions
                .Where(p => !string.IsNullOrWhiteSpace(p.Symbol))
                .Select(p => p.Symbol)
                .Distinct()
                .ToList();
            if (symbols.Count == 0) return;

            var rows = await Timed("tags", () => db.StockClassifications
                .Where(s => symbols.Contains(s.Symbol) && s.Tags != null)
                .Select(s => new { s.Symbol, s.Tags })
                .ToListAsync(ct));
            var tags = rows.ToDictionary(r => r.Symbol, r => StockCategoryService.SplitTags(r.Tags));
            foreach (var p in positions)
            {
                if (tags.TryGetValue(p.Symbol, out var symbolTags))
                    p.Tags = symbolTags.ToList();
            }
        }

        /// <summary>
        /// The day's change in liquid net worth (流動淨資產: liquid assets net of debt, no real estate)
        /// against the previous trading day's holdings at that day's closes, and that as a % of the previous
        /// day's liquid gross assets — the basis of the other headline percentages. Null until there is
        /// something to compare: holdings that changed, or a price newer than the previous trading day's
        /// close. Before today's prices land both days read alike, and 0 would be misleading.
        /// </summary>
        private static (decimal? TWD, decimal? Pct) DayChange(
            HoldingSnapshot today, HoldingSnapshot? previous, DateOnly previousTradingDay,
            ICollection<HoldingPosition> allPositions, PortfolioPrices prices, PortfolioTotals totals)
        {
            if (previous == null) return (null, null);
            var holdingsChanged = today.Id != previous.Id && !ArePositionsEqual(today.Positions, previous.Positions);
            if (!holdingsChanged && !HasNewerPriceThan(allPositions, previousTradingDay, prices.Stocks, prices.Funds))
                return (null, null);

            // Both sides convert at today's rates (prices.Fx), not at the rate saved with the earlier
            // snapshot: that is a mid-market rate from whenever it was saved, and mixing the two put the gap
            // between unrelated rates into every day's figure. Holding FX constant leaves what this figure
            // is for, price moves and trades.
            var asOfPrices = AsOfPriceBook.From(prices.Stocks, prices.Funds);
            var previousLiquidNetWorth = ValueAsOfDate(
                previous.Positions, previousTradingDay, prices.Fx.MidUsdTwd, asOfPrices,
                includeRealEstate: false, includeLiability: true, fxFor: prices.Fx.RateFor);
            if (!previousLiquidNetWorth.HasValue) return (null, null);

            var change = totals.LiquidNetWorth - previousLiquidNetWorth.Value;
            var previousLiquidGross = ValueAsOfDate(
                previous.Positions, previousTradingDay, prices.Fx.MidUsdTwd, asOfPrices,
                includeRealEstate: false, includeLiability: false, fxFor: prices.Fx.RateFor);
            return (change, previousLiquidGross is > 0 ? Math.Round(change / previousLiquidGross.Value * 100, 2) : null);
        }

        private static PortfolioSummaryDto BuildSummary(
            HoldingSnapshot snapshot, decimal fx, PortfolioTotals totals, decimal totalDividendsTWD,
            decimal? annualizedReturnPct, (decimal? TWD, decimal? Pct) dayChange, List<PositionDto> positions)
        {
            var totalReturnPnlTWD = totals.UnrealizedPnl + totalDividendsTWD;
            return new PortfolioSummaryDto
            {
                EffectiveDate = snapshot.EffectiveDate,
                UsdTwdRate = fx,
                TotalNetWorthTWD = totals.NetWorth,
                TwStockTWD = totals.TwStock,
                UsStockTWD = totals.UsStock,
                CashTWD = totals.Cash,
                FundTWD = totals.Fund,
                RealEstateTWD = totals.RealEstate,
                // Shown as a positive amount owed.
                LiabilityTWD = -totals.Liability,
                LiquidGrossAssetsTWD = totals.LiquidGrossAssets,
                CostBasisTWD = totals.CostBasis,
                UnrealizedPnlTWD = totals.UnrealizedPnl,
                UnrealizedPnlPct = totals.PctOfLiquidGrossAssets(totals.UnrealizedPnl),
                EstimatedSellCostTWD = totals.EstimatedSellCost,
                TotalDividendsTWD = totalDividendsTWD,
                TotalReturnPnlTWD = totalReturnPnlTWD,
                TotalReturnPct = totals.PctOfLiquidGrossAssets(totalReturnPnlTWD),
                AnnualizedReturnPct = annualizedReturnPct,
                MarketExposureTWD = totals.Exposure,
                ExposureOfLiquidAssetsPct = totals.ExposureOfLiquidAssetsPct,
                LiquidExposureMultiple = totals.LiquidExposureMultiple,
                DrawdownBufferPct = totals.DrawdownBufferPct,
                DayChangeTWD = dayChange.TWD,
                DayChangePct = dayChange.Pct,
                Positions = positions.OrderByDescending(p => p.MarketValueTWD).ToList(),
            };
        }

        /// <summary>
        /// Manual retry for a position stuck showing "報價失敗" — busts both this request-scoped
        /// in-memory cache entry and the provider's own persisted sticky state (e.g. a TW symbol
        /// wrongly resolved to the other market once and never re-checked since, see
        /// <see cref="Market.PersistentTwHistoricalProvider.InvalidateAsync"/>) for
        /// just this one symbol, then returns a freshly recomputed summary. Only STOCK positions
        /// route through <see cref="Market.IMarketDataProviderFactory"/> at all (FUND pricing goes through
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
