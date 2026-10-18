using JackyAIApp.Server.Data;
using JackyAIApp.Server.Data.Models.SQL;
using JackyAIApp.Server.DTO.Finance;
using JackyAIApp.Server.Services.ExternalApi;
using JackyAIApp.Server.Services.Finance.DataProviders;
using JackyAIApp.Server.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TWStockLib.Models;

namespace JackyAIApp.Server.Tests.Services.Finance
{
    /// <summary>
    /// The background backfill fetches older TW history a month at a time. These pin the order it
    /// goes in (newest month first across all stocks), what it writes down as covered (only months
    /// that really arrived), and what it does when the exchange answers badly — an empty month for a
    /// stock that was trading must never be written down as covered.
    /// </summary>
    public class TwHistoryBackfillServiceTests : IDisposable
    {
        private static readonly DateOnly Target = new(2026, 3, 1);

        private readonly DbContextOptions<AzureSQLDBContext> _options;
        private readonly AzureSQLDBContext _db;
        private readonly Mock<ITWStockLibHistoricalProvider> _history = new();
        private readonly ManualTimeProvider _time = new();
        private readonly TwHistoryBackfillService _service;
        private readonly List<(string Symbol, DateOnly Month, GatewayPriority Priority)> _fetched = [];

        /// <summary>What the "exchange" answers for a (symbol, month); by default two days of data.</summary>
        private Func<string, DateOnly, TwMonthBars> _answer = (_, month) => Bars(month);

        public TwHistoryBackfillServiceTests()
        {
            _options = new DbContextOptionsBuilder<AzureSQLDBContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;
            _db = new AzureSQLDBContext(_options);

            _history
                .Setup(h => h.FetchMonthAsync(It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<MarketType>(),
                    It.IsAny<GatewayPriority>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string symbol, DateOnly month, MarketType _, GatewayPriority priority, CancellationToken _) =>
                {
                    _fetched.Add((symbol, month, priority));
                    return _answer(symbol, month);
                });
            _history.Setup(h => h.DetermineMarketType(It.IsAny<string>())).ReturnsAsync(MarketType.TSE);

            var services = new ServiceCollection();
            services.AddSingleton(_history.Object);
            _service = new TwHistoryBackfillService(
                services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
                new TestDbContextFactory(_options),
                NullLogger<TwHistoryBackfillService>.Instance,
                _time);
        }

        public void Dispose() => _db.Dispose();

        private static TwMonthBars Bars(DateOnly month) => new(true,
        [
            new DailyPrice { Date = month.AddDays(1).ToDateTime(TimeOnly.MinValue), Open = 10, High = 11, Low = 9, Close = 10, Volume = 1000 },
            new DailyPrice { Date = month.AddDays(2).ToDateTime(TimeOnly.MinValue), Open = 10, High = 11, Low = 9, Close = 10, Volume = 1000 },
        ]);

        private static readonly TwMonthBars Empty = new(true, []);

        private async Task SeedAsync(string symbol, DateOnly coveredFrom, string market = "TSE")
        {
            _db.StockSymbolInfos.Add(new StockSymbolInfo
            {
                Symbol = symbol, Market = "TW", TwMarketType = market, HistoryCoveredFrom = coveredFrom,
            });
            await _db.SaveChangesAsync();
        }

        private async Task<DateOnly?> CoveredFromInDbAsync(string symbol)
        {
            await using var db = new AzureSQLDBContext(_options);
            return (await db.StockSymbolInfos.AsNoTracking().SingleAsync(i => i.Symbol == symbol)).HistoryCoveredFrom;
        }

        private async Task<List<TwHistoryBackfillService.StepResult>> DrainAsync()
        {
            var steps = new List<TwHistoryBackfillService.StepResult>();
            for (var i = 0; i < 200; i++)
            {
                var step = await _service.StepAsync(CancellationToken.None);
                if (step == TwHistoryBackfillService.StepResult.Idle) return steps;
                steps.Add(step);
            }
            throw new Exception("The backfill did not settle.");
        }

        private static HistoryWant Want(string symbol, DateOnly needFrom, DateOnly? heldFrom = null) =>
            new(symbol, needFrom, heldFrom);

