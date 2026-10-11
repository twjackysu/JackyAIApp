using System.Globalization;
using System.Net;
using System.Text;
using JackyAIApp.Server.DTO.Finance;
using JackyAIApp.Server.Services.Finance.DataProviders;
using JackyAIApp.Server.Tests.TestHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace JackyAIApp.Server.Tests.Services.Finance
{
    /// <summary>
    /// <see cref="MarketIndexHistoryProvider"/>: turning Yahoo's <c>^TWII</c> chart JSON into the
    /// series — cleaning, the "today's bar" rule, and the sanity checks that reject a whole series.
    /// Offline: the JSON is built to Yahoo's documented layout; the real layout is checked by
    /// <c>YahooChartContractTests</c>.
    /// </summary>
    public class MarketIndexHistoryProviderTests
    {
        // 2026-10-08 is a Thursday. Taipei is UTC+8 all year.
        private static readonly DateOnly Thu = new(2026, 10, 8);
        private static readonly DateOnly Fri = new(2026, 10, 9);

        /// <summary>A UTC instant given as Taipei wall-clock time.</summary>
        private static DateTimeOffset Taipei(int y, int mo, int d, int h, int mi = 0) =>
            new DateTimeOffset(y, mo, d, h, mi, 0, TimeSpan.FromHours(8)).ToUniversalTime();

        // ---- JSON building -------------------------------------------------------------------

        /// <summary>Weekdays only, oldest first, ending on <paramref name="last"/> (must be a weekday).</summary>
        private static List<DateOnly> WeekdaysEndingOn(DateOnly last, int count)
        {
            var days = new List<DateOnly>();
            for (var d = last; days.Count < count; d = d.AddDays(-1))
                if (d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)) days.Add(d);
            days.Reverse();
            return days;
        }

        /// <summary>A gently rising series with a small wiggle — no day moves anywhere near the 15% limit.</summary>
        private static double Level(int i) => 15000 + i * 3 + Math.Sin(i / 5.0) * 80;

        /// <summary>Yahoo stamps a TAIEX bar at the 09:00 Taipei open, which is 01:00 UTC the same date.</summary>
        private static long OpenStamp(DateOnly date) =>
            new DateTimeOffset(date.Year, date.Month, date.Day, 1, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();

        private static string Chart(IEnumerable<(long Stamp, double? Close)> bars)
        {
            var list = bars.ToList();
            var stamps = string.Join(",", list.Select(b => b.Stamp.ToString(CultureInfo.InvariantCulture)));
            var closes = string.Join(",", list.Select(b => b.Close is { } c ? c.ToString("R", CultureInfo.InvariantCulture) : "null"));
            return "{\"chart\":{\"result\":[{\"meta\":{\"symbol\":\"^TWII\"}," +
                   $"\"timestamp\":[{stamps}],\"indicators\":{{\"quote\":[{{\"close\":[{closes}]}}]}}}}],\"error\":null}}}}";
        }

        private static string Chart(IEnumerable<DateOnly> days, Func<int, double?>? close = null)
        {
            var closeAt = close ?? (i => Level(i));
            return Chart(days.Select((d, i) => (OpenStamp(d), closeAt(i))));
        }

        /// <summary>Enough clean history to pass every check, the newest day being <paramref name="last"/>.</summary>
        private static string ValidChart(DateOnly last, int count = 1450, Func<int, double?>? close = null) =>
            Chart(WeekdaysEndingOn(last, count), close);

        private static (MarketIndexHistoryResponse? History, string? Rejection) ParseAt(string json, DateTimeOffset now) =>
            MarketIndexHistoryProvider.Parse(json, now);

        // After the 14:00 close on Thursday 2026-10-08 — so Thursday's own bar is final.
        private static readonly DateTimeOffset ThuEvening = Taipei(2026, 10, 8, 18);

        // ---- Parsing ---------------------------------------------------------------------------

        [Fact]
        public void ValidSeries_IsReturnedAscending_WithTheDocumentedFields()
        {
            var (history, rejection) = ParseAt(ValidChart(Thu), ThuEvening);

            Assert.Null(rejection);
            Assert.NotNull(history);
            Assert.Equal("^TWII", history.Symbol);
            Assert.Equal("加權指數（價格指數，不含息）", history.Name);
            Assert.Equal("Yahoo Finance", history.Source);
            Assert.Equal(Thu, history.AsOf);
            Assert.Equal(1450, history.Points.Count);
            Assert.Equal(Thu, history.Points[^1].Date);
            Assert.True(history.Points.Zip(history.Points.Skip(1), (a, b) => a.Date < b.Date).All(x => x),
                "dates must be strictly ascending");
            Assert.All(history.Points, p => Assert.True(p.Close > 0));
        }

        [Fact]
        public void Closes_AreRoundedToTwoDecimals()
        {
            // Yahoo sends float noise such as 17754.490234375.
            var (history, _) = ParseAt(
                ValidChart(Thu, close: i => i == 1449 ? 17754.490234375 : Level(i)), ThuEvening);

            Assert.Equal(17754.49m, history!.Points[^1].Close);
        }

        [Fact]
        public void DaysWithoutAUsableClose_AreSkipped_NotFailed()
        {
            // null, zero, negative, and an absurd value each lose that one day; nothing else is touched.
            var bad = new Dictionary<int, double?> { [10] = null, [20] = 0, [30] = -5, [40] = 1e300 };
            var (history, rejection) = ParseAt(
                ValidChart(Thu, close: i => bad.TryGetValue(i, out var v) ? v : Level(i)), ThuEvening);

            Assert.Null(rejection);
            Assert.Equal(1450 - bad.Count, history!.Points.Count);
            var days = WeekdaysEndingOn(Thu, 1450);
            foreach (var i in bad.Keys)
                Assert.DoesNotContain(history.Points, p => p.Date == days[i]);
        }

        [Fact]
        public void ACloseThatRoundsToZero_IsSkipped()
        {
            var (history, _) = ParseAt(
                ValidChart(Thu, close: i => i == 50 ? 0.001 : Level(i)), ThuEvening);

            Assert.Equal(1449, history!.Points.Count);
        }

        [Fact]
        public void SameDateTwice_KeepsTheLaterStampedBar_InEitherOrder()
        {
            var days = WeekdaysEndingOn(Thu, 1450);
            var earlier = (OpenStamp(Thu), Close: (double?)20000.00);
            var later = (OpenStamp(Thu) + 3600, Close: (double?)20100.00); // same Taipei date, an hour on

            foreach (var duplicatesOrdered in new[] { new[] { earlier, later }, new[] { later, earlier } })
            {
                var bars = days.Take(1449).Select((d, i) => (OpenStamp(d), (double?)Level(i))).Concat(duplicatesOrdered);
                var (history, rejection) = ParseAt(Chart(bars), ThuEvening);

                Assert.Null(rejection);
                Assert.Equal(1450, history!.Points.Count);
                Assert.Equal(20100.00m, history.Points[^1].Close);
            }
        }

        [Fact]
        public void UnorderedInput_IsSorted()
        {
            var days = WeekdaysEndingOn(Thu, 1450);
            var bars = days.Select((d, i) => (OpenStamp(d), (double?)Level(i)))
                           .OrderBy(b => (b.Item1 * 2654435761L) % 1009) // a fixed shuffle
                           .ToList();
            Assert.NotEqual(days.Select(OpenStamp), bars.Select(b => b.Item1)); // really shuffled

            var (history, rejection) = ParseAt(Chart(bars), ThuEvening);

            Assert.Null(rejection);
            Assert.Equal(days, history!.Points.Select(p => p.Date));
        }

        [Fact]
        public void DatesAreTheTaipeiDate_NotTheUtcDate()
        {
            // Every bar stamped at 00:00 Taipei = 16:00 UTC the day before. Read as UTC dates they'd all
            // land one day early (a Monday bar on Sunday), so AsOf and the weekday pattern would be off.
            var days = WeekdaysEndingOn(Thu, 1450);
            var bars = days.Select((d, i) => (OpenStamp(d) - 9 * 3600, (double?)Level(i)));

            var (history, rejection) = ParseAt(Chart(bars), ThuEvening);

            Assert.Null(rejection);
            Assert.Equal(days, history!.Points.Select(p => p.Date));
            Assert.Equal(Thu, history.AsOf);
        }

        // ---- Today's bar -----------------------------------------------------------------------

        public static TheoryData<string, DateTimeOffset, bool> TodaysBarCases() => new()
        {
            { "Thu 08:30 — market just opened, forming", Taipei(2026, 10, 8, 8, 30), false },
            { "Thu 13:59 — one minute before final",     Taipei(2026, 10, 8, 13, 59), false },
            { "Thu 14:00 — exactly final",               Taipei(2026, 10, 8, 14, 0), true },
            { "Thu 18:00 — long after",                  Taipei(2026, 10, 8, 18, 0), true },
        };

        [Theory]
        [MemberData(nameof(TodaysBarCases))]
        public void TodaysBar_IsKeptOnlyFromFinalTime_OnATradingDay(string _, DateTimeOffset now, bool kept)
        {
            var (history, rejection) = ParseAt(ValidChart(Thu), now);

            Assert.Null(rejection);
            Assert.Equal(kept ? Thu : Thu.AddDays(-1), history!.AsOf);
            Assert.Equal(kept ? 1450 : 1449, history.Points.Count);
        }

        [Fact]
        public void TodaysBar_IsDroppedOnAWeekend_AtAnyTime()
        {
            // Saturday 2026-10-10 evening, with a bar (Yahoo should not, but might) dated Saturday.
            var sat = new DateOnly(2026, 10, 10);
            var days = WeekdaysEndingOn(Fri, 1449);
            var bars = days.Select((d, i) => (OpenStamp(d), (double?)Level(i)))
                           .Append((OpenStamp(sat), (double?)Level(1449)));

            var (history, rejection) = ParseAt(Chart(bars), Taipei(2026, 10, 10, 18));

            Assert.Null(rejection);
            Assert.Equal(Fri, history!.AsOf);
            Assert.DoesNotContain(history.Points, p => p.Date == sat);
        }

        [Fact]
        public void YesterdaysBar_IsKept_EvenBeforeTodaysFinalTime()
        {
            // Friday 09:00 Taipei: Thursday's close is long settled; Friday has no bar yet.
            var (history, rejection) = ParseAt(ValidChart(Thu), Taipei(2026, 10, 9, 9));

            Assert.Null(rejection);
            Assert.Equal(Thu, history!.AsOf);
        }

        [Fact]
        public void TaipeiCalendarDecidesWhatIsToday_NotUtc()
        {
            // 17:00 UTC Thursday is 01:00 Friday in Taipei: "today" is Friday, before its close.
            // Friday's forming bar goes; Thursday's settled bar stays.
            var days = WeekdaysEndingOn(Fri, 1450);
            var now = new DateTimeOffset(2026, 10, 8, 17, 0, 0, TimeSpan.Zero);

            var (history, rejection) = ParseAt(Chart(days), now);

            Assert.Null(rejection);
            Assert.Equal(Thu, history!.AsOf);
        }

        // ---- Sanity checks: a failing series is rejected whole --------------------------------

        [Fact]
        public void PointCount_BoundaryIs1000()
        {
            Assert.NotNull(ParseAt(ValidChart(Thu, count: 1000), ThuEvening).History);

            var (history, rejection) = ParseAt(ValidChart(Thu, count: 999), ThuEvening);
            Assert.Null(history);
            Assert.Contains("999", rejection);
        }

        [Fact]
        public void TooOldANewestPoint_IsRejected_BoundaryIs10Days()
        {
            // Newest point 10 days before "today" (Taipei) passes; 11 fails.
            var today = new DateOnly(2026, 10, 19);                      // Monday
            var now = Taipei(2026, 10, 19, 18);
            Assert.NotNull(ParseAt(ValidChart(today.AddDays(-10)), now).History); // Friday 10-09 -> 10 days

            var (history, rejection) = ParseAt(ValidChart(Thu), now);             // Thursday 10-08 -> 11 days
            Assert.Null(history);
            Assert.Contains("days before today", rejection);
        }

        [Theory]
        [InlineData(0.20)]
        [InlineData(-0.20)]
        public void ASingleDayMoveOver15Percent_RejectsTheWholeSeries(double move)
        {
            var (history, rejection) = ParseAt(
                ValidChart(Thu, close: i => i < 700 ? Level(i) : Level(i) * (1 + move)), ThuEvening);

            Assert.Null(history);
            Assert.Contains("one day", rejection);
        }

        [Theory]
        [InlineData(0.097)]   // the TAIEX's real worst day (2025-04-07)
        [InlineData(-0.097)]
        [InlineData(0.10)]    // the exchange's daily limit
        [InlineData(0.149)]
        public void RealisticallyLargeMoves_AreAccepted(double move)
        {
            var (history, rejection) = ParseAt(
                ValidChart(Thu, close: i => i < 700 ? Level(i) : Level(i) * (1 + move)), ThuEvening);

            Assert.Null(rejection);
            Assert.NotNull(history);
        }

        [Fact]
        public void ABadJump_IsNeverPatchedAround()
        {
            // The caller gets nothing rather than "everything except the bad day".
            var (history, _) = ParseAt(
                ValidChart(Thu, close: i => i == 900 ? 1e6 : Level(i)), ThuEvening);

            Assert.Null(history);
        }

        // ---- Malformed responses ---------------------------------------------------------------

        [Theory]
        [InlineData("")]
        [InlineData("<html>Too Many Requests</html>")]
        [InlineData("null")]
        [InlineData("[]")]
        [InlineData("{}")]
        [InlineData("{\"chart\":{}}")]
        [InlineData("{\"chart\":{\"result\":null,\"error\":{\"code\":\"Not Found\",\"description\":\"No data found\"}}}")]
        [InlineData("{\"chart\":{\"result\":[]}}")]
        [InlineData("{\"chart\":{\"result\":[{\"meta\":{}}]}}")]
        [InlineData("{\"chart\":{\"result\":[{\"timestamp\":[1],\"indicators\":{\"quote\":[]}}]}}")]
        [InlineData("{\"chart\":{\"result\":[{\"timestamp\":[1],\"indicators\":{\"quote\":[{\"open\":[1]}]}}]}}")]
        public void MalformedOrErrorResponses_AreRejected_NotThrown(string body)
        {
            var (history, rejection) = ParseAt(body, ThuEvening);

            Assert.Null(history);
            Assert.False(string.IsNullOrEmpty(rejection));
        }

        [Fact]
        public void TimestampAndCloseArraysOfDifferentLength_AreRejected()
        {
            var json = "{\"chart\":{\"result\":[{\"timestamp\":[1,2,3],\"indicators\":{\"quote\":[{\"close\":[1,2]}]}}]}}";

            var (history, rejection) = ParseAt(json, ThuEvening);

            Assert.Null(history);
            Assert.Contains("close[]", rejection);
        }

        // ---- The HTTP call ---------------------------------------------------------------------

        private static MarketIndexHistoryProvider ProviderFor(
            Func<HttpRequestMessage, HttpResponseMessage> responder, TimeProvider? time = null, Action<string>? onClient = null)
        {
            var factory = new Mock<IHttpClientFactory>();
            factory.Setup(f => f.CreateClient(It.IsAny<string>()))
                   .Returns<string>(name =>
                   {
                       onClient?.Invoke(name);
                       return StubHttpMessageHandler.CreateClient(responder);
                   });
            return new MarketIndexHistoryProvider(
                factory.Object, NullLogger<MarketIndexHistoryProvider>.Instance, time ?? new FixedTime(ThuEvening));
        }

        [Fact]
        public async Task Get_RequestsSixYearsOfDailyBars_ThroughTheReadOnlyClient()
        {
            HttpRequestMessage? sent = null;
            string? clientName = null;
            var provider = ProviderFor(r =>
            {
                sent = r;
                return StubHttpMessageHandler.JsonResponse(ValidChart(Thu));
            }, onClient: n => clientName = n);

            var history = await provider.GetAsync();

            Assert.NotNull(history);
            Assert.Equal("ExternalReadOnly", clientName);
            Assert.Equal(HttpMethod.Get, sent!.Method);
            Assert.Equal("https://query1.finance.yahoo.com/v8/finance/chart/%5ETWII?range=6y&interval=1d", sent.RequestUri!.OriginalString);
            Assert.True(sent.Headers.Contains("User-Agent"), "Yahoo rejects requests with no User-Agent");
        }

        [Fact]
        public async Task Get_ANonSuccessStatus_IsNull()
        {
            var provider = ProviderFor(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests));

            Assert.Null(await provider.GetAsync());
        }

        [Fact]
        public async Task Get_AnInvalidSeries_IsNull()
        {
            var provider = ProviderFor(_ => StubHttpMessageHandler.JsonResponse(ValidChart(Thu, count: 10)));

            Assert.Null(await provider.GetAsync());
        }

        [Fact]
        public async Task Get_ANetworkFailure_IsNull_NotAnException()
        {
            var provider = ProviderFor(_ => throw new HttpRequestException("connection reset"));

            Assert.Null(await provider.GetAsync());
        }

        [Fact]
        public async Task Get_ATimeout_IsNull()
        {
            // HttpClient reports its own timeout as a TaskCanceledException the caller never asked for.
            var provider = ProviderFor(_ => throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout"));

            Assert.Null(await provider.GetAsync(CancellationToken.None));
        }

        [Fact]
        public async Task Get_ACallerWhoCancels_GetsTheCancellation()
        {
            using var cts = new CancellationTokenSource();
            var provider = ProviderFor(_ =>
            {
                cts.Cancel();
                throw new OperationCanceledException(cts.Token);
            });

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.GetAsync(cts.Token));
        }

        [Fact]
        public void TheUrl_AsksForTheIndexOverSixYearsOfDays()
        {
            Assert.Contains("%5ETWII", MarketIndexHistoryProvider.Url);
            Assert.Contains("range=6y", MarketIndexHistoryProvider.Url);
            Assert.Contains("interval=1d", MarketIndexHistoryProvider.Url);
        }

        private sealed class FixedTime(DateTimeOffset now) : TimeProvider
        {
            public override DateTimeOffset GetUtcNow() => now;
        }
    }
}
