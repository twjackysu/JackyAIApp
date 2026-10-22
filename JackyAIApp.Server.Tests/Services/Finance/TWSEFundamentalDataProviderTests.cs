using System.Net;
using JackyAIApp.Server.Services.ExternalApi.Twse;
using JackyAIApp.Server.Services.Finance.DataProviders;
using JackyAIApp.Server.Tests.TestHelpers;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace JackyAIApp.Server.Tests.Services.Finance
{
    /// <summary>
    /// Parses real response shapes captured from TWSE OpenAPI's fundamentals endpoints (BWIBBU_d,
    /// t187ap05_L revenue, t187ap14_L EPS, STOCK_DAY_ALL) — fixtures only, no network calls.
    /// THIRD_PARTY_CONTRACT_TESTS_PLAN.md section B1. These endpoints return Chinese field names
    /// that are the most likely thing to silently break on a TWSE format change.
    /// </summary>
    public class TWSEFundamentalDataProviderTests
    {
        private const string FixtureDir = "Fixtures/TWSE";

        private static string ReadFixture(string name) =>
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, FixtureDir, name));

        private static TWSEFundamentalDataProvider CreateProvider(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            var httpClient = StubHttpMessageHandler.CreateClient(responder);
            var httpClientFactory = new Mock<IHttpClientFactory>();
            httpClientFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);
            var cache = new MemoryCache(new MemoryCacheOptions());
            return new TWSEFundamentalDataProvider(NullLogger<TWSEFundamentalDataProvider>.Instance,
                new TwseOpenApi(httpClientFactory.Object, cache, NullLogger<TwseOpenApi>.Instance, TimeProvider.System));
        }

        private static HttpResponseMessage Route(HttpRequestMessage request)
        {
            var url = request.RequestUri!.ToString();
            if (url.Contains("BWIBBU_d")) return StubHttpMessageHandler.JsonResponse(ReadFixture("bwibbu_d.json"));
            if (url.Contains("t187ap05_L")) return StubHttpMessageHandler.JsonResponse(ReadFixture("revenue_t187ap05_l.json"));
            if (url.Contains("t187ap14_L")) return StubHttpMessageHandler.JsonResponse(ReadFixture("eps_t187ap14_l.json"));
            if (url.Contains("STOCK_DAY_ALL")) return StubHttpMessageHandler.JsonResponse(ReadFixture("stock_day_all.json"));
            throw new InvalidOperationException($"Unexpected URL in test: {url}");
        }

        [Fact]
        public async Task FetchAsync_KnownStock_ParsesAllFourSources()
        {
            var provider = CreateProvider(Route);

            var result = await provider.FetchAsync("2330");

            Assert.NotNull(result);
            Assert.Equal(32.80m, result!.PERatio);
            Assert.Equal(10.74m, result.PBRatio);
            Assert.Equal(0.90m, result.DividendYield);
            Assert.Equal("2026Q1", result.FiscalYearQuarter);

            Assert.Equal(416975163m, result.MonthlyRevenue);
            Assert.Equal(30.09498020271696m, result.RevenueYoY);
            Assert.Equal(1.5217099529812539m, result.RevenueMoM);
            Assert.Equal("11505", result.RevenueMonth);
            Assert.NotNull(result.RevenueLabel);

            Assert.Equal(22.08m, result.EPS);
            Assert.Equal(658966142m, result.OperatingIncome);
            Assert.Equal(572801304m, result.NetIncome);
            Assert.Equal("115年Q1", result.EpsDataPeriod);
        }

        [Fact]
        public async Task FetchAsync_BlankPeRatioField_ParsesAsNullWithoutThrowing()
        {
            // The 1101 fixture entry has PEratio: "" — TWSE leaves it blank when a company has no
            // trailing-12-month earnings to compute a P/E off of.
            var provider = CreateProvider(Route);

            var result = await provider.FetchAsync("1101");

            Assert.NotNull(result);
            Assert.Null(result!.PERatio);
            Assert.Equal(0.75m, result.PBRatio);
        }

        [Fact]
        public async Task FetchAsync_StockNotInAnySource_ReturnsNull()
        {
            var provider = CreateProvider(Route);

            var result = await provider.FetchAsync("9999");

            Assert.Null(result);
        }

        [Fact]
        public async Task FetchAsync_HttpFailure_ReturnsNullInsteadOfThrowing()
        {
            var provider = CreateProvider(_ => StubHttpMessageHandler.JsonResponse("", HttpStatusCode.InternalServerError));

            var result = await provider.FetchAsync("2330");

            Assert.Null(result);
        }

        [Fact]
        public async Task FetchAsync_NetworkFailure_ReturnsNullInsteadOfThrowing()
        {
            var provider = CreateProvider(_ => throw new HttpRequestException("network down"));

            var result = await provider.FetchAsync("2330");

            Assert.Null(result);
        }
    }
}