        [Fact]
        public async Task AStockAlreadyStoredBackToWhatIsNeeded_IsCompleteAndNothingIsQueued()
        {
            await SeedAsync("2330", new DateOnly(2026, 1, 1));

            var status = await _service.RequestAsync([Want("2330", Target)], Target, CancellationToken.None);

            Assert.Equal(BackfillState.Complete, status.State);
            Assert.Equal(0, _service.QueuedSymbols);
        }

        [Fact]
        public async Task AStockStoredOnlyBackToTheDeepWindow_IsQueuedAndReportedRunning()
        {
            await SeedAsync("2330", new DateOnly(2026, 6, 1));

            var status = await _service.RequestAsync([Want("2330", Target)], Target, CancellationToken.None);

            Assert.Equal(BackfillState.Running, status.State);
            Assert.Equal(new DateOnly(2026, 6, 1), status.CoveredFrom);
            Assert.Equal(Target, status.TargetFrom);
            Assert.Equal(1, _service.QueuedSymbols);
        }

        [Fact]
        public async Task AStockWhoseDeepFetchNeverRan_IsLeftAlone()
        {
            // No recent window to extend: nothing here knows where the stored history starts.
            _db.StockSymbolInfos.Add(new StockSymbolInfo { Symbol = "2330", Market = "TW", TwMarketType = "TSE" });
            await _db.SaveChangesAsync();

            var status = await _service.RequestAsync([Want("2330", Target)], Target, CancellationToken.None);

            Assert.Equal(BackfillState.Complete, status.State);
            Assert.Equal(0, _service.QueuedSymbols);
        }

        [Fact]
        public void BeforeTheBackfillHasRun_TheCoverageIsWhatTheDeepFetchWindowHeld()
        {
            // Deep fetch on 2026-10-18 asked for six months back: from 2026-04-18. April is only
            // partly in it, so the first whole month is May.
            var info = new StockSymbolInfo { Symbol = "2330", Market = "TW", DeepFetchedAt = new DateTime(2026, 10, 18, 3, 0, 0, DateTimeKind.Utc) };
            Assert.Equal(new DateOnly(2026, 5, 1), TwHistoryBackfillService.CoveredFromOf(info));

            // Exactly on a month start, that month is whole.
            info.DeepFetchedAt = new DateTime(2026, 10, 1, 3, 0, 0, DateTimeKind.Utc);
            Assert.Equal(new DateOnly(2026, 4, 1), TwHistoryBackfillService.CoveredFromOf(info));

            // Once the backfill has run, its own record wins.
            info.HistoryCoveredFrom = new DateOnly(2025, 12, 1);
            Assert.Equal(new DateOnly(2025, 12, 1), TwHistoryBackfillService.CoveredFromOf(info));

            Assert.Null(TwHistoryBackfillService.CoveredFromOf(new StockSymbolInfo { Symbol = "x", Market = "TW" }));
        }

        [Fact]
        public async Task EachMonthIsStoredAsItArrives_AndTheCoverageMovesBackOneMonthAtATime()
        {
            await SeedAsync("2330", new DateOnly(2026, 6, 1));
            await _service.RequestAsync([Want("2330", Target)], Target, CancellationToken.None);

            Assert.Equal(TwHistoryBackfillService.StepResult.Progressed, await _service.StepAsync(CancellationToken.None));
            Assert.Equal(new DateOnly(2026, 5, 1), await CoveredFromInDbAsync("2330"));
            Assert.Equal(2, await _db.DailyStockPrices.CountAsync(p => p.Symbol == "2330"));

            await DrainAsync();

            Assert.Equal(new DateOnly(2026, 3, 1), await CoveredFromInDbAsync("2330"));
            Assert.Equal(
                [new DateOnly(2026, 5, 1), new DateOnly(2026, 4, 1), new DateOnly(2026, 3, 1)],
                _fetched.Select(f => f.Month));
            Assert.Equal(6, await _db.DailyStockPrices.CountAsync(p => p.Symbol == "2330"));
            Assert.Equal(0, _service.QueuedSymbols);

            var again = await _service.RequestAsync([Want("2330", Target)], Target, CancellationToken.None);
            Assert.Equal(BackfillState.Complete, again.State);
        }

