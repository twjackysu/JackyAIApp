using JackyAIApp.Server.Common;
using JackyAIApp.Server.Services.Market;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using JackyAIApp.Server.Services.ExternalApi.Twse;
using Microsoft.Extensions.Caching.Memory;

namespace JackyAIApp.Server.Tests.Services.Market
{
    /// <summary>The calendar's rules on top of <see cref="ITradingCalendar.IsMarketClosed"/>, the
    /// background load of the schedule, and Taipei time.</summary>
    public class TradingCalendarTests
    {
        /// <summary>Weekends closed, the given holidays closed, every other weekday open.</summary>
        private sealed class Calendar(params DateOnly[] holidays) : ITradingCalendar
        {
            public bool? IsMarketClosed(DateOnly date) =>
                WeekdayTradingCalendar.IsWeekend(date) || holidays.Contains(date);
        }

        // 2026-10-12 is a Monday; 2026-10-09 the Friday before it.
        private static readonly DateOnly Monday = new(2026, 10, 12);
        private static readonly DateOnly Friday = new(2026, 10, 9);

        [Fact]
        public void PreviousTradingDay_SkipsTheWeekend()
        {
            Assert.Equal(Friday, new Calendar().PreviousTradingDay(Monday));
        }

        [Fact]
        public void PreviousTradingDay_SkipsHolidays_AsWellAsTheWeekend()
        {
            // Friday a holiday: the Monday after looks back to Thursday.
            Assert.Equal(Friday.AddDays(-1), new Calendar(Friday).PreviousTradingDay(Monday));
        }

        [Fact]
        public void WithoutASchedule_AWeekdayCountsAsATradingDay()
        {
            Assert.Equal(Friday, WeekdayTradingCalendar.Instance.PreviousTradingDay(Monday));
            Assert.True(WeekdayTradingCalendar.Instance.IsTradingDay(Friday));
            Assert.Null(WeekdayTradingCalendar.Instance.IsMarketClosed(Friday));
            Assert.Equal(true, WeekdayTradingCalendar.Instance.IsMarketClosed(Friday.AddDays(1)));
        }

        [Fact]
        public void TaipeiTime_IsUtcPlusEight_AllYear()
        {
            var winter = new DateTimeOffset(2026, 1, 15, 16, 30, 0, TimeSpan.Zero);
            var summer = new DateTimeOffset(2026, 7, 15, 16, 30, 0, TimeSpan.Zero);

            Assert.Equal(new DateTime(2026, 1, 16, 0, 30, 0), TaipeiTime.ToTaipei(winter));
            Assert.Equal(new DateTime(2026, 7, 16, 0, 30, 0), TaipeiTime.ToTaipei(summer));
            Assert.Equal(new DateOnly(2026, 1, 16), new FixedTime(winter).GetTaipeiToday());
        }

        private sealed class FixedTime(DateTimeOffset now) : TimeProvider
        {
            public DateTimeOffset Now { get; set; } = now;

            public override DateTimeOffset GetUtcNow() => Now;
        }

        private static (TradingCalendarRefreshService Service, Func<int> Fetches, Action<bool> FetchSucceeds, FixedTime Time) Refresher()
        {
            var calls = 0;
            var succeed = true;
            var factory = new Mock<IHttpClientFactory>();
            factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(() =>
                new HttpClient(new TestHelpers.StubHttpMessageHandler(_ =>
                {
                    calls++;
                    return succeed
                        ? TestHelpers.StubHttpMessageHandler.JsonResponse("[{\"Date\":\"1150101\",\"Name\":\"開國紀念日\"}]")
                        : new HttpResponseMessage(System.Net.HttpStatusCode.InternalServerError);
                }), disposeHandler: false));
            var time = new FixedTime(new DateTimeOffset(2026, 10, 12, 0, 0, 0, TimeSpan.Zero));
            var calendar = new TwseTradingCalendar(
                new TwseOpenApi(factory.Object, new MemoryCache(new MemoryCacheOptions()), NullLogger<TwseOpenApi>.Instance, TimeProvider.System), NullLogger<TwseTradingCalendar>.Instance);
            var service = new TradingCalendarRefreshService(calendar, NullLogger<TradingCalendarRefreshService>.Instance, time);
            return (service, () => calls, ok => succeed = ok, time);
        }

        [Fact]
        public async Task Refresh_LoadsOnce_ThenAgainOnlyAfterTwelveHours()
        {
            var (service, fetches, _, time) = Refresher();

            await service.ReloadIfStaleAsync(CancellationToken.None);
            time.Now += TimeSpan.FromHours(11);
            await service.ReloadIfStaleAsync(CancellationToken.None);
            Assert.Equal(1, fetches());

            time.Now += TimeSpan.FromHours(1);
            await service.ReloadIfStaleAsync(CancellationToken.None);
            Assert.Equal(2, fetches());
        }

        [Fact]
        public async Task Refresh_AFailedLoad_IsTriedAgainAtTheNextCheck()
        {
            var (service, fetches, fetchSucceeds, time) = Refresher();
            fetchSucceeds(false);

            await service.ReloadIfStaleAsync(CancellationToken.None);
            time.Now += TimeSpan.FromHours(1);
            fetchSucceeds(true);
            await service.ReloadIfStaleAsync(CancellationToken.None);
            time.Now += TimeSpan.FromHours(1);
            await service.ReloadIfStaleAsync(CancellationToken.None);

            Assert.Equal(2, fetches());
        }
    }
}
