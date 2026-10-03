using JackyAIApp.Server.Services.Portfolio;
using JackyAIApp.Server.Tests.TestHelpers;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace JackyAIApp.Server.Tests.Services.Portfolio
{
    /// <summary>
    /// Parses real response shapes captured from Yahoo Finance (events=div) and FundRich's
    /// GetFundDividendHistory (PORTFOLIO_DIVIDEND_PLAN.md section 2) — fixtures only, no network calls.
    /// </summary>
    [Collection(SingleFlightCollection.Name)]
    public class DividendServiceTests
    {
        private static DividendService CreateService(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            var httpClient = StubHttpMessageHandler.CreateClient(responder);
            var httpClientFactory = new Mock<IHttpClientFactory>();
            httpClientFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);
            var cache = new MemoryCache(new MemoryCacheOptions());
            return new DividendService(httpClientFactory.Object, cache, NullLogger<DividendService>.Instance);
        }

        private static HttpResponseMessage JsonResponse(string json) => StubHttpMessageHandler.JsonResponse(json);

        [Fact]
        public async Task GetStockDividendsAsync_UsMarket_ParsesYahooEventsBlock()
        {
            const string json = """
            {"chart":{"result":[{"meta":{"currency":"USD"},"events":{"dividends":{"1754919000":{"amount":0.26,"date":1754919000}}}}]}}
            """;
            var service = CreateService(_ => JsonResponse(json));

            var records = await service.GetStockDividendsAsync("AAPL", "US");

            var record = Assert.Single(records);
            Assert.Equal(0.26m, record.AmountPerUnit);
            Assert.Equal("USD", record.Currency);
            Assert.Equal(
                DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(1754919000).UtcDateTime),
                record.ExDate);
        }

        [Fact]
        public async Task GetStockDividendsAsync_TwMarket_FallsBackToOtcSuffixWhenListedHasNoDividends()
        {
            const string listedEmpty = """{"chart":{"result":[{"meta":{"currency":"TWD"},"events":{"dividends":{}}}]}}""";
            const string otc = """{"chart":{"result":[{"meta":{"currency":"TWD"},"events":{"dividends":{"1700000000":{"amount":2,"date":1700000000}}}}]}}""";

            var service = CreateService(req =>
                JsonResponse(req.RequestUri!.ToString().Contains(".TWO") ? otc : listedEmpty));

            var records = await service.GetStockDividendsAsync("6488", "TW");

            var record = Assert.Single(records);
            Assert.Equal(2m, record.AmountPerUnit);
        }

        [Fact]
        public async Task GetStockDividendsAsync_TwMarket_UsesListedSuffixWhenItHasDividends()
        {
            const string listed = """{"chart":{"result":[{"meta":{"currency":"TWD"},"events":{"dividends":{"1700000000":{"amount":4,"date":1700000000}}}}]}}""";
            var otcCalled = false;

            var service = CreateService(req =>
            {
                if (req.RequestUri!.ToString().Contains(".TWO")) otcCalled = true;
                return JsonResponse(listed);
            });

            var records = await service.GetStockDividendsAsync("2330", "TW");

            Assert.False(otcCalled, "should not fall back to OTC when the listed suffix already has dividends");
            Assert.Equal(4m, Assert.Single(records).AmountPerUnit);
        }

        [Fact]
        public async Task GetFundDividendsAsync_ParsesFundRichHistory()
        {
            const string json = """
            {"data":{"tableRow":{"history":[{"baseDay":"2026/03/10","exDividendDate":"2026/03/06","perUnitPrice":"5.9100","internalRateOfDividend":"2.9529%","currentPeriodDividend":null}]}}}
            """;
            var service = CreateService(_ => JsonResponse(json));

            var records = await service.GetFundDividendsAsync("076009");

            var record = Assert.Single(records);
            Assert.Equal(5.91m, record.AmountPerUnit);
            Assert.Equal(new DateOnly(2026, 3, 6), record.ExDate);
            // No currency field in FundRich's response — caller substitutes the fund's NAV currency.
            Assert.Equal("", record.Currency);
        }

        [Fact]
        public async Task GetFundDividendsAsync_AccumulationFundWithNoHistory_ReturnsEmptyList()
        {
            const string json = """{"data":{"tableRow":{"history":[]}}}""";
            var service = CreateService(_ => JsonResponse(json));

            var records = await service.GetFundDividendsAsync("ACCUM001");

            Assert.Empty(records);
        }

        [Fact]
        public async Task GetStockDividendsAsync_NetworkFailure_ReturnsEmptyListInsteadOfThrowing()
        {
            var service = CreateService(_ => throw new HttpRequestException("network down"));

            var records = await service.GetStockDividendsAsync("AAPL", "US");

            Assert.Empty(records);
        }

        [Fact]
        public async Task GetFundDividendsAsync_NetworkFailure_ReturnsEmptyListInsteadOfThrowing()
        {
            var service = CreateService(_ => throw new HttpRequestException("network down"));

            var records = await service.GetFundDividendsAsync("076009");

            Assert.Empty(records);
        }
    }
}