        [Fact]
        public async Task MonthsGoNewestFirstAcrossStocks_NotOneStockAllTheWayBack()
        {
            // A chart shows only the days every stock has a price for, so it grows leftwards as a
            // month is finished for all of them.
            await SeedAsync("AAA1", new DateOnly(2026, 5, 1));
            await SeedAsync("BBB2", new DateOnly(2026, 5, 1));
            await _service.RequestAsync([Want("AAA1", Target), Want("BBB2", Target)], Target, CancellationToken.None);

            await DrainAsync();

            Assert.Equal(
                [("AAA1", new DateOnly(2026, 4, 1)), ("BBB2", new DateOnly(2026, 4, 1)),
                 ("AAA1", new DateOnly(2026, 3, 1)), ("BBB2", new DateOnly(2026, 3, 1))],
                _fetched.Select(f => (f.Symbol, f.Month)));
        }

        [Fact]
        public async Task EveryMonthIsRequestedAsBackground_SoAPersonWaitingGoesFirst()
        {
            await SeedAsync("2330", new DateOnly(2026, 4, 1));
            await _service.RequestAsync([Want("2330", Target)], Target, CancellationToken.None);

            await DrainAsync();

            Assert.NotEmpty(_fetched);
            Assert.All(_fetched, f => Assert.Equal(GatewayPriority.Background, f.Priority));
        }

        [Fact]
        public async Task AMonthThatDidNotArrive_IsNotWrittenDownAsCovered()
        {
            await SeedAsync("2330", new DateOnly(2026, 5, 1));
            _answer = (_, _) => TwMonthBars.Failed;
            await _service.RequestAsync([Want("2330", Target)], Target, CancellationToken.None);

            var step = await _service.StepAsync(CancellationToken.None);

            Assert.Equal(TwHistoryBackfillService.StepResult.Failed, step);
            Assert.Equal(new DateOnly(2026, 5, 1), await CoveredFromInDbAsync("2330"));
            Assert.Equal(1, _service.QueuedSymbols);
        }

        [Fact]
        public async Task AMonthThatFailsOnceIsRetriedAndTheBackfillCarriesOn()
        {
            await SeedAsync("2330", new DateOnly(2026, 5, 1));
            var attempts = 0;
            _answer = (_, month) => ++attempts == 1 ? TwMonthBars.Failed : Bars(month);
            await _service.RequestAsync([Want("2330", Target)], Target, CancellationToken.None);

            var steps = await DrainAsync();

            Assert.Equal(TwHistoryBackfillService.StepResult.Failed, steps[0]);
            Assert.Equal(new DateOnly(2026, 3, 1), await CoveredFromInDbAsync("2330"));
        }

        [Fact]
        public async Task AfterRepeatedFailures_TheStockIsGivenUpOnForAWhile_ThenQueuedAgain()
        {
            await SeedAsync("2330", new DateOnly(2026, 5, 1));
            _answer = (_, _) => TwMonthBars.Failed;
            await _service.RequestAsync([Want("2330", Target)], Target, CancellationToken.None);

            for (var i = 0; i < TwHistoryBackfillService.MaxFailures; i++)
                await _service.StepAsync(CancellationToken.None);

            Assert.Equal(0, _service.QueuedSymbols);
            Assert.Equal(TwHistoryBackfillService.StepResult.Idle, await _service.StepAsync(CancellationToken.None));

            // Asking again straight away does not start another round of retries...
            var soon = await _service.RequestAsync([Want("2330", Target)], Target, CancellationToken.None);
            Assert.Equal(BackfillState.Stalled, soon.State);
            Assert.Equal(0, _service.QueuedSymbols);

            // ...but after the pause it does.
            _time.Advance(TwHistoryBackfillService.GiveUpFor + TimeSpan.FromSeconds(1));
            var later = await _service.RequestAsync([Want("2330", Target)], Target, CancellationToken.None);
            Assert.Equal(BackfillState.Running, later.State);
            Assert.Equal(1, _service.QueuedSymbols);
        }

