using System.Net;
using JackyAIApp.Server.Services.Finance.DataProviders.US;
using JackyAIApp.Server.Tests.TestHelpers;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace JackyAIApp.Server.Tests.Services.Finance.US
{
    /// <summary>
    /// Parses real response shapes captured from SEC EDGAR — company_tickers.json (ticker→CIK
    /// lookup) and companyfacts/CIK*.json (XBRL us-gaap facts) — fixtures only, no network calls.
    /// THIRD_PARTY_CONTRACT_TESTS_PLAN.md section B7.
    /// </summary>
    public class SECFundamentalDataProviderTests
    {
        private const string FixtureDir = "Fixtures/SEC";
        private const string AaplCik = "0000320193";

        private static string ReadFixture(string name) =>
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, FixtureDir, name));

        private static SECFundamentalDataProvider CreateProvider(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            var httpClient = StubHttpMessageHandler.CreateClient(responder);
            var httpClientFactory = new Mock<IHttpClientFactory>();
            httpClientFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);
            var cache = new MemoryCache(new MemoryCacheOptions());
            return new SECFundamentalDataProvider(httpClientFactory.Object, cache, NullLogger<SECFundamentalDataProvider>.Instance);
        }

        private static HttpResponseMessage RouteNormal(HttpRequestMessage request)
        {
            var url = request.RequestUri!.ToString();
            if (url.Contains("company_tickers.json")) return StubHttpMessageHandler.JsonResponse(ReadFixture("company_tickers.json"));
            if (url.Contains($"CIK{AaplCik}.json")) return StubHttpMessageHandler.JsonResponse(ReadFixture("companyfacts_aapl.json"));
            throw new InvalidOperationException($"Unexpected URL in test: {url}");
        }

        [Fact]
        public async Task FetchAsync_ResolvesTickerAndParsesUsGaapFacts()
        {
            var provider = CreateProvider(RouteNormal);

            var result = await provider.FetchAsync("AAPL");

            Assert.NotNull(result);
            // Latest single-quarter EPS (end 2026-03-28, 90-day window) from companyfacts_aapl.json fixture.
            Assert.Equal(2.01m, result!.EPS);
            Assert.NotNull(result.EpsDataPeriod);
            // TTM EPS = sum of last 4 single-quarter entries.
            Assert.NotNull(result.TrailingEPS);
            Assert.NotNull(result.TrailingEpsDataPeriod);
            // Latest quarterly revenue with YoY (fixture has a same-quarter-last-year match).
            Assert.NotNull(result.MonthlyRevenue);
            Assert.NotNull(result.RevenueLabel);
            Assert.Equal(29578000000m / 1000, result.NetIncome);
            Assert.Equal(35885000000m / 1000, result.OperatingIncome);
            // BVPS = StockholdersEquity / CommonStockSharesOutstanding, stored temporarily in PBRatio.
            Assert.NotNull(result.PBRatio);
            Assert.True(result.PBRatio > 0);
            // Annualized dividend (quarterly * 4), stored temporarily in DividendYield.
            Assert.Equal(0.26m * 4, result.DividendYield);
            Assert.NotNull(result.FiscalYearQuarter);
        }

        [Fact]
        public async Task FetchAsync_UnknownTicker_ReturnsNullInsteadOfThrowing()
        {
            var provider = CreateProvider(RouteNormal);

            var result = await provider.FetchAsync("NOSUCHTICKER");

            Assert.Null(result);
        }

        [Fact]
        public async Task FetchAsync_FactsMissingUsGaapSection_ReturnsNullInsteadOfThrowing()
        {
            var provider = CreateProvider(req =>
            {
                var url = req.RequestUri!.ToString();
                if (url.Contains("company_tickers.json")) return StubHttpMessageHandler.JsonResponse(ReadFixture("company_tickers.json"));
                if (url.Contains($"CIK{AaplCik}.json")) return StubHttpMessageHandler.JsonResponse("""{"cik":320193,"entityName":"Apple Inc.","facts":{}}""");
                throw new InvalidOperationException($"Unexpected URL: {url}");
            });

            var result = await provider.FetchAsync("AAPL");

            Assert.Null(result);
        }

        [Fact]
        public async Task FetchAsync_TickersHttpFailure_ReturnsNullInsteadOfThrowing()
        {
            var provider = CreateProvider(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

            var result = await provider.FetchAsync("AAPL");

            Assert.Null(result);
        }

        [Fact]
        public async Task FetchAsync_FactsHttpFailure_ReturnsNullInsteadOfThrowing()
        {
            var provider = CreateProvider(req =>
            {
                var url = req.RequestUri!.ToString();
                if (url.Contains("company_tickers.json")) return StubHttpMessageHandler.JsonResponse(ReadFixture("company_tickers.json"));
                return new HttpResponseMessage(HttpStatusCode.InternalServerError);
            });

            var result = await provider.FetchAsync("AAPL");

            Assert.Null(result);
        }

        [Fact]
        public async Task FetchAsync_NetworkFailure_ReturnsNullInsteadOfThrowing()
        {
            var provider = CreateProvider(_ => throw new HttpRequestException("network down"));

            var result = await provider.FetchAsync("AAPL");

            Assert.Null(result);
        }
    }
}
