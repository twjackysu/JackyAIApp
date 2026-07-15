using JackyAIApp.Server.Data;
using JackyAIApp.Server.Data.Models.SQL;
using JackyAIApp.Server.DTO.Finance;
using JackyAIApp.Server.Services.Finance.DataProviders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TWStockLib.Models;

namespace JackyAIApp.Server.Tests.Services.Finance
{
    /// <summary>
    /// PersistentTwHistoricalProvider exists because Azure App Service's free tier cold-starts
    /// (wiping IMemoryCache) but Azure SQL Basic (DTU model) doesn't auto-pause on idle — so
    /// persisting finalized bars to DB is what survives a cold start. These tests pin the gap-fill
    /// behavior: DB-empty fetches the full window, a partial DB only fetches the gap, an
    /// already-current DB makes no network call at all, and today's still-moving price is never
    /// persisted. See .claude/plans/portfolio-db-price-cache.md.
    /// </summary>
    public class PersistentTwHistoricalProviderTests : IDisposable
    {
        private const string Symbol = "2330";

        private readonly AzureSQLDBContext _dbContext;
        private readonly Mock<ITWStockLibHistoricalProvider> _inner;
        private readonly PersistentTwHistoricalProvider _provider;

        public PersistentTwHistoricalProviderTests()
        {
            var options = new DbContextOptionsBuilder<AzureSQLDBContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;
            // Same options/database name as the factory below hands out to the provider — the
            // in-memory provider shares the underlying store across context instances pointed at
            // the same name, so seeding/asserting here and the provider's own factory-created
            // contexts (mirroring prod's "fresh context per call" fix) see the same data.
            _dbContext = new AzureSQLDBContext(options);

            _inner = new Mock<ITWStockLibHistoricalProvider>();
            _provider = new PersistentTwHistoricalProvider(
                _inner.Object, new TestDbContextFactory(options), NullLogger<PersistentTwHistoricalProvider>.Instance);
        }

        public void Dispose() => _dbContext.Dispose();

        private class TestDbContextFactory(DbContextOptions<AzureSQLDBContext> options)
            : IDbContextFactory<AzureSQLDBContext>
        {
            public AzureSQLDBContext CreateDbContext() => new(options);

            public Task<AzureSQLDBContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
                Task.FromResult(new AzureSQLDBContext(options));
        }

        /// <summary>Most recent weekday before <paramref name="today"/> — mirrors the provider's
        /// own (intentionally simple, holiday-unaware) LastTradingDayBefore so tests agree with it
        /// regardless of which real day the suite happens to run on.</summary>
        private static DateOnly LastTradingDayBefore(DateOnly today)
        {
            var d = today.AddDays(-1);
            while (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) d = d.AddDays(-1);
            return d;
        }

        /// <summary>Mirrors the provider's own IsPastTwCloseTime/IsWeekday so a test asserting
        /// behavior around "is today's bar finalized yet" agrees with it regardless of the real
        /// wall-clock time the suite happens to run at.</summary>
        private static bool TodayIsFinalized(DateOnly today)
        {
            if (today.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) return false;
            var taipeiNow = DateTime.UtcNow.AddHours(8);
            return taipeiNow.TimeOfDay >= new TimeSpan(13, 30, 0);
        }