        [Fact]
        public async Task AnEmptyMonthForAStockThatWasHeld_IsAFailure_NotCoverage()
        {
            // The exchange answers "no data" for plenty of reasons other than there being none. A
            // stock held in March was trading in March, so an empty March is a bad answer.
            await SeedAsync("2330", new DateOnly(2026, 5, 1));
            _answer = (_, _) => Empty;
            await _service.RequestAsync([Want("2330", Target, heldFrom: new DateOnly(2026, 3, 15))], Target, CancellationToken.None);

            var step = await _service.StepAsync(CancellationToken.None);

            Assert.Equal(TwHistoryBackfillService.StepResult.Failed, step);
            Assert.Equal(new DateOnly(2026, 5, 1), await CoveredFromInDbAsync("2330"));
        }

        [Fact]
        public async Task AnEmptyMonthBeforeAHeldStocksFirstDay_MeansItWasNotListedYet_SoTheRestIsDone()
        {
            // Held from mid-April; March (the margin month) has nothing, and neither will February.
            await SeedAsync("2330", new DateOnly(2026, 5, 1));
            _answer = (_, month) => month < new DateOnly(2026, 4, 1) ? Empty : Bars(month);
            var need = new DateOnly(2025, 12, 1);
            await _service.RequestAsync([Want("2330", need, heldFrom: new DateOnly(2026, 4, 15))], need, CancellationToken.None);

            await DrainAsync();

            Assert.Equal(
                [new DateOnly(2026, 4, 1), new DateOnly(2026, 3, 1)],
                _fetched.Select(f => f.Month));
            Assert.Equal(need, await CoveredFromInDbAsync("2330"));
            Assert.Equal(0, _service.QueuedSymbols);
        }

        [Fact]
        public async Task ForABenchmarkWithNothingKnownAboutIt_TwoEmptyMonthsInARowEndTheSearch()
        {
            await SeedAsync("0050", new DateOnly(2026, 5, 1));
            _answer = (_, month) => month < new DateOnly(2026, 4, 1) ? Empty : Bars(month);
            var need = new DateOnly(2025, 1, 1);
            await _service.RequestAsync([Want("0050", need)], need, CancellationToken.None);

            await DrainAsync();

            // April has data; March and February are empty, and that is taken as enough.
            Assert.Equal(
                [new DateOnly(2026, 4, 1), new DateOnly(2026, 3, 1), new DateOnly(2026, 2, 1)],
                _fetched.Select(f => f.Month));
            Assert.Equal(need, await CoveredFromInDbAsync("0050"));
        }

        [Fact]
        public async Task AMonthWithDataBetweenTwoEmptyOnes_RestartsTheEmptyCount()
        {
            await SeedAsync("0050", new DateOnly(2026, 6, 1));
            // May empty, April data, March empty, February data, January onward empty.
            _answer = (_, month) => month.Month is 4 or 2 ? Bars(month) : Empty;
            var need = new DateOnly(2025, 6, 1);
            await _service.RequestAsync([Want("0050", need)], need, CancellationToken.None);

            await DrainAsync();

            Assert.Equal(
                new[] { 5, 4, 3, 2, 1, 12 },
                _fetched.Select(f => f.Month.Month));
        }

        [Fact]
        public async Task AskingAgainForMoreHistory_ExtendsTheStockAlreadyQueued()
        {
            await SeedAsync("2330", new DateOnly(2026, 5, 1));
            await _service.RequestAsync([Want("2330", new DateOnly(2026, 4, 1))], new DateOnly(2026, 4, 1), CancellationToken.None);

            await _service.RequestAsync([Want("2330", Target)], Target, CancellationToken.None);
            await DrainAsync();

            Assert.Equal(Target, await CoveredFromInDbAsync("2330"));
            Assert.Equal([new DateOnly(2026, 4, 1), new DateOnly(2026, 3, 1)], _fetched.Select(f => f.Month));
        }

