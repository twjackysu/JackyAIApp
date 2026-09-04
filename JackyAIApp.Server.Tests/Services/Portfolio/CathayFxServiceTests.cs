using System.Net;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.Data.Models.SQL;
using JackyAIApp.Server.Services.Portfolio;
using JackyAIApp.Server.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace JackyAIApp.Server.Tests.Services.Portfolio
{
    /// <summary>
    /// 國泰世華's rate endpoint is anti-forgery protected and answers a whole date range at once,
    /// business days only. These tests pin the token handshake, the range fill, the carry-forward
    /// over non-business days, and that a lookup asks for the date its PRICE belongs to rather than
    /// today — which is the whole point of using this source (國泰 values at 昨收匯率).
    ///
    /// Payloads are the real shape captured from cathaybk.com.tw.
    /// </summary>
    public class CathayFxServiceTests : IDisposable
    {
        private readonly AzureSQLDBContext _db;

        public CathayFxServiceTests()
        {
            var options = new DbContextOptionsBuilder<AzureSQLDBContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            _db = new AzureSQLDBContext(options);
        }

        public void Dispose() => _db.Dispose();

        private const string BoardPage =
            """<html><body><input name="__RequestVerificationToken" type="hidden" value="tok-abc123" /></body></html>""";

        // Real values for these dates, trimmed to the fields the parser reads.
        private const string RatesJson = """
        [{"Name":"USD (3)","CurDate":"2026/08/26","BuyRate":"31.7300","SellRate":"31.9300"},
         {"Name":"USD (3)","CurDate":"2026/08/27","BuyRate":"31.6500","SellRate":"31.7500"},
         {"Name":"USD (3)","CurDate":"2026/08/28","BuyRate":"31.5200","SellRate":"31.7200"},
         {"Name":"USD (3)","CurDate":"2026/09/03","BuyRate":"31.6800","SellRate":"31.8800"}]
        """;

        [Fact]
        public async Task GetBuyRate_PublishedDate_ReturnsThatDaysBuyRate()
        {
            var service = Create(DefaultResponder());

            // 2026/09/03 is the close 國泰 was pricing against when it showed BRK.B at +127.
            Assert.Equal(31.6800m, await service.GetBuyRateAsync("USD", new DateOnly(2026, 9, 3)));
        }

        [Fact]
        public async Task GetBuyRate_SendsTheAntiForgeryTokenHarvestedFromTheBoardPage()
        {
            // Without the token the endpoint rejects the call, so this is load-bearing rather than
            // incidental — if the page markup changes, this is what breaks first.
            string? postedBody = null;
            var service = Create(req =>
            {
                if (req.Method == HttpMethod.Get) return Html(BoardPage);
                postedBody = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                return StubHttpMessageHandler.JsonResponse(RatesJson);
            });

            await service.GetBuyRateAsync("USD", new DateOnly(2026, 9, 3));

            Assert.Contains("Token=tok-abc123", postedBody);
            Assert.Contains("curr_type=3", postedBody); // 即期, not 現鈔
            Assert.Contains("currency=USD", postedBody);
        }

        [Fact]
        public async Task GetBuyRate_NonBusinessDay_CarriesTheLastPublishedDayForward()
        {
            var service = Create(DefaultResponder());

            // 2026/08/29 and 08/30 are a weekend; the rate that applies is Friday 08/28's.
            Assert.Equal(31.5200m, await service.GetBuyRateAsync("USD", new DateOnly(2026, 8, 30)));
        }

        [Fact]
        public async Task GetBuyRate_PersistsTheWholeRangeSoNeighbouringDatesCostNoFetch()
        {
            var calls = 0;
            var service = Create(req =>
            {
                if (req.Method == HttpMethod.Get) return Html(BoardPage);
                calls++;
                return StubHttpMessageHandler.JsonResponse(RatesJson);
            });

            await service.GetBuyRateAsync("USD", new DateOnly(2026, 9, 3));
            var earlier = await service.GetBuyRateAsync("USD", new DateOnly(2026, 8, 27));

            // One call answers a range, so asking for another day inside it must not refetch.
            Assert.Equal(31.6500m, earlier);
            Assert.Equal(1, calls);
            Assert.Equal(4, await _db.FxRates.CountAsync());
            Assert.All(await _db.FxRates.ToListAsync(), r => Assert.Equal(FxRateSources.Cathay, r.Source));
        }

        [Fact]
        public async Task GetBuyRate_KeepsFourDecimals()
        {
            var service = Create(DefaultResponder());
            await service.GetBuyRateAsync("USD", new DateOnly(2026, 9, 3));

            var row = await _db.FxRates.SingleAsync(r => r.RateDate == new DateOnly(2026, 8, 26));
            Assert.Equal(31.7300m, row.BuyRate);
            Assert.Equal(31.9300m, row.SellRate);
        }

        [Fact]
        public async Task GetBuyRate_NoTokenOnPage_ReturnsNullWithoutPosting()
        {
            var posted = false;
            var service = Create(req =>
            {
                if (req.Method == HttpMethod.Get) return Html("<html><body>markup changed</body></html>");
                posted = true;
                return StubHttpMessageHandler.JsonResponse(RatesJson);
            });

            Assert.Null(await service.GetBuyRateAsync("USD", new DateOnly(2026, 9, 3)));
            Assert.False(posted);
        }

        [Fact]
        public async Task GetBuyRate_SourceUnreachable_ReturnsNullRatherThanThrowing()
        {
            // An FX lookup must never be what stops a portfolio from being valued.
            var service = Create(_ => throw new HttpRequestException("boom"));

            Assert.Null(await service.GetBuyRateAsync("USD", new DateOnly(2026, 9, 3)));
        }

        [Fact]
        public async Task GetBuyRate_DateOlderThanAnythingPublished_ReturnsNullRatherThanGuessing()
        {
            var service = Create(DefaultResponder());

            // Far outside the carry-forward window — stretching a rate that far would be invention.
            Assert.Null(await service.GetBuyRateAsync("USD", new DateOnly(2026, 1, 5)));
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static Func<HttpRequestMessage, HttpResponseMessage> DefaultResponder() => req =>
            req.Method == HttpMethod.Get
                ? Html(BoardPage)
                : StubHttpMessageHandler.JsonResponse(RatesJson);

        private static HttpResponseMessage Html(string html) =>
            new(HttpStatusCode.OK) { Content = new StringContent(html, System.Text.Encoding.UTF8, "text/html") };

        private CathayFxService Create(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            var factory = new Mock<IHttpClientFactory>();
            factory.Setup(f => f.CreateClient(It.IsAny<string>()))
                .Returns(StubHttpMessageHandler.CreateClient(responder));
            return new CathayFxService(
                _db, factory.Object, new MemoryCache(new MemoryCacheOptions()),
                NullLogger<CathayFxService>.Instance);
        }
    }
}
