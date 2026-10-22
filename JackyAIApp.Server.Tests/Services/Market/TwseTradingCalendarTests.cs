using System.Net;
using JackyAIApp.Server.Services.Market;
using JackyAIApp.Server.Tests.TestHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using JackyAIApp.Server.Services.ExternalApi.Twse;
using Microsoft.Extensions.Caching.Memory;

namespace JackyAIApp.Server.Tests.Services.Market
{
    /// <summary>
    /// The sample schedule follows the layout we expect from OpenAPI holidaySchedule (民國
    /// yyyMMdd dates, Name/Description text). That layout is an assumption until
    /// LiveContracts/TaiexContractTests has checked it against the live API.
    ///
    /// The calendar is loaded with <see cref="TwseTradingCalendar.RefreshAsync"/> (the background
    /// refresh does that) and answers from memory: asking it never reaches TWSE.
    /// </summary>
    public class TwseTradingCalendarTests
    {
        private const string Schedule =
            "[" +
            "{\"Date\":\"1150101\",\"Name\":\"中華民國開國紀念日\",\"Weekday\":\"四\",\"Description\":\"依規定放假1日。\"}," +
            "{\"Date\":\"1150211\",\"Name\":\"春節前最後交易日\",\"Weekday\":\"三\",\"Description\":\"\"}," +
            "{\"Date\":\"1150212\",\"Name\":\"市場無交易，僅辦理結算交割作業\",\"Weekday\":\"四\",\"Description\":\"\"}," +
            "{\"Date\":\"1150223\",\"Name\":\"春節後開始交易日\",\"Weekday\":\"一\",\"Description\":\"\"}" +
            "]";

        private int _calls;
        private Func<HttpResponseMessage> _respond = () => StubHttpMessageHandler.JsonResponse(Schedule);

        private TwseTradingCalendar CreateCalendar()
        {
            var factory = new Mock<IHttpClientFactory>();
            factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(() =>
                new HttpClient(new StubHttpMessageHandler(_ => { _calls++; return _respond(); }), disposeHandler: false));
            return new TwseTradingCalendar(
                new TwseOpenApi(factory.Object, new MemoryCache(new MemoryCacheOptions()), NullLogger<TwseOpenApi>.Instance, TimeProvider.System), NullLogger<TwseTradingCalendar>.Instance);
        }

        private async Task<TwseTradingCalendar> LoadedCalendar()
        {
            var calendar = CreateCalendar();
            Assert.True(await calendar.RefreshAsync());
            return calendar;
        }

        [Theory]
        [InlineData("2026-01-01", true)]   // a listed holiday
        [InlineData("2026-02-12", true)]   // market closed, settlement only
        [InlineData("2026-02-11", false)]  // listed, but it's the last trading day before the break
        [InlineData("2026-02-23", false)]  // listed, but it's the first trading day after
        [InlineData("2026-03-02", false)]  // not listed: an ordinary trading day
        [InlineData("2026-03-07", true)]   // a Saturday
        public async Task IsMarketClosed_KnownYear(string date, bool closed)
        {
            Assert.Equal(closed, (await LoadedCalendar()).IsMarketClosed(DateOnly.Parse(date)));
        }

        [Fact]
        public async Task AYearTheScheduleDoesNotCover_IsUnknown()
        {
            Assert.Null((await LoadedCalendar()).IsMarketClosed(new DateOnly(2027, 1, 4)));
        }

        [Fact]
        public void BeforeTheFirstLoad_AWeekdayIsUnknown_AndAWeekendIsClosed()
        {
            var calendar = CreateCalendar();

            Assert.Null(calendar.IsMarketClosed(new DateOnly(2026, 1, 1)));
            Assert.True(calendar.IsMarketClosed(new DateOnly(2026, 1, 3)));
            Assert.Equal(0, _calls);
        }

        [Theory]
        [InlineData(HttpStatusCode.InternalServerError)]
        [InlineData(HttpStatusCode.Forbidden)]
        public async Task FetchFails_LoadsNothing(HttpStatusCode status)
        {
            _respond = () => new HttpResponseMessage(status);
            var calendar = CreateCalendar();

            Assert.False(await calendar.RefreshAsync());
            Assert.Null(calendar.IsMarketClosed(new DateOnly(2026, 1, 1)));
        }

        [Theory]
        [InlineData("<html>maintenance</html>")]
        [InlineData("{\"not\":\"an array\"}")]
        [InlineData("[]")]
        [InlineData("[{\"Name\":\"no date here\"}]")]
        public async Task UnreadableSchedule_LoadsNothing(string body)
        {
            _respond = () => StubHttpMessageHandler.JsonResponse(body);
            var calendar = CreateCalendar();

            Assert.False(await calendar.RefreshAsync());
            Assert.Null(calendar.IsMarketClosed(new DateOnly(2026, 1, 1)));
        }

        [Fact]
        public async Task Asking_NeverFetches()
        {
            var calendar = await LoadedCalendar();

            calendar.IsMarketClosed(new DateOnly(2026, 1, 1));
            calendar.IsMarketClosed(new DateOnly(2026, 3, 2));
            calendar.IsMarketClosed(new DateOnly(2027, 1, 4));

            Assert.Equal(1, _calls);
        }

        [Fact]
        public async Task AFailedReload_KeepsTheScheduleItHas()
        {
            var calendar = await LoadedCalendar();
            _respond = () => new HttpResponseMessage(HttpStatusCode.InternalServerError);

            Assert.False(await calendar.RefreshAsync());

            Assert.Equal(true, calendar.IsMarketClosed(new DateOnly(2026, 1, 1)));
        }

        [Theory]
        [InlineData("1150101", 2026, 1, 1)]
        [InlineData("20260101", 2026, 1, 1)]
        [InlineData("115/01/01", 2026, 1, 1)]
        [InlineData("2026/01/01", 2026, 1, 1)]
        [InlineData(" 1150101 ", 2026, 1, 1)]
        [InlineData("1150230", 0, 0, 0)]
        [InlineData("not a date", 0, 0, 0)]
        [InlineData("", 0, 0, 0)]
        public void TryParseDate_Cases(string text, int year, int month, int day)
        {
            var ok = TwseTradingCalendar.TryParseDate(text, out var date);

            Assert.Equal(year != 0, ok);
            if (ok) Assert.Equal(new DateOnly(year, month, day), date);
        }
    }
}