        [Fact]
        public async Task WhileAnyStockIsStillFetching_TheStatusReportsTheMostRestrictiveCoverage()
        {
            await SeedAsync("AAA1", new DateOnly(2026, 6, 1));
            await SeedAsync("BBB2", new DateOnly(2026, 4, 1));
            await SeedAsync("CCC3", new DateOnly(2026, 1, 1));   // already enough

            var status = await _service.RequestAsync(
                [Want("AAA1", Target), Want("BBB2", Target), Want("CCC3", Target)], Target, CancellationToken.None);

            // The chart can only reach back as far as the stock that has got the least far.
            Assert.Equal(BackfillState.Running, status.State);
            Assert.Equal(new DateOnly(2026, 6, 1), status.CoveredFrom);
        }

        [Fact]
        public async Task TheQueueHoldsAtMostAFixedNumberOfStocks()
        {
            var wants = new List<HistoryWant>();
            for (var i = 0; i < TwHistoryBackfillService.MaxQueuedSymbols + 5; i++)
            {
                var symbol = $"{1000 + i}";
                await SeedAsync(symbol, new DateOnly(2026, 6, 1));
                wants.Add(Want(symbol, Target));
            }

            var status = await _service.RequestAsync(wants, Target, CancellationToken.None);

            Assert.Equal(TwHistoryBackfillService.MaxQueuedSymbols, _service.QueuedSymbols);
            Assert.Equal(BackfillState.Running, status.State);
        }

        [Fact]
        public async Task AMarketThatCannotBeResolved_FailsTheStockInsteadOfGuessing()
        {
            _db.StockSymbolInfos.Add(new StockSymbolInfo
            {
                Symbol = "9999", Market = "TW", TwMarketType = null, HistoryCoveredFrom = new DateOnly(2026, 5, 1),
            });
            await _db.SaveChangesAsync();
            _history.Setup(h => h.DetermineMarketType("9999")).ReturnsAsync((MarketType?)null);
            await _service.RequestAsync([Want("9999", Target)], Target, CancellationToken.None);

            var step = await _service.StepAsync(CancellationToken.None);

            Assert.Equal(TwHistoryBackfillService.StepResult.Failed, step);
            Assert.Empty(_fetched);
        }

        [Fact]
        public async Task TheWorkerLoop_PicksUpRequestsAsTheyComeAndSleepsBetweenThem()
        {
            await SeedAsync("2330", new DateOnly(2026, 5, 1));
            await SeedAsync("2317", new DateOnly(2026, 5, 1));
            await _service.StartAsync(CancellationToken.None);
            try
            {
                await _service.RequestAsync([Want("2330", Target)], Target, CancellationToken.None);
                await WaitForAsync(async () => await CoveredFromInDbAsync("2330") == Target);

                // Idle by now; a later request has to wake it again.
                await _service.RequestAsync([Want("2317", Target)], Target, CancellationToken.None);
                await WaitForAsync(async () => await CoveredFromInDbAsync("2317") == Target);
            }
            finally
            {
                await _service.StopAsync(CancellationToken.None);
            }
        }

        private static async Task WaitForAsync(Func<Task<bool>> condition)
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (!await condition())
            {
                if (DateTime.UtcNow > deadline) throw new TimeoutException("The worker did not get there.");
                await Task.Delay(10);
            }
        }

        [Fact]
        public async Task ABarFromTheMonthIsNotStoredTwice_WhenTheRequestOverlapsWhatIsAlreadyThere()
        {
            await SeedAsync("2330", new DateOnly(2026, 5, 1));
            _db.DailyStockPrices.Add(new DailyStockPrice
            {
                Symbol = "2330", Market = "TW", TradeDate = new DateOnly(2026, 4, 2), Open = 1, High = 1, Low = 1, Close = 1, Volume = 1,
            });
            await _db.SaveChangesAsync();
            await _service.RequestAsync([Want("2330", new DateOnly(2026, 4, 1))], new DateOnly(2026, 4, 1), CancellationToken.None);

            await DrainAsync();

            var april = await _db.DailyStockPrices.Where(p => p.Symbol == "2330").OrderBy(p => p.TradeDate).ToListAsync();
            Assert.Equal(2, april.Count);          // 04-02 kept as it was, 04-03 added
            Assert.Equal(1, april[0].Close);
        }
    }
}