        [Fact]
        public async Task FetchAsync_EmptyDb_FetchesFullWindowAndPersistsFinalizedBarsOnly()
        {
            var today = DateOnly.FromDateTime(DateTime.Now);
            var lastTradingDay = LastTradingDayBefore(today);

            _inner.Setup(x => x.DetermineMarketType(Symbol)).ReturnsAsync(MarketType.TSE);
            _inner.Setup(x => x.FetchRangeAsync(
                    Symbol, It.IsAny<DateTime>(), It.IsAny<DateTime>(), MarketType.TSE, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new MarketData
                {
                    StockCode = Symbol,
                    CompanyName = "台積電",
                    HistoricalPrices = new List<DailyPrice>
                    {
                        new() { Date = lastTradingDay.ToDateTime(TimeOnly.MinValue), Close = 900m, Open = 890m, High = 905m, Low = 885m, Volume = 1000 },
                        // Today's bar — only finalized (and thus only persisted) once the TW session
                        // has closed for the day; see TodayIsFinalized.
                        new() { Date = today.ToDateTime(TimeOnly.MinValue), Close = 910m, Open = 900m, High = 915m, Low = 895m, Volume = 500 },
                    },
                });

            var result = await _provider.FetchAsync(Symbol);

            Assert.Equal("台積電", result.CompanyName);
            var persisted = await _dbContext.DailyStockPrices
                .Where(p => p.Symbol == Symbol).OrderBy(p => p.TradeDate).ToListAsync();
            if (TodayIsFinalized(today))
            {
                Assert.Equal(2, persisted.Count);
                Assert.Equal(lastTradingDay, persisted[0].TradeDate);
                Assert.Equal(900m, persisted[0].Close);
                Assert.Equal(today, persisted[1].TradeDate);
                Assert.Equal(910m, persisted[1].Close);
            }
            else
            {
                var persistedDate = Assert.Single(persisted);
                Assert.Equal(lastTradingDay, persistedDate.TradeDate);
                Assert.Equal(900m, persistedDate.Close);
            }

            var symbolInfo = await _dbContext.StockSymbolInfos.FindAsync(Symbol);
            Assert.NotNull(symbolInfo);
            Assert.Equal("TSE", symbolInfo!.TwMarketType);
            Assert.Equal("台積電", symbolInfo.Name);
        }

        [Fact]
        public async Task FetchAsync_DbHasYesterdayOnly_AfterCloseRefetchesAndPersistsTodaysBar()
        {
            // Regression test for the real prod bug: a DB already current through yesterday must
            // still trigger a gap-fetch for today once the session has closed — otherwise the
            // provider (and everything built on it) stays pinned to yesterday's close all evening,
            // even though TWSE published today's close hours ago.
            if (!TodayIsFinalized(DateOnly.FromDateTime(DateTime.Now)))
                return; // Only meaningful once past TW close; see TodayIsFinalized.

            var today = DateOnly.FromDateTime(DateTime.Now);
            var lastTradingDay = LastTradingDayBefore(today);

            _dbContext.StockSymbolInfos.Add(new StockSymbolInfo
            {
                Symbol = Symbol, Market = "TW", TwMarketType = "TSE", Name = "台積電",
            });
            _dbContext.DailyStockPrices.Add(new DailyStockPrice
            {
                Symbol = Symbol, Market = "TW", TradeDate = lastTradingDay,
                Open = 890m, High = 905m, Low = 885m, Close = 900m, Volume = 1000,
            });
            await _dbContext.SaveChangesAsync();

            _inner.Setup(x => x.FetchRangeAsync(
                    Symbol, It.IsAny<DateTime>(), It.IsAny<DateTime>(), MarketType.TSE, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new MarketData
                {
                    StockCode = Symbol,
                    CompanyName = "台積電",
                    HistoricalPrices = new List<DailyPrice>
                    {
                        new() { Date = today.ToDateTime(TimeOnly.MinValue), Close = 910m, Open = 900m, High = 915m, Low = 895m, Volume = 500 },
                    },
                });

            var result = await _provider.FetchAsync(Symbol);

            var persisted = await _dbContext.DailyStockPrices
                .Where(p => p.Symbol == Symbol).OrderBy(p => p.TradeDate).ToListAsync();
            Assert.Equal(2, persisted.Count);
            Assert.Equal(today, persisted[1].TradeDate);
            Assert.Equal(910m, persisted[1].Close);
            Assert.Equal(910m, result.HistoricalPrices[^1].Close);
        }

        [Fact]
        public async Task FetchAsync_DbAlreadyCurrent_MakesNoNetworkCallAtAll()
        {
            var today = DateOnly.FromDateTime(DateTime.Now);
            var lastTradingDay = LastTradingDayBefore(today);
            // "Already current" means through today once the session has closed — otherwise
            // seeding only lastTradingDay would (correctly) still trigger a fetch.
            var latestAvailableTradingDay = TodayIsFinalized(today) ? today : lastTradingDay;

            _dbContext.StockSymbolInfos.Add(new StockSymbolInfo
            {
                Symbol = Symbol, Market = "TW", TwMarketType = "TSE", Name = "台積電",
            });
            _dbContext.DailyStockPrices.Add(new DailyStockPrice
            {
                Symbol = Symbol, Market = "TW", TradeDate = latestAvailableTradingDay,
                Open = 890m, High = 905m, Low = 885m, Close = 900m, Volume = 1000,
            });
            await _dbContext.SaveChangesAsync();

            var result = await _provider.FetchAsync(Symbol);

            Assert.Equal("台積電", result.CompanyName);
            var price = Assert.Single(result.HistoricalPrices);
            Assert.Equal(900m, price.Close);

            _inner.Verify(x => x.DetermineMarketType(It.IsAny<string>()), Times.Never);
            _inner.Verify(x => x.FetchRangeAsync(
                It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(),
                It.IsAny<MarketType?>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task FetchAsync_PartialDb_OnlyFetchesTheGap()
        {
            var today = DateOnly.FromDateTime(DateTime.Now);
            var lastTradingDay = LastTradingDayBefore(today);
            var staleDate = lastTradingDay.AddDays(-10);

            _dbContext.StockSymbolInfos.Add(new StockSymbolInfo
            {
                Symbol = Symbol, Market = "TW", TwMarketType = "TSE", Name = "台積電",
            });
            _dbContext.DailyStockPrices.Add(new DailyStockPrice
            {
                Symbol = Symbol, Market = "TW", TradeDate = staleDate,
                Open = 800m, High = 810m, Low = 795m, Close = 805m, Volume = 900,
            });
            await _dbContext.SaveChangesAsync();

            DateTime? capturedStart = null;
            _inner.Setup(x => x.FetchRangeAsync(
                    Symbol, It.IsAny<DateTime>(), It.IsAny<DateTime>(), MarketType.TSE, It.IsAny<CancellationToken>()))
                .Callback<string, DateTime, DateTime, MarketType?, CancellationToken>((_, start, _, _, _) => capturedStart = start)
                .ReturnsAsync(new MarketData
                {
                    StockCode = Symbol,
                    CompanyName = "台積電",
                    HistoricalPrices = new List<DailyPrice>
                    {
                        new() { Date = lastTradingDay.ToDateTime(TimeOnly.MinValue), Close = 900m, Open = 890m, High = 905m, Low = 885m, Volume = 1000 },
                    },
                });

            var result = await _provider.FetchAsync(Symbol);

            // Gap-fetch requested from the day *after* the stale row, not the full 6-month window.
            Assert.Equal(staleDate.AddDays(1).ToDateTime(TimeOnly.MinValue), capturedStart);
            // Market type already persisted — no need to call DetermineMarketType again.
            _inner.Verify(x => x.DetermineMarketType(It.IsAny<string>()), Times.Never);

            var persisted = await _dbContext.DailyStockPrices
                .Where(p => p.Symbol == Symbol).OrderBy(p => p.TradeDate).ToListAsync();
            Assert.Equal(2, persisted.Count); // old stale row kept + newly gap-filled row
            Assert.Equal(staleDate, persisted[0].TradeDate);
            Assert.Equal(lastTradingDay, persisted[1].TradeDate);
            Assert.Equal(2, result.HistoricalPrices.Count);
        }

        [Fact]
        public async Task FetchAsync_GapCrossesMultipleMonths_FetchesTheWholeGapInOneCall()
        {
            // e.g. sold on 1/1, bought back around 5/1 — nobody queried this symbol in between,
            // so the DB's last row is ~4 months stale, well within the 6-month window.
            var today = DateOnly.FromDateTime(DateTime.Now);
            var lastTradingDay = LastTradingDayBefore(today);
            var staleDate = lastTradingDay.AddMonths(-4);

            _dbContext.StockSymbolInfos.Add(new StockSymbolInfo
            {
                Symbol = Symbol, Market = "TW", TwMarketType = "TSE", Name = "台積電",
            });
            _dbContext.DailyStockPrices.Add(new DailyStockPrice
            {
                Symbol = Symbol, Market = "TW", TradeDate = staleDate,
                Open = 800m, High = 810m, Low = 795m, Close = 805m, Volume = 900,
            });
            await _dbContext.SaveChangesAsync();

            DateTime? capturedStart = null;
            _inner.Setup(x => x.FetchRangeAsync(
                    Symbol, It.IsAny<DateTime>(), It.IsAny<DateTime>(), MarketType.TSE, It.IsAny<CancellationToken>()))
                .Callback<string, DateTime, DateTime, MarketType?, CancellationToken>((_, start, _, _, _) => capturedStart = start)
                .ReturnsAsync(new MarketData
                {
                    StockCode = Symbol,
                    CompanyName = "台積電",
                    HistoricalPrices = new List<DailyPrice>
                    {
                        // Stand-in for however many monthly STOCK_DAY calls TWStockLib made
                        // internally to cover the range — that pagination is its own concern, our
                        // code only ever issues one logical FetchRangeAsync for the whole gap.
                        new() { Date = staleDate.AddMonths(1).ToDateTime(TimeOnly.MinValue), Close = 830m, Open = 820m, High = 835m, Low = 815m, Volume = 700 },
                        new() { Date = staleDate.AddMonths(2).ToDateTime(TimeOnly.MinValue), Close = 860m, Open = 835m, High = 865m, Low = 830m, Volume = 800 },
                        new() { Date = lastTradingDay.ToDateTime(TimeOnly.MinValue), Close = 900m, Open = 890m, High = 905m, Low = 885m, Volume = 1000 },
                    },
                });

            var result = await _provider.FetchAsync(Symbol);

            // One single call spanning the whole multi-month gap, not one call per month.
            _inner.Verify(x => x.FetchRangeAsync(
                It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(),
                It.IsAny<MarketType?>(), It.IsAny<CancellationToken>()), Times.Once);
            Assert.Equal(staleDate.AddDays(1).ToDateTime(TimeOnly.MinValue), capturedStart);

            var persisted = await _dbContext.DailyStockPrices
                .Where(p => p.Symbol == Symbol).OrderBy(p => p.TradeDate).ToListAsync();
            Assert.Equal(4, persisted.Count); // original stale row + 3 newly gap-filled rows
            Assert.Equal(4, result.HistoricalPrices.Count);
        }

        [Fact]
        public async Task FetchAsync_LastRowOlderThanSixMonthWindow_TreatedAsEmptyAndFetchesFullWindow()
        {
            // A gap longer than the lookback window (e.g. sold 2 years ago) — the stale row falls
            // outside the 6-month window PersistentTwHistoricalProvider actually serves, so it's
            // correctly ignored rather than triggering a multi-year gap-fetch nobody needs.
            var today = DateOnly.FromDateTime(DateTime.Now);
            var lastTradingDay = LastTradingDayBefore(today);
            var ancientDate = today.AddMonths(-8);
            var expectedWindowStart = today.AddMonths(-6);

            _dbContext.DailyStockPrices.Add(new DailyStockPrice
            {
                Symbol = Symbol, Market = "TW", TradeDate = ancientDate,
                Open = 700m, High = 710m, Low = 695m, Close = 705m, Volume = 500,
            });
            await _dbContext.SaveChangesAsync();

            DateTime? capturedStart = null;
            _inner.Setup(x => x.DetermineMarketType(Symbol)).ReturnsAsync(MarketType.TSE);
            _inner.Setup(x => x.FetchRangeAsync(
                    Symbol, It.IsAny<DateTime>(), It.IsAny<DateTime>(), MarketType.TSE, It.IsAny<CancellationToken>()))
                .Callback<string, DateTime, DateTime, MarketType?, CancellationToken>((_, start, _, _, _) => capturedStart = start)
                .ReturnsAsync(new MarketData
                {
                    StockCode = Symbol,
                    CompanyName = "台積電",
                    HistoricalPrices = new List<DailyPrice>
                    {
                        new() { Date = lastTradingDay.ToDateTime(TimeOnly.MinValue), Close = 900m, Open = 890m, High = 905m, Low = 885m, Volume = 1000 },
                    },
                });

            await _provider.FetchAsync(Symbol);

            // Full-window fetch (from ~6 months ago), not "ancientDate + 1 day".
            Assert.Equal(expectedWindowStart.ToDateTime(TimeOnly.MinValue), capturedStart);
            // Market type wasn't persisted (no StockSymbolInfo seeded), so it had to resolve fresh —
            // same as any other cold/never-seen symbol.
            _inner.Verify(x => x.DetermineMarketType(Symbol), Times.Once);
        }

        [Fact]
        public async Task FetchAsync_CalledTwiceSameDay_SecondCallDoesNotDuplicateRows()
        {
            var today = DateOnly.FromDateTime(DateTime.Now);
            var lastTradingDay = LastTradingDayBefore(today);

            _inner.Setup(x => x.DetermineMarketType(Symbol)).ReturnsAsync(MarketType.TSE);
            _inner.Setup(x => x.FetchRangeAsync(
                    Symbol, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<MarketType?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new MarketData
                {
                    StockCode = Symbol,
                    CompanyName = "台積電",
                    HistoricalPrices = new List<DailyPrice>
                    {
                        new() { Date = lastTradingDay.ToDateTime(TimeOnly.MinValue), Close = 900m, Open = 890m, High = 905m, Low = 885m, Volume = 1000 },
                        // Included so the DB can actually reach "fully current" post-close on the
                        // first call — without it, every call would (correctly) keep retrying since
                        // today's close genuinely hasn't been fetched yet.
                        new() { Date = today.ToDateTime(TimeOnly.MinValue), Close = 910m, Open = 900m, High = 915m, Low = 895m, Volume = 500 },
                    },
                });

            await _provider.FetchAsync(Symbol);
            await _provider.FetchAsync(Symbol);

            var persisted = await _dbContext.DailyStockPrices.Where(p => p.Symbol == Symbol).ToListAsync();
            Assert.Equal(TodayIsFinalized(today) ? 2 : 1, persisted.Count);
            // Second call found the DB already current, so no second fetch.
            _inner.Verify(x => x.FetchRangeAsync(
                It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(),
                It.IsAny<MarketType?>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task FetchAsync_CalledConcurrentlyForManySymbols_DoesNotThrow()
        {
            // Regression test for a real prod incident: PortfolioService fans FetchAsync out
            // across every distinct symbol via Task.WhenAll within one request. When this
            // provider used a single injected (scoped) AzureSQLDBContext, concurrent branches
            // shared one context instance and EF Core threw "A second operation was started on
            // this context instance..." — surfacing as spurious "報價失敗" on whichever symbols
            // lost the race. The IDbContextFactory fix gives each FetchAsync call its own
            // context, so this must complete cleanly for many symbols fetched in parallel.
            var symbols = Enumerable.Range(1, 20).Select(i => $"S{i:0000}").ToList();
            var today = DateOnly.FromDateTime(DateTime.Now);
            var lastTradingDay = LastTradingDayBefore(today);

            _inner.Setup(x => x.DetermineMarketType(It.IsAny<string>())).ReturnsAsync(MarketType.TSE);
            _inner.Setup(x => x.FetchRangeAsync(
                    It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(),
                    It.IsAny<MarketType?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string symbol, DateTime _, DateTime _, MarketType? _, CancellationToken _) =>
                    new MarketData
                    {
                        StockCode = symbol,
                        CompanyName = "Test Co " + symbol,
                        HistoricalPrices = new List<DailyPrice>
                        {
                            new() { Date = lastTradingDay.ToDateTime(TimeOnly.MinValue), Close = 100m, Open = 99m, High = 101m, Low = 98m, Volume = 10 },
                        },
                    });

            var results = await Task.WhenAll(symbols.Select(s => _provider.FetchAsync(s)));

            Assert.Equal(symbols.Count, results.Length);
            Assert.All(results, r => Assert.Single(r.HistoricalPrices));
            Assert.All(results, r => Assert.Equal(100m, r.HistoricalPrices[0].Close));

            var persistedSymbols = await _dbContext.DailyStockPrices.Select(p => p.Symbol).Distinct().ToListAsync();
            Assert.Equal(symbols.Count, persistedSymbols.Count);
        }
    }
}
