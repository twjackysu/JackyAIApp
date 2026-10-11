using JackyAIApp.Server.Data;
using JackyAIApp.Server.Services.Portfolio.DrawdownBrake;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace JackyAIApp.Server.Tests.Services.Portfolio.DrawdownBrake
{
    public class TaiexTotalReturnRefreshServiceTests
    {
        // Taipei = UTC+8. 2026-10-08 is a Thursday.
        private static DateTimeOffset Taipei(int y, int m, int d, int h, int min = 0) =>
            new DateTimeOffset(y, m, d, h, min, 0, TimeSpan.FromHours(8)).ToUniversalTime();

        private readonly DbContextOptions<AzureSQLDBContext> _options = new DbContextOptionsBuilder<AzureSQLDBContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        private readonly FakeClient _client = new();
        private readonly TaiexTotalReturnStore _store;
        private readonly FakeTime _time = new();
        private readonly TaiexDataStatus _status = new();

        public TaiexTotalReturnRefreshServiceTests()
        {
            _store = new TaiexTotalReturnStore(new TestDbContextFactory(_options));
        }

        private TaiexTotalReturnRefreshService CreateService() =>
            new(_client, _store, _status, NullLogger<TaiexTotalReturnRefreshService>.Instance, _time);

        private Task SeedAsync(DateOnly earliest, DateOnly latest) =>
            _store.UpsertAsync([new IndexClose(earliest, 100m), new IndexClose(latest, 101m)]);

        private static DateOnly D(int y, int m, int d) => new(y, m, d);

        // ── when to fetch ───────────────────────────────────────────────────

        [Fact]
        public async Task Weekday_Before1400_DoesNotFetch()
        {
            _time.Now = Taipei(2026, 10, 8, 13, 59);
            await SeedAsync(D(2023, 10, 2), D(2026, 10, 7));

            await CreateService().RefreshAsync(CancellationToken.None);

            Assert.Empty(_client.Requested);
        }

        [Theory]
        [InlineData(10, 10)] // Saturday
        [InlineData(10, 11)] // Sunday
        public async Task Weekend_DoesNotFetch(int month, int day)
        {
            _time.Now = Taipei(2026, month, day, 15);
            await SeedAsync(D(2023, 10, 2), D(2026, 10, 8));

            await CreateService().RefreshAsync(CancellationToken.None);

            Assert.Empty(_client.Requested);
        }

        [Fact]
        public async Task Weekday_After1400_NewestDayBeforeToday_FetchesTheCurrentMonth()
        {
            _time.Now = Taipei(2026, 10, 8, 14);
            await SeedAsync(D(2023, 10, 2), D(2026, 10, 7));

            await CreateService().RefreshAsync(CancellationToken.None);

            Assert.Equal([(2026, 10)], _client.Requested);
        }

        [Fact]
        public async Task Weekday_After1400_TodayAlreadyStored_DoesNotFetch()
        {
            _time.Now = Taipei(2026, 10, 8, 18);
            await SeedAsync(D(2023, 10, 2), D(2026, 10, 8));

            await CreateService().RefreshAsync(CancellationToken.None);

            Assert.Empty(_client.Requested);
        }

        [Fact]
        public async Task AGap_IsFilledFromTheMonthOfTheNewestStoredDay_OneMonthAtATime()
        {
            _time.Now = Taipei(2026, 10, 8, 15);
            await SeedAsync(D(2023, 10, 2), D(2026, 7, 20));

            await CreateService().RefreshAsync(CancellationToken.None);

            Assert.Equal([(2026, 7), (2026, 8), (2026, 9), (2026, 10)], _client.Requested);
        }

        [Fact]
        public async Task AGap_AcrossAYearBoundary_WrapsDecemberToJanuary()
        {
            _time.Now = Taipei(2026, 1, 6, 15); // Tuesday
            await SeedAsync(D(2023, 1, 3), D(2025, 11, 28));

            await CreateService().RefreshAsync(CancellationToken.None);

            Assert.Equal([(2025, 11), (2025, 12), (2026, 1)], _client.Requested);
        }

        // ── backfill ────────────────────────────────────────────────────────

        [Theory]
        [InlineData(2026, 10, 10, 3)] // Saturday: a backfill is not tied to trading hours
        [InlineData(2026, 10, 8, 9)]  // Thursday morning, before the close
        public async Task EmptyTable_BackfillsThreeYears_NewestMonthFirst(int y, int m, int d, int hour)
        {
            _time.Now = Taipei(y, m, d, hour);

            await CreateService().RefreshAsync(CancellationToken.None);

            Assert.Equal(37, _client.Requested.Count); // this month plus the 36 before it
            Assert.Equal((2026, 10), _client.Requested[0]);
            Assert.Equal((2023, 10), _client.Requested[^1]);
            Assert.Equal(_client.Requested.Distinct().Count(), _client.Requested.Count);
        }

        [Fact]
        public async Task Backfill_ThatFailsPartWay_ContinuesFromWhereItStopped_NextRound()
        {
            _time.Now = Taipei(2026, 10, 10, 3);
            _client.FailOnCall = 6;

            await CreateService().RefreshAsync(CancellationToken.None);
            Assert.Equal(6, _client.Requested.Count); // five months fetched, the sixth failed and ended the round
            Assert.Equal((2026, 5), _client.Requested[^1]);

            _client.FailOnCall = null;
            _client.Requested.Clear();
            await CreateService().RefreshAsync(CancellationToken.None);

            Assert.Equal((2026, 5), _client.Requested[0]);     // the failed month is retried first
            Assert.Equal((2023, 10), _client.Requested[^1]);
            Assert.Equal(32, _client.Requested.Count);          // 2026-05 back to 2023-10, nothing already stored re-fetched
        }

        [Fact]
        public async Task CompleteBackfill_IsNotRepeated()
        {
            _time.Now = Taipei(2026, 10, 10, 3);
            await CreateService().RefreshAsync(CancellationToken.None);
            _client.Requested.Clear();

            await CreateService().RefreshAsync(CancellationToken.None);

            Assert.Empty(_client.Requested);
        }

        // ── failure handling ────────────────────────────────────────────────

        [Fact]
        public async Task UnexpectedResponse_StopsTheRound_AndStoresNothing()
        {
            _time.Now = Taipei(2026, 10, 10, 3);
            _client.FailOnCall = 1;

            await CreateService().RefreshAsync(CancellationToken.None);

            Assert.Single(_client.Requested);
            Assert.Null(await _store.GetRangeAsync());
        }

        [Fact]
        public async Task AnExceptionFromTheClient_DoesNotEscape_AndTheNextRoundStillRuns()
        {
            _time.Now = Taipei(2026, 10, 10, 3);
            _client.ThrowOnCall = 2;

            await CreateService().RefreshAsync(CancellationToken.None); // would end the host loop if it escaped

            _client.ThrowOnCall = null;
            await CreateService().RefreshAsync(CancellationToken.None);
            Assert.NotNull(await _store.GetRangeAsync());
        }

        [Fact]
        public async Task CancellationIsNotSwallowed()
        {
            _time.Now = Taipei(2026, 10, 10, 3);
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CreateService().RefreshAsync(cts.Token));
        }

        // ── what gets stored ────────────────────────────────────────────────

        [Fact]
        public async Task DaysAlreadyStoredInAFetchedMonth_AreNotDuplicated()
        {
            _time.Now = Taipei(2026, 10, 8, 15);
            await SeedAsync(D(2023, 10, 2), D(2026, 10, 7));
            _client.PointsFor = (y, m) => [new IndexClose(D(2026, 10, 7), 101m), new IndexClose(D(2026, 10, 8), 102m)];

            await CreateService().RefreshAsync(CancellationToken.None);

            // The seeded earliest day, the already-stored 7th (same value), and the new 8th.
            Assert.Equal(3, (await _store.GetRangeAsync())!.Value.Count);
        }

        [Fact]
        public async Task OnceTodayIsStored_ARefreshAsksForNothingMore()
        {
            _time.Now = Taipei(2026, 10, 8, 15);
            await SeedAsync(D(2023, 10, 2), D(2026, 10, 7));
            _client.PointsFor = (y, m) => [new IndexClose(D(2026, 10, 8), 102m)];
            var service = CreateService();

            await service.RefreshAsync(CancellationToken.None);
            var afterFirst = await _store.GetAllAsync();
            _client.Requested.Clear();
            await service.RefreshAsync(CancellationToken.None);

            Assert.Empty(_client.Requested);
            Assert.Equal(afterFirst, await _store.GetAllAsync());
        }

        [Fact]
        public async Task TodaysValue_IsNotStoredBeforeTheCloseIsPublished()
        {
            _time.Now = Taipei(2026, 10, 8, 10);
            _client.PointsFor = (y, m) => [new IndexClose(D(2026, 10, 7), 100m), new IndexClose(D(2026, 10, 8), 101m)];

            await CreateService().RefreshAsync(CancellationToken.None);

            Assert.Equal(D(2026, 10, 7), (await _store.GetRangeAsync())!.Value.Latest);
        }

        [Fact]
        public async Task TodaysValue_IsStoredOnceTheCloseIsPublished()
        {
            _time.Now = Taipei(2026, 10, 8, 14, 5);
            await SeedAsync(D(2023, 10, 2), D(2026, 10, 7));
            _client.PointsFor = (y, m) => [new IndexClose(D(2026, 10, 7), 101m), new IndexClose(D(2026, 10, 8), 102m)];

            await CreateService().RefreshAsync(CancellationToken.None);

            Assert.Equal(D(2026, 10, 8), (await _store.GetRangeAsync())!.Value.Latest);
        }

        [Fact]
        public async Task FutureDays_AreNeverStored()
        {
            _time.Now = Taipei(2026, 10, 8, 15);
            await SeedAsync(D(2023, 10, 2), D(2026, 10, 7));
            _client.PointsFor = (y, m) => [new IndexClose(D(2026, 10, 8), 102m), new IndexClose(D(2026, 10, 9), 103m)];

            await CreateService().RefreshAsync(CancellationToken.None);

            Assert.Equal(D(2026, 10, 8), (await _store.GetRangeAsync())!.Value.Latest);
        }

        // ── IsFinal ─────────────────────────────────────────────────────────

        [Theory]
        [InlineData("2026-10-07", "2026-10-08T00:00", true)]   // an earlier day is always final
        [InlineData("2026-10-08", "2026-10-08T13:59", false)]  // today, close not yet published
        [InlineData("2026-10-08", "2026-10-08T14:00", true)]
        [InlineData("2026-10-09", "2026-10-08T15:00", false)]  // tomorrow
        [InlineData("2026-10-10", "2026-10-10T15:00", false)]  // today, but a Saturday
        public void IsFinal_Cases(string date, string taipeiNow, bool expected) =>
            Assert.Equal(expected, TaiexTotalReturnRefreshService.IsFinal(DateOnly.Parse(date), DateTime.Parse(taipeiNow)));

        [Fact]
        public void PlanMonths_NothingToDo_IsEmpty()
        {
            var range = new TaiexStoredRange(D(2023, 10, 2), D(2026, 10, 8), 700);

            Assert.Empty(TaiexTotalReturnRefreshService.PlanMonths(range, new DateTime(2026, 10, 8, 20, 0, 0)));
        }

        // ── manual refresh (TryRefreshNowAsync) ─────────────────────────────

        private static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(60);

        /// <summary>How long a test waits on something that should answer at once before calling it a failure.</summary>
        private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

        /// <summary>The month's data as TWSE has it before today's close is published: the day
        /// already stored, and nothing newer.</summary>
        private void SourceHasNothingNew() => _client.PointsFor = (y, m) => [new IndexClose(D(2026, 10, 7), 101m)];

        [Fact]
        public async Task Manual_NewDayStored_IsUpdated_AndTheSignalsCacheVersionMoves()
        {
            _time.Now = Taipei(2026, 10, 8, 15);
            await SeedAsync(D(2023, 10, 2), D(2026, 10, 7));
            _client.PointsFor = (y, m) => [new IndexClose(D(2026, 10, 8), 102m)];
            var versionBefore = _status.Version;

            var outcome = await CreateService().TryRefreshNowAsync();

            Assert.Equal(DrawdownBrakeRefreshResult.Updated, outcome.Result);
            Assert.Null(outcome.RetryAfterSeconds);
            Assert.Equal([(2026, 10)], _client.Requested);
            Assert.Equal(D(2026, 10, 8), (await _store.GetRangeAsync())!.Value.Latest);
            Assert.Equal<DateTimeOffset?>(_time.Now, _status.LastCheckedAt);
            Assert.NotEqual(versionBefore, _status.Version);
        }

        [Fact]
        public async Task Manual_TwseAnswersButHasNothingNew_IsNoNewData_YetCountsAsChecked()
        {
            _time.Now = Taipei(2026, 10, 8, 15);
            await SeedAsync(D(2023, 10, 2), D(2026, 10, 7));
            SourceHasNothingNew();

            var outcome = await CreateService().TryRefreshNowAsync();

            Assert.Equal(DrawdownBrakeRefreshResult.NoNewData, outcome.Result);
            Assert.Single(_client.Requested);
            Assert.Equal<DateTimeOffset?>(_time.Now, _status.LastCheckedAt); // TWSE did answer
        }

        [Fact]
        public async Task Manual_TwseFails_IsSourceUnavailable_AndLastCheckedAtDoesNotMove()
        {
            _time.Now = Taipei(2026, 10, 8, 15);
            await SeedAsync(D(2023, 10, 2), D(2026, 10, 7));
            _client.FailOnCall = 1;
            var versionBefore = _status.Version;

            var outcome = await CreateService().TryRefreshNowAsync();

            Assert.Equal(DrawdownBrakeRefreshResult.SourceUnavailable, outcome.Result);
            Assert.Null(outcome.RetryAfterSeconds);
            Assert.Null(_status.LastCheckedAt);
            Assert.Equal(versionBefore, _status.Version);
            Assert.Equal(D(2026, 10, 7), (await _store.GetRangeAsync())!.Value.Latest);
        }

        [Fact]
        public async Task Manual_FailingPartWayThroughAGap_StopsThere_AndReportsSourceUnavailable()
        {
            _time.Now = Taipei(2026, 10, 8, 15);
            await SeedAsync(D(2023, 10, 2), D(2026, 7, 20));
            _client.FailOnCall = 3; // July and August come back, September does not

            var outcome = await CreateService().TryRefreshNowAsync();

            Assert.Equal(DrawdownBrakeRefreshResult.SourceUnavailable, outcome.Result);
            Assert.Equal([(2026, 7), (2026, 8), (2026, 9)], _client.Requested); // October never asked
            // What did come back was stored and announced, so the signal rebuilt from it is current.
            Assert.Equal(D(2026, 8, 15), (await _store.GetRangeAsync())!.Value.Latest);
            Assert.Equal<DateTimeOffset?>(_time.Now, _status.LastCheckedAt);
        }

        [Fact]
        public async Task Manual_OnlyCatchesUpForward_NeverBackfills()
        {
            _time.Now = Taipei(2026, 10, 8, 15);
            // Only a year of history is stored, so the hourly round would also want 2023–2025.
            await SeedAsync(D(2025, 10, 2), D(2026, 7, 20));

            await CreateService().TryRefreshNowAsync();

            Assert.Equal([(2026, 7), (2026, 8), (2026, 9), (2026, 10)], _client.Requested);
        }

        // Throttling

        [Fact]
        public async Task Manual_WithinTheCooldown_IsThrottled_WithTheTimeLeft_AndAsksNothing()
        {
            _time.Now = Taipei(2026, 10, 8, 15);
            await SeedAsync(D(2023, 10, 2), D(2026, 10, 7));
            SourceHasNothingNew();
            var service = CreateService();
            await service.TryRefreshNowAsync(); // asks TWSE, starts the cooldown
            _client.Requested.Clear();

            _time.Now += TimeSpan.FromSeconds(25);
            var outcome = await service.TryRefreshNowAsync();

            Assert.Equal(DrawdownBrakeRefreshResult.Throttled, outcome.Result);
            Assert.Equal(35, outcome.RetryAfterSeconds);
            Assert.Empty(_client.Requested);
        }

        [Fact]
        public async Task Manual_TheCooldownIsCountedUpAndRoundedUp_AndEndsAtSixtySeconds()
        {
            _time.Now = Taipei(2026, 10, 8, 15);
            await SeedAsync(D(2023, 10, 2), D(2026, 10, 7));
            SourceHasNothingNew();
            var service = CreateService();
            await service.TryRefreshNowAsync();
            _client.Requested.Clear();

            _time.Now += Cooldown - TimeSpan.FromMilliseconds(500);
            var almost = await service.TryRefreshNowAsync();
            Assert.Equal(DrawdownBrakeRefreshResult.Throttled, almost.Result);
            Assert.Equal(1, almost.RetryAfterSeconds); // never 0 while still throttled
            Assert.Empty(_client.Requested);

            _time.Now += TimeSpan.FromMilliseconds(500); // exactly 60 s after the first round ended
            var after = await service.TryRefreshNowAsync();
            Assert.Equal(DrawdownBrakeRefreshResult.NoNewData, after.Result);
            Assert.Single(_client.Requested); // it asked again
        }

        [Fact]
        public async Task Manual_AfterAFailedAttempt_IsAlsoThrottled()
        {
            _time.Now = Taipei(2026, 10, 8, 15);
            await SeedAsync(D(2023, 10, 2), D(2026, 10, 7));
            _client.FailOnCall = 1;
            var service = CreateService();
            Assert.Equal(DrawdownBrakeRefreshResult.SourceUnavailable, (await service.TryRefreshNowAsync()).Result);
            _client.Requested.Clear();
            _time.Now += TimeSpan.FromSeconds(10);

            var outcome = await service.TryRefreshNowAsync();

            Assert.Equal(DrawdownBrakeRefreshResult.Throttled, outcome.Result);
            Assert.Equal(50, outcome.RetryAfterSeconds);
            Assert.Empty(_client.Requested); // a host that just refused us is not asked again at once
        }

        [Fact]
        public async Task TheBackgroundRoundAndManualRefresh_ShareOneCooldown()
        {
            _time.Now = Taipei(2026, 10, 8, 15);
            await SeedAsync(D(2023, 10, 2), D(2026, 10, 7));
            SourceHasNothingNew();
            var service = CreateService();
            await service.RefreshAsync(CancellationToken.None); // the hourly round asks TWSE
            Assert.Single(_client.Requested);
            _client.Requested.Clear();

            _time.Now += TimeSpan.FromSeconds(10);
            var outcome = await service.TryRefreshNowAsync();

            Assert.Equal(DrawdownBrakeRefreshResult.Throttled, outcome.Result);
            Assert.Equal(50, outcome.RetryAfterSeconds);
            Assert.Empty(_client.Requested);
        }

        [Fact]
        public async Task TheBackgroundRound_IsNotHeldBackByTheCooldown()
        {
            // A failed manual attempt is exactly when the hourly retry is wanted.
            _time.Now = Taipei(2026, 10, 8, 15);
            await SeedAsync(D(2023, 10, 2), D(2026, 10, 7));
            _client.FailOnCall = 1;
            var service = CreateService();
            await service.TryRefreshNowAsync();
            _client.Requested.Clear();
            _client.FailOnCall = null; // TWSE is back
            _client.PointsFor = (y, m) => [new IndexClose(D(2026, 10, 8), 102m)];

            _time.Now += TimeSpan.FromSeconds(10);
            await service.RefreshAsync(CancellationToken.None);

            Assert.Equal([(2026, 10)], _client.Requested);
            Assert.Equal(D(2026, 10, 8), (await _store.GetRangeAsync())!.Value.Latest); // and it stored what came back
        }

        [Fact]
        public async Task Manual_TenAtOnce_OnlyOneAsksTwse()
        {
            _time.Now = Taipei(2026, 10, 8, 15);
            await SeedAsync(D(2023, 10, 2), D(2026, 10, 7));
            SourceHasNothingNew();
            var service = CreateService();

            var outcomes = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => Task.Run(() => service.TryRefreshNowAsync())));

            Assert.Single(_client.Requested);
            Assert.Equal(1, outcomes.Count(o => o.Result != DrawdownBrakeRefreshResult.Throttled));
            Assert.Equal(9, outcomes.Count(o => o.Result == DrawdownBrakeRefreshResult.Throttled && o.RetryAfterSeconds is > 0 and <= 60));
        }

        [Fact]
        public async Task Manual_WhileARoundIsStillRunning_IsThrottledAtOnce_NotQueued()
        {
            _time.Now = Taipei(2026, 10, 8, 15);
            await SeedAsync(D(2023, 10, 2), D(2026, 10, 7));
            var release = new TaskCompletionSource();
            _client.Block = release.Task;
            _client.PointsFor = (y, m) => [new IndexClose(D(2026, 10, 8), 102m)];
            var service = CreateService();

            var first = service.TryRefreshNowAsync();
            await _client.Entered.Task.WaitAsync(Patience); // the first press is now inside the TWSE call
            var second = await service.TryRefreshNowAsync().WaitAsync(Patience); // a regression would queue here, not answer

            Assert.Equal(DrawdownBrakeRefreshResult.Throttled, second.Result);
            Assert.Equal(60, second.RetryAfterSeconds);
            Assert.False(first.IsCompleted);

            release.SetResult();
            Assert.Equal(DrawdownBrakeRefreshResult.Updated, (await first.WaitAsync(Patience)).Result);
            Assert.Single(_client.Requested);
        }

        [Fact]
        public async Task Manual_WhileTheBackgroundBackfillRuns_IsThrottled()
        {
            _time.Now = Taipei(2026, 10, 10, 3);
            var release = new TaskCompletionSource();
            _client.Block = release.Task;
            var service = CreateService();

            var background = service.RefreshAsync(CancellationToken.None); // empty table: the 3-year backfill
            await _client.Entered.Task.WaitAsync(Patience);
            var outcome = await service.TryRefreshNowAsync().WaitAsync(Patience);
            release.SetResult();
            await background.WaitAsync(Patience);

            Assert.Equal(DrawdownBrakeRefreshResult.Throttled, outcome.Result);
            Assert.Equal(37, _client.Requested.Count); // the press added no request of its own
        }

        [Fact]
        public async Task Manual_WhenTheClientThrows_TheExceptionSurfaces_YetTheGateIsFreeAndTheCooldownRuns()
        {
            _time.Now = Taipei(2026, 10, 8, 15);
            await SeedAsync(D(2023, 10, 2), D(2026, 10, 7));
            _client.ThrowOnCall = 1;
            var service = CreateService();

            await Assert.ThrowsAsync<InvalidOperationException>(() => service.TryRefreshNowAsync());
            var next = await service.TryRefreshNowAsync();

            Assert.Equal(DrawdownBrakeRefreshResult.Throttled, next.Result); // not stuck, not asking again at once
            Assert.Single(_client.Requested);
        }

        // Short-circuits: no request, and no cooldown started

        [Theory]
        [InlineData(2026, 10, 10, 12)] // Saturday, Friday's close already stored
        [InlineData(2026, 10, 11, 20)] // Sunday
        [InlineData(2026, 10, 12, 9)]  // Monday before the close: Friday is the newest there can be
        [InlineData(2026, 10, 12, 13)]
        public async Task Manual_WhenNothingNewerCanExistYet_IsNoNewData_WithoutAskingTwse(int y, int m, int d, int hour)
        {
            _time.Now = Taipei(y, m, d, hour);
            await SeedAsync(D(2023, 10, 2), D(2026, 10, 9));

            var outcome = await CreateService().TryRefreshNowAsync();

            Assert.Equal(DrawdownBrakeRefreshResult.NoNewData, outcome.Result);
            Assert.Empty(_client.Requested);
            Assert.Null(_status.LastCheckedAt); // TWSE was not asked, so nothing was "checked"
        }

        [Fact]
        public async Task Manual_BeforeTheClose_WithYesterdayStored_IsNoNewData_WithoutAskingTwse()
        {
            _time.Now = Taipei(2026, 10, 8, 13, 59);
            await SeedAsync(D(2023, 10, 2), D(2026, 10, 7));

            var outcome = await CreateService().TryRefreshNowAsync();

            Assert.Equal(DrawdownBrakeRefreshResult.NoNewData, outcome.Result);
            Assert.Empty(_client.Requested);
        }

        [Fact]
        public async Task Manual_ShortCircuit_StartsNoCooldown_SoTheFirstRealAttemptIsNotThrottled()
        {
            _time.Now = Taipei(2026, 10, 8, 13, 59);
            await SeedAsync(D(2023, 10, 2), D(2026, 10, 7));
            var service = CreateService();
            await service.TryRefreshNowAsync(); // short-circuit
            _client.PointsFor = (y, m) => [new IndexClose(D(2026, 10, 8), 102m)];

            _time.Now = Taipei(2026, 10, 8, 14, 0) + TimeSpan.FromSeconds(5); // 20 s later, close now published
            var outcome = await service.TryRefreshNowAsync();

            Assert.Equal(DrawdownBrakeRefreshResult.Updated, outcome.Result);
        }

        [Fact]
        public async Task Manual_AfterTheClose_WithTodayAlreadyStored_IsNoNewData_WithoutAskingTwse()
        {
            _time.Now = Taipei(2026, 10, 8, 18);
            await SeedAsync(D(2023, 10, 2), D(2026, 10, 8));

            var outcome = await CreateService().TryRefreshNowAsync();

            Assert.Equal(DrawdownBrakeRefreshResult.NoNewData, outcome.Result);
            Assert.Empty(_client.Requested);
        }

        [Fact]
        public async Task Manual_AWeekendWithFridaysCloseMissing_StillAsks_AndStoresFridayOnly()
        {
            // Nothing is final on a Saturday except what was published on or before Friday.
            _time.Now = Taipei(2026, 10, 10, 12);
            await SeedAsync(D(2023, 10, 2), D(2026, 10, 8));
            _client.PointsFor = (y, m) => [new IndexClose(D(2026, 10, 9), 103m), new IndexClose(D(2026, 10, 10), 104m)];

            var outcome = await CreateService().TryRefreshNowAsync();

            Assert.Equal(DrawdownBrakeRefreshResult.Updated, outcome.Result);
            Assert.Equal(D(2026, 10, 9), (await _store.GetRangeAsync())!.Value.Latest);
        }

        [Fact]
        public async Task Manual_BeforeTheClose_NeverStoresTodaysValue()
        {
            _time.Now = Taipei(2026, 10, 12, 10); // Monday morning; Friday's close is missing
            await SeedAsync(D(2023, 10, 2), D(2026, 10, 8));
            _client.PointsFor = (y, m) => [new IndexClose(D(2026, 10, 9), 103m), new IndexClose(D(2026, 10, 12), 104m)];

            await CreateService().TryRefreshNowAsync();

            Assert.Equal(D(2026, 10, 9), (await _store.GetRangeAsync())!.Value.Latest);
        }

        [Fact]
        public async Task Manual_OnAnEmptyTable_LeavesTheBackfillAlone()
        {
            _time.Now = Taipei(2026, 10, 8, 15);

            var outcome = await CreateService().TryRefreshNowAsync();

            Assert.Equal(DrawdownBrakeRefreshResult.NoNewData, outcome.Result);
            Assert.Empty(_client.Requested);
        }

        [Theory]
        [InlineData("2026-10-07", "2026-10-08T13:59", true)]   // Thursday before the close; Wednesday stored
        [InlineData("2026-10-06", "2026-10-08T13:59", false)]  // Wednesday's close is missing
        [InlineData("2026-10-07", "2026-10-08T14:00", false)]  // close published, today not stored
        [InlineData("2026-10-08", "2026-10-08T14:00", true)]   // close published, today stored
        [InlineData("2026-10-09", "2026-10-10T12:00", true)]   // Saturday, Friday stored
        [InlineData("2026-10-08", "2026-10-10T12:00", false)]  // Saturday, Friday missing
        [InlineData("2026-10-09", "2026-10-11T23:59", true)]   // Sunday
        [InlineData("2026-10-09", "2026-10-12T09:00", true)]   // Monday morning: previous weekday is Friday
        [InlineData("2026-10-08", "2026-10-12T09:00", false)]
        [InlineData("2026-10-09", "2026-10-12T14:00", false)]
        public void NoNewDataPossible_Cases(string latest, string taipeiNow, bool expected) =>
            Assert.Equal(expected, TaiexTotalReturnRefreshService.NoNewDataPossible(DateOnly.Parse(latest), DateTime.Parse(taipeiNow)));

        // The two sides of the signal's cache: nothing moves until TWSE has been heard from

        [Fact]
        public async Task TheBackgroundRound_AlsoAnnouncesWhatItStored()
        {
            _time.Now = Taipei(2026, 10, 8, 15);
            await SeedAsync(D(2023, 10, 2), D(2026, 10, 7));
            _client.PointsFor = (y, m) => [new IndexClose(D(2026, 10, 8), 102m)];
            var versionBefore = _status.Version;

            await CreateService().RefreshAsync(CancellationToken.None);

            Assert.NotEqual(versionBefore, _status.Version);
            Assert.Equal<DateTimeOffset?>(_time.Now, _status.LastCheckedAt);
        }

        [Fact]
        public async Task ARoundThatAsksNothing_DoesNotTouchTheStatus()
        {
            _time.Now = Taipei(2026, 10, 8, 10);
            await SeedAsync(D(2023, 10, 2), D(2026, 10, 7));
            var versionBefore = _status.Version;

            await CreateService().RefreshAsync(CancellationToken.None);

            Assert.Equal(versionBefore, _status.Version);
            Assert.Null(_status.LastCheckedAt);
        }

        // Logging

        [Fact]
        public async Task EveryManualRefresh_LogsOneInformationLine_WithTheResultAndWhetherTwseWasAsked()
        {
            _time.Now = Taipei(2026, 10, 8, 15);
            await SeedAsync(D(2023, 10, 2), D(2026, 10, 7));
            SourceHasNothingNew();
            var logger = new CapturingLogger<TaiexTotalReturnRefreshService>();
            var service = new TaiexTotalReturnRefreshService(_client, _store, _status, logger, _time);

            await service.TryRefreshNowAsync(); // asks TWSE
            _time.Now += TimeSpan.FromSeconds(5);
            await service.TryRefreshNowAsync(); // throttled

            var lines = logger.Entries.Where(e => e.Level == LogLevel.Information && e.Message.Contains("manual refresh")).ToList();
            Assert.Equal(2, lines.Count);
            Assert.Contains("NoNewData", lines[0].Message);
            Assert.Contains("TWSE asked", lines[0].Message);
            Assert.Contains("Throttled", lines[1].Message);
            Assert.Contains("TWSE not asked", lines[1].Message);
        }

        private sealed class FakeClient : ITaiexTotalReturnClient
        {
            public List<(int Year, int Month)> Requested { get; } = [];
            public int? FailOnCall { get; set; }
            public int? ThrowOnCall { get; set; }

            /// <summary>When set, a call waits for it after announcing itself on <see cref="Entered"/>.</summary>
            public Task? Block { get; set; }
            public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

            /// <summary>One mid-month day per month unless a test says otherwise.</summary>
            public Func<int, int, IReadOnlyList<IndexClose>> PointsFor { get; set; } =
                (y, m) => [new IndexClose(new DateOnly(y, m, 15), 100m + m)];

            public async Task<TaiexMonthResult> FetchMonthAsync(int year, int month, CancellationToken ct = default)
            {
                ct.ThrowIfCancellationRequested();
                lock (Requested) Requested.Add((year, month));
                Entered.TrySetResult();
                if (Block is not null) await Block;
                if (ThrowOnCall == Requested.Count) throw new InvalidOperationException("boom");
                return FailOnCall == Requested.Count
                    ? TaiexMonthResult.Failure
                    : TaiexMonthResult.Ok(PointsFor(year, month));
            }
        }

        private sealed class FakeTime : TimeProvider
        {
            public DateTimeOffset Now { get; set; }
            public override DateTimeOffset GetUtcNow() => Now;
        }

        private sealed class CapturingLogger<T> : ILogger<T>
        {
            public List<(LogLevel Level, string Message)> Entries { get; } = [];

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                Entries.Add((logLevel, formatter(state, exception)));
        }
    }
}
