using System.Net;
using JackyAIApp.Server.Services.Portfolio.DrawdownBrake;
using JackyAIApp.Server.Tests.TestHelpers;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace JackyAIApp.Server.Tests.Services.Portfolio.DrawdownBrake
{
    /// <summary>
    /// The sample schedule follows the layout we expect from OpenAPI holidaySchedule (民國
    /// yyyMMdd dates, Name/Description text). That layout is an assumption until
    /// LiveContracts/TaiexContractTests has checked it against the live API.
    /// </summary>
    public class TwseHolidayCalendarTests
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

        private TwseHolidayCalendar CreateCalendar()
        {
            var factory = new Mock<IHttpClientFactory>();
            factory.Setup(f => f.CreateClient("ExternalReadOnly")).Returns(() =>
                new HttpClient(new StubHttpMessageHandler(_ => { _calls++; return _respond(); }), disposeHandler: false));
            return new TwseHolidayCalendar(factory.Object, new MemoryCache(new MemoryCacheOptions()), NullLogger<TwseHolidayCalendar>.Instance);
        }

        [Theory]
        [InlineData("2026-01-01", true)]   // a listed holiday
        [InlineData("2026-02-12", true)]   // market closed, settlement only
        [InlineData("2026-02-11", false)]  // listed, but it's the last trading day before the break
        [InlineData("2026-02-23", false)]  // listed, but it's the first trading day after
        [InlineData("2026-03-02", false)]  // not listed: an ordinary trading day
        public async Task IsMarketClosedAsync_KnownYear(string date, bool closed)
        {
            Assert.Equal(closed, await CreateCalendar().IsMarketClosedAsync(DateOnly.Parse(date)));
        }

        [Fact]
        public async Task IsMarketClosedAsync_AYearTheScheduleDoesNotCover_IsUnknown()
        {
            Assert.Null(await CreateCalendar().IsMarketClosedAsync(new DateOnly(2027, 1, 4)));
        }

        [Theory]
        [InlineData(HttpStatusCode.InternalServerError)]
        [InlineData(HttpStatusCode.Forbidden)]
        public async Task IsMarketClosedAsync_FetchFails_IsUnknown(HttpStatusCode status)
        {
            _respond = () => new HttpResponseMessage(status);

            Assert.Null(await CreateCalendar().IsMarketClosedAsync(new DateOnly(2026, 1, 1)));
        }

        [Theory]
        [InlineData("<html>maintenance</html>")]
        [InlineData("{\"not\":\"an array\"}")]
        [InlineData("[]")]
        [InlineData("[{\"Name\":\"no date here\"}]")]
        public async Task IsMarketClosedAsync_UnreadableSchedule_IsUnknown(string body)
        {
            _respond = () => StubHttpMessageHandler.JsonResponse(body);

            Assert.Null(await CreateCalendar().IsMarketClosedAsync(new DateOnly(2026, 1, 1)));
        }

        [Fact]
        public async Task TheScheduleIsFetchedOnce_ThenServedFromMemory()
        {
            var calendar = CreateCalendar();

            await calendar.IsMarketClosedAsync(new DateOnly(2026, 1, 1));
            await calendar.IsMarketClosedAsync(new DateOnly(2026, 3, 2));
            await calendar.IsMarketClosedAsync(new DateOnly(2027, 1, 4));

            Assert.Equal(1, _calls);
        }

        [Fact]
        public async Task AFailedFetch_IsNotRetriedOnEveryCall()
        {
            _respond = () => new HttpResponseMessage(HttpStatusCode.InternalServerError);
            var calendar = CreateCalendar();

            await calendar.IsMarketClosedAsync(new DateOnly(2026, 1, 1));
            await calendar.IsMarketClosedAsync(new DateOnly(2026, 1, 2));

            Assert.Equal(1, _calls);
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
            var ok = TwseHolidayCalendar.TryParseDate(text, out var date);

            Assert.Equal(year != 0, ok);
            if (ok) Assert.Equal(new DateOnly(year, month, day), date);
        }
    }
}
