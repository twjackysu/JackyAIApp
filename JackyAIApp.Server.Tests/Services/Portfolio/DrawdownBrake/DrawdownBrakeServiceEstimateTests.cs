using JackyAIApp.Server.DTO.Portfolio;
using JackyAIApp.Server.Services.Portfolio.DrawdownBrake;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace JackyAIApp.Server.Tests.Services.Portfolio.DrawdownBrake
{
    /// <summary>TASK-038: the intraday estimate and the "signal corrected" flag, laid on the official signal.</summary>
    public class DrawdownBrakeServiceEstimateTests : IDisposable
    {
        // 2026-10-08 is a Thursday.
        private static readonly DateOnly Thursday = new(2026, 10, 8);

        private readonly Mock<ITaiexTotalReturnStore> _store = new();
        private readonly Mock<ITwseHolidayCalendar> _calendar = new();
        private readonly Mock<IMisTaiexQuoteSource> _quotes = new();
        private readonly FakeTime _time = new();
        private readonly TaiexDataStatus _status = new();
        private readonly DrawdownBrakeTelemetry _telemetry = new(NullLogger<DrawdownBrakeTelemetry>.Instance);
        private readonly IMemoryCache _cache = new MemoryCache(new MemoryCacheOptions());

        private MisQuoteSnapshot _snapshot;
        private readonly List<TimeSpan> _maxAges = [];

        public DrawdownBrakeServiceEstimateTests()
        {
            _time.Now = Taipei(2026, 10, 8, 10);
            _snapshot = new MisQuoteSnapshot(null, _time.Now);
            _quotes.Setup(q => q.GetAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
                .Callback((TimeSpan maxAge, CancellationToken _) => _maxAges.Add(maxAge))
                .ReturnsAsync(() => _snapshot);
            StoreHolds(Flat(Thursday.AddDays(-1)));
        }

        public void Dispose() => _telemetry.Dispose();

        private DrawdownBrakeService CreateService() =>
            new(_store.Object, _calendar.Object, _status, _quotes.Object, _cache, _telemetry, _time);

        private static DateTimeOffset Taipei(int y, int m, int d, int h, int min = 0) =>
            new DateTimeOffset(y, m, d, h, min, 0, TimeSpan.FromHours(8)).ToUniversalTime();

        /// <summary>740 consecutive days at 100, ending on <paramref name="last"/>: OFFENSE, no drawdown.</summary>
        private static List<IndexClose> Flat(DateOnly last, decimal close = 100m) =>
            Enumerable.Range(0, 740).Select(i => new IndexClose(last.AddDays(i - 739), close)).ToList();

        private void StoreHolds(IReadOnlyList<IndexClose> closes) =>
            _store.Setup(s => s.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(closes);

        /// <summary>MIS now says the index is <paramref name="ratio"/> times yesterday's close.</summary>
        private MisQuoteSnapshot MisSays(decimal ratio, DateOnly? tradeDate = null, int minute = 0)
        {
            var quote = new MisIndexQuote(
                tradeDate ?? Thursday,
                new DateTimeOffset(2026, 10, 8, 10, minute, 5, TimeSpan.FromHours(8)),
                23000m * ratio, 23000m);
            return _snapshot = new MisQuoteSnapshot(quote, _time.Now);
        }

        private void MarketClosedOn(params DateOnly[] days)
        {
            foreach (var d in days)
                _calendar.Setup(c => c.IsMarketClosedAsync(d, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        }

        private void MisWasNeverAsked() =>
            _quotes.Verify(q => q.GetAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Never);

        // ── what the estimate says ──────────────────────────────────────────

        [Fact]
        public async Task InTheWindow_WithAReading_TheSignalCarriesTheEstimate()
        {
            MisSays(0.90m); // −10% on a flat history: through the −8% trigger

            var dto = await CreateService().GetAsync();

            var estimate = Assert.IsType<DrawdownBrakeEstimateDto>(dto.Estimate);
            Assert.Equal(90m, estimate.EstimatedClose);
            Assert.Equal(BrakeState.DEFENSE, estimate.State);
            Assert.Equal(BrakeState.OFFENSE, estimate.PrevState);
            Assert.Equal(-10m, estimate.DrawdownPct);
            Assert.Equal(new DateTimeOffset(2026, 10, 8, 10, 0, 5, TimeSpan.FromHours(8)), estimate.AsOfTime);
        }

        [Fact]
        public async Task TheOfficialFields_AreWhatTheyWereWithoutAnEstimate()
        {
            var without = await CreateService().GetAsync();
            _cache.Remove(DrawdownBrakeService.CacheKey);
            MisSays(0.90m);

            var with = await CreateService().GetAsync();

            Assert.Null(without.Estimate);
            Assert.NotNull(with.Estimate);
            Assert.Equal(without.AsOfDate, with.AsOfDate);
            Assert.Equal(without.State, with.State);
            Assert.Equal(without.PrevState, with.PrevState);
            Assert.Equal(without.Close, with.Close);
            Assert.Equal(without.DrawdownPct, with.DrawdownPct);
            Assert.Equal(without.GeneratedAt, with.GeneratedAt);
            Assert.False(with.EstimateCorrected);
        }

        [Fact]
        public async Task AnEstimateOfNoChange_StillSaysOffense()
        {
            MisSays(1.0m);

            var estimate = (await CreateService().GetAsync()).Estimate!;

            Assert.Equal(100m, estimate.EstimatedClose);
            Assert.Equal(BrakeState.OFFENSE, estimate.State);
        }

        [Fact]
        public async Task WhenMisCannotBeRead_ThereIsNoEstimate_AndTheOfficialSignalIsServedAsEver()
        {
            _snapshot = new MisQuoteSnapshot(null, _time.Now);

            var dto = await CreateService().GetAsync();

            Assert.Null(dto.Estimate);
            Assert.False(dto.IsInitializing);
            Assert.Equal(BrakeState.OFFENSE, dto.State);
            Assert.Equal(Thursday.AddDays(-1), dto.AsOfDate);
        }

        [Fact]
        public async Task AReadingOfAnotherDay_IsNoEstimateOfToday()
        {
            // MIS answers a market holiday with the last session's numbers.
            MisSays(0.90m, tradeDate: Thursday.AddDays(-1));

            Assert.Null((await CreateService().GetAsync()).Estimate);
        }

        [Fact]
        public async Task MisBeingDown_DoesNotTurnTheRefreshIntoAnError_OrHideTheOfficialSignal()
        {
            _quotes.Setup(q => q.GetAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new MisQuoteSnapshot(null, _time.Now));

            var dto = await CreateService().GetAfterEstimateRefreshAsync();

            Assert.Null(dto.Estimate);
            Assert.Equal(BrakeState.OFFENSE, dto.State);
        }

        // ── when an estimate is wanted ──────────────────────────────────────

        [Theory]
        [InlineData(9, 0, true)]    // from nine o'clock
        [InlineData(8, 59, false)]  // not before
        [InlineData(0, 0, false)]
        [InlineData(13, 29, true)]
        [InlineData(14, 30, true)]  // after the close, until the official close is stored
        [InlineData(23, 59, true)]
        public async Task TheWindowOpensAtNine_OnATradingDay(int hour, int minute, bool wanted)
        {
            _time.Now = Taipei(2026, 10, 8, hour, minute);
            MisSays(0.90m);

            var dto = await CreateService().GetAsync();

            Assert.Equal(wanted, dto.Estimate is not null);
            if (!wanted) MisWasNeverAsked();
        }

        [Theory]
        [InlineData(2026, 10, 10)] // Saturday (the stored close is Friday's)
        [InlineData(2026, 10, 11)] // Sunday
        public async Task AtTheWeekend_ThereIsNoEstimate_AndMisIsNotAsked(int y, int m, int d)
        {
            StoreHolds(Flat(new DateOnly(2026, 10, 9)));
            _time.Now = Taipei(y, m, d, 11);
            MisSays(0.90m);

            Assert.Null((await CreateService().GetAsync()).Estimate);
            MisWasNeverAsked();
        }

        [Fact]
        public async Task OnceTodaysOfficialCloseIsStored_ThereIsNoEstimate_AndMisIsNotAsked()
        {
            StoreHolds(Flat(Thursday)); // today's close is in
            _time.Now = Taipei(2026, 10, 8, 14, 30);
            MisSays(0.90m);

            Assert.Null((await CreateService().GetAsync()).Estimate);
            MisWasNeverAsked();
        }

        [Fact]
        public async Task TheEstimateGoesAway_TheMomentTheOfficialCloseIsStored()
        {
            _time.Now = Taipei(2026, 10, 8, 14, 10);
            MisSays(1.0m);
            var service = CreateService();
            Assert.NotNull((await service.GetAsync()).Estimate);

            StoreHolds(Flat(Thursday));
            _status.RecordCheck(_time.Now); // what the refresh does after storing days
            var after = await service.GetAsync();

            Assert.Null(after.Estimate);
            Assert.Equal(Thursday, after.AsOfDate);
        }

        [Fact]
        public async Task WhileInitializing_ThereIsNothingToEstimateFrom()
        {
            StoreHolds([]);
            MisSays(0.90m);

            var dto = await CreateService().GetAsync();

            Assert.True(dto.IsInitializing);
            Assert.Null(dto.Estimate);
            MisWasNeverAsked();
        }

        // ── the base must be the previous trading day ───────────────────────

        [Fact]
        public async Task OnMondayMorning_FridaysCloseIsTheBase_WithNoCalendarNeeded()
        {
            StoreHolds(Flat(new DateOnly(2026, 10, 9)));
            _time.Now = Taipei(2026, 10, 12, 10);
            MisSays(0.90m, tradeDate: new DateOnly(2026, 10, 12));

            Assert.NotNull((await CreateService().GetAsync()).Estimate);
            _calendar.Verify(c => c.IsMarketClosedAsync(It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task AWeekdayBetweenThatIsKnownToBeAHoliday_DoesNotSpoilTheBase()
        {
            // Stored: Mon 10-05. Tue 10-06 and Wed 10-07 were holidays. Today Thursday.
            StoreHolds(Flat(new DateOnly(2026, 10, 5)));
            MarketClosedOn(new DateOnly(2026, 10, 6), new DateOnly(2026, 10, 7));
            MisSays(0.90m);

            Assert.NotNull((await CreateService().GetAsync()).Estimate);
        }

        [Fact]
        public async Task AMissingTradingDay_MeansNoEstimate_NotAnEstimateFromAStaleBase()
        {
            // Stored: Mon 10-05. Tue was a holiday; Wed 10-07 was a trading day we do not have.
            StoreHolds(Flat(new DateOnly(2026, 10, 5)));
            MarketClosedOn(new DateOnly(2026, 10, 6));
            _calendar.Setup(c => c.IsMarketClosedAsync(new DateOnly(2026, 10, 7), It.IsAny<CancellationToken>())).ReturnsAsync(false);
            MisSays(0.90m);

            Assert.Null((await CreateService().GetAsync()).Estimate);
            MisWasNeverAsked();
        }

        [Fact]
        public async Task WhereTheCalendarCannotSay_ThereIsNoEstimate_RatherThanAGuess()
        {
            StoreHolds(Flat(new DateOnly(2026, 10, 6)));
            _calendar.Setup(c => c.IsMarketClosedAsync(It.IsAny<DateOnly>(), It.IsAny<CancellationToken>())).ReturnsAsync((bool?)null);
            MisSays(0.90m);

            Assert.Null((await CreateService().GetAsync()).Estimate);
            MisWasNeverAsked();
        }

        // ── how fresh, and how often MIS is asked ───────────────────────────

        [Fact]
        public async Task ALook_AcceptsAReadingUpToThirtySecondsOld_TheRefreshButtonOnlyTenSeconds()
        {
            MisSays(0.97m);
            var service = CreateService();

            await service.GetAsync();
            await service.GetWithCacheStatusAsync();
            await service.GetAfterEstimateRefreshAsync();

            Assert.Equal([TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(10)], _maxAges);
        }

        [Fact]
        public async Task TheOfficialSignal_IsReadFromTheDatabaseOncePerMinute_NoMatterHowOftenTheEstimateMoves()
        {
            var service = CreateService();

            MisSays(0.97m, minute: 0);
            await service.GetAsync();
            _time.Now += TimeSpan.FromSeconds(30);
            MisSays(0.96m, minute: 1);
            await service.GetAsync();

            _store.Verify(s => s.GetAllAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task ANewReading_ShowsAtOnce_OnTheAlreadyCachedOfficialSignal()
        {
            var service = CreateService();
            MisSays(0.97m, minute: 0);
            var first = await service.GetAsync();

            _time.Now += TimeSpan.FromSeconds(30);
            MisSays(0.91m, minute: 1);
            var second = await service.GetAsync();

            Assert.Equal(97m, first.Estimate!.EstimatedClose);
            Assert.Equal(91m, second.Estimate!.EstimatedClose);
            Assert.Equal(first.GeneratedAt, second.GeneratedAt); // same official copy underneath
        }

        [Fact]
        public async Task TheSameReadingAndTheSameOfficialCopy_AreOneObject()
        {
            MisSays(0.97m);
            var service = CreateService();

            var first = await service.GetAsync();
            var second = await service.GetAsync();

            Assert.Same(first, second);
        }

        [Fact]
        public async Task WithNoEstimateAndNothingCorrected_TheSharedOfficialCopyItselfIsHandedOut()
        {
            var service = CreateService();

            var first = await service.GetAsync();
            var second = await service.GetAsync();

            Assert.Same(first, second);
            Assert.Null(first.Estimate);
        }

        [Fact]
        public async Task LayingTheEstimateOn_NeverChangesTheSharedCopy()
        {
            var service = CreateService();
            var official = await service.GetAsync();       // no reading yet: the shared copy itself

            MisSays(0.90m);
            var withEstimate = await service.GetAsync();   // same cached official copy underneath

            Assert.NotSame(official, withEstimate);
            Assert.Null(official.Estimate);
            Assert.False(official.EstimateCorrected);
        }

        [Fact]
        public async Task TheViewMetric_StillDescribesTheOfficialSignalsCache_NotTheEstimate()
        {
            MisSays(0.97m);
            var service = CreateService();

            var first = await service.GetWithCacheStatusAsync();
            var second = await service.GetWithCacheStatusAsync();

            Assert.False(first.CacheHit);
            Assert.True(second.CacheHit);
        }

        // ── "signal corrected" ──────────────────────────────────────────────

        /// <summary>
        /// Plays a day: an estimate of <paramref name="estimateRatio"/> is seen, then the official close
        /// (flat at 100, so OFFENSE) arrives and the service is asked again.
        /// </summary>
        private async Task<DrawdownBrakeDto> TheDayEnds(decimal estimateRatio, DrawdownBrakeService? service = null)
        {
            service ??= CreateService();
            _time.Now = Taipei(2026, 10, 8, 13, 20);
            MisSays(estimateRatio);
            await service.GetAsync();

            _time.Now = Taipei(2026, 10, 8, 14, 40);
            StoreHolds(Flat(Thursday));
            _status.RecordCheck(_time.Now);
            return await service.GetAsync();
        }

        [Fact]
        public async Task AnEstimateOfDefense_ThatTheOfficialCloseOverturns_IsReportedAsCorrected()
        {
            var dto = await TheDayEnds(estimateRatio: 0.90m);   // estimate DEFENSE; official OFFENSE

            Assert.True(dto.EstimateCorrected);
            Assert.Equal(BrakeState.OFFENSE, dto.State);
            Assert.Null(dto.Estimate);   // and the estimate is gone
        }

        [Fact]
        public async Task AnEstimateThatTheOfficialCloseConfirms_IsNotCorrected()
        {
            var dto = await TheDayEnds(estimateRatio: 1.0m);    // estimate OFFENSE; official OFFENSE

            Assert.False(dto.EstimateCorrected);
        }

        [Fact]
        public async Task TheLatestEstimateOfTheDay_IsTheOneTheOfficialCloseIsComparedWith()
        {
            var service = CreateService();
            _time.Now = Taipei(2026, 10, 8, 10);
            MisSays(0.90m, minute: 0);                       // DEFENSE for a while …
            await service.GetAsync();
            _time.Now = Taipei(2026, 10, 8, 13, 25);
            MisSays(1.0m, minute: 25);                       // … but by the end OFFENSE
            await service.GetAsync();

            _time.Now = Taipei(2026, 10, 8, 14, 40);
            StoreHolds(Flat(Thursday));
            _status.RecordCheck(_time.Now);

            Assert.False((await service.GetAsync()).EstimateCorrected);
        }

        [Fact]
        public async Task TheCorrection_StaysOnTheSignal_UntilTheNextOfficialDayArrives()
        {
            var service = CreateService();
            await TheDayEnds(0.90m, service);

            _time.Now = Taipei(2026, 10, 8, 16);
            var evening = await service.GetAsync();
            _time.Now = Taipei(2026, 10, 9, 8);               // next morning, before the window opens
            var nextMorning = await service.GetAsync();

            Assert.True(evening.EstimateCorrected);
            Assert.True(nextMorning.EstimateCorrected);
            Assert.Null(nextMorning.Estimate);
        }

        [Fact]
        public async Task OnceTheNextDaysOfficialCloseIsIn_TheOldCorrection_IsNoLongerShown()
        {
            var service = CreateService();
            await TheDayEnds(0.90m, service);

            _time.Now = Taipei(2026, 10, 9, 14, 40);
            StoreHolds(Flat(new DateOnly(2026, 10, 9)));
            _status.RecordCheck(_time.Now);

            Assert.False((await service.GetAsync()).EstimateCorrected);
        }

        [Fact]
        public async Task IfTheRecordIsLost_TheFlagIsQuietlyFalse()
        {
            var service = CreateService();
            _time.Now = Taipei(2026, 10, 8, 13, 20);
            MisSays(0.90m);
            await service.GetAsync();
            _cache.Remove(DrawdownBrakeService.EstimateRecordKey(Thursday));   // a restart, as far as the cache goes

            _time.Now = Taipei(2026, 10, 8, 14, 40);
            StoreHolds(Flat(Thursday));
            _status.RecordCheck(_time.Now);

            Assert.False((await service.GetAsync()).EstimateCorrected);
        }

        [Fact]
        public async Task AnEstimateOfAnotherDay_IsNotHeldAgainstThisDaysClose()
        {
            _cache.Set(DrawdownBrakeService.EstimateRecordKey(Thursday.AddDays(-1)), BrakeState.DEFENSE);
            _time.Now = Taipei(2026, 10, 8, 14, 40);
            StoreHolds(Flat(Thursday));

            Assert.False((await CreateService().GetAsync()).EstimateCorrected);
        }

        [Fact]
        public async Task TheRecord_IsKeptForTwoDays()
        {
            // The cache runs on the system clock, so the clock cannot show this: check what was asked of it.
            var recording = new RecordingCache(_cache);
            var service = new DrawdownBrakeService(_store.Object, _calendar.Object, _status, _quotes.Object, recording, _telemetry, _time);
            MisSays(0.90m);

            await service.GetAsync();

            var record = Assert.Single(recording.Created, e => Equals(e.Key, DrawdownBrakeService.EstimateRecordKey(Thursday)));
            Assert.Equal(TimeSpan.FromDays(2), record.AbsoluteExpirationRelativeToNow);
            Assert.Equal(BrakeState.DEFENSE, record.Value);
        }

        [Fact]
        public async Task OnFridayMorning_ThursdaysCorrection_AndFridaysEstimate_AreShownTogether()
        {
            // The correction is about the official day shown (Thursday); the estimate is about the day
            // after it (Friday), so both can stand on one signal.
            var service = CreateService();
            await TheDayEnds(0.90m, service);

            _time.Now = Taipei(2026, 10, 9, 10);
            _snapshot = new MisQuoteSnapshot(
                new MisIndexQuote(new DateOnly(2026, 10, 9), new DateTimeOffset(2026, 10, 9, 10, 0, 5, TimeSpan.FromHours(8)), 23000m, 23000m),
                _time.Now);
            var friday = await service.GetAsync();

            Assert.True(friday.EstimateCorrected);
            Assert.NotNull(friday.Estimate);
            Assert.Equal(Thursday, friday.AsOfDate);
        }

        /// <summary>Passes everything to a real cache and keeps the entries it was asked to create.</summary>
        private sealed class RecordingCache(IMemoryCache inner) : IMemoryCache
        {
            public List<ICacheEntry> Created { get; } = [];
            public ICacheEntry CreateEntry(object key) { var entry = inner.CreateEntry(key); Created.Add(entry); return entry; }
            public bool TryGetValue(object key, out object? value) => inner.TryGetValue(key, out value);
            public void Remove(object key) => inner.Remove(key);
            public void Dispose() { }
        }

        private sealed class FakeTime : TimeProvider
        {
            public DateTimeOffset Now { get; set; }
            public override DateTimeOffset GetUtcNow() => Now;
        }
    }
}
