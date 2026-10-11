using JackyAIApp.Server.Data;
using JackyAIApp.Server.Services.Portfolio.DrawdownBrake;
using Microsoft.EntityFrameworkCore;
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

        public TaiexTotalReturnRefreshServiceTests()
        {
            _store = new TaiexTotalReturnStore(new TestDbContextFactory(_options));
        }

        private TaiexTotalReturnRefreshService CreateService() =>
            new(_client, _store, NullLogger<TaiexTotalReturnRefreshService>.Instance, _time);

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

        private sealed class FakeClient : ITaiexTotalReturnClient
        {
            public List<(int Year, int Month)> Requested { get; } = [];
            public int? FailOnCall { get; set; }
            public int? ThrowOnCall { get; set; }

            /// <summary>One mid-month day per month unless a test says otherwise.</summary>
            public Func<int, int, IReadOnlyList<IndexClose>> PointsFor { get; set; } =
                (y, m) => [new IndexClose(new DateOnly(y, m, 15), 100m + m)];

            public Task<TaiexMonthResult> FetchMonthAsync(int year, int month, CancellationToken ct = default)
            {
                ct.ThrowIfCancellationRequested();
                Requested.Add((year, month));
                if (ThrowOnCall == Requested.Count) throw new InvalidOperationException("boom");
                return Task.FromResult(FailOnCall == Requested.Count
                    ? TaiexMonthResult.Failure
                    : TaiexMonthResult.Ok(PointsFor(year, month)));
            }
        }

        private sealed class FakeTime : TimeProvider
        {
            public DateTimeOffset Now { get; set; }
            public override DateTimeOffset GetUtcNow() => Now;
        }
    }
}
