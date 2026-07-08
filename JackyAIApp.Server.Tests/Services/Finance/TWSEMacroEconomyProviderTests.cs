using System.Net;
using JackyAIApp.Server.Services.Finance.DataProviders;
using JackyAIApp.Server.Tests.TestHelpers;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace JackyAIApp.Server.Tests.Services.Finance
{
    /// <summary>
    /// Parses real response shapes captured from TWSE OpenAPI's market-wide endpoints (FMTQIK,
    /// MI_INDEX, MI_MARGN) — fixtures only, no network calls. THIRD_PARTY_CONTRACT_TESTS_PLAN.md
    /// section B2.
    ///
    /// TWSEMacroEconomyProvider.FetchAsync also fetches the Bank of Taiwan FX CSV and CBC rate
    /// CSV (B5/B6) in the same Task.WhenAll — those are out of scope for Phase 2 (Phase 3), so
    /// this test class stubs those two URLs with an innocuous empty response and asserts nothing
    /// about ExchangeRates/BankRate.
    /// </summary>
    public class TWSEMacroEconomyProviderTests
    {
        private const string FixtureDir = "Fixtures/TWSE";

        private static string ReadFixture(string name) =>
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, FixtureDir, name));

        private static TWSEMacroEconomyProvider CreateProvider(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            var httpClient = StubHttpMessageHandler.CreateClient(responder);
            var httpClientFactory = new Mock<IHttpClientFactory>();
            httpClientFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);
            var cache = new MemoryCache(new MemoryCacheOptions());
            return new TWSEMacroEconomyProvider(httpClientFactory.Object, cache, NullLogger<TWSEMacroEconomyProvider>.Instance);
        }

        private static HttpResponseMessage RouteNormal(HttpRequestMessage request)
        {
            var url = request.RequestUri!.ToString();
            if (url.Contains("FMTQIK")) return StubHttpMessageHandler.JsonResponse(ReadFixture("fmtqik.json"));
            if (url.Contains("MI_INDEX")) return StubHttpMessageHandler.JsonResponse(ReadFixture("mi_index.json"));
            if (url.Contains("MI_MARGN")) return StubHttpMessageHandler.JsonResponse(ReadFixture("mi_margn.json"));
            // Out-of-scope B5/B6 CSV endpoints — return an innocuous empty body so FetchAsync's
            // Task.WhenAll doesn't blow up; not asserted on.
            if (url.Contains("rate.bot.com.tw") || url.Contains("cbc.gov.tw"))
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("") };
            throw new InvalidOperationException($"Unexpected URL in test: {url}");
        }

        [Fact]
        public async Task FetchAsync_ParsesMarketIndexSectorIndicesAndMarginSummary()
        {
            var provider = CreateProvider(RouteNormal);

            var result = await provider.FetchAsync();

            Assert.Equal(4, result.MarketIndex.Count);
            var first = result.MarketIndex[0];
            Assert.Equal("1150701", first.Date);
            Assert.Equal(47018.99m, first.TAIEX);
            Assert.Equal(893.08m, first.Change);
            Assert.Equal(14683404939L, first.TradeVolume);
            Assert.Equal(1367817795171L, first.TradeValue);
            Assert.Equal(6457744L, first.Transaction);

            // Fixture includes a non-key sector ("寶島股價指數") that must be filtered out, plus
            // several key sectors that must survive.
            Assert.Contains(result.SectorIndices, s => s.Name == "發行量加權股價指數");
            Assert.Contains(result.SectorIndices, s => s.Name == "半導體類指數");
            Assert.DoesNotContain(result.SectorIndices, s => s.Name == "寶島股價指數");
            var weighted = result.SectorIndices.Single(s => s.Name == "發行量加權股價指數");
            Assert.Equal(45479.11m, weighted.CloseIndex);
            Assert.Equal(1077.28m, weighted.ChangePoints); // comma-formatted "1,077.28" must parse
            Assert.Equal(-2.31m, weighted.ChangePercent);

            Assert.NotNull(result.Margin);
            // mi_margn.json fixture has 4 rows; MarginBuyTotal should be the sum of 融資買進 across all.
            var expectedMarginBuyTotal = 2411 + 16 + 13 + 13038;
            Assert.Equal(expectedMarginBuyTotal, result.Margin!.MarginBuyTotal);
        }

        [Fact]
        public async Task FetchAsync_MissingNumericFields_DefaultToZeroInsteadOfThrowing()
        {
            const string fmtqikMissingFields = """[{"Date":"1150701"}]""";
            const string miIndexMissingFields = """[{"指數":"發行量加權股價指數"}]""";
            const string miMargnMissingFields = """[{"股票代號":"2330"}]""";

            var provider = CreateProvider(req =>
            {
                var url = req.RequestUri!.ToString();
                if (url.Contains("FMTQIK")) return StubHttpMessageHandler.JsonResponse(fmtqikMissingFields);
                if (url.Contains("MI_INDEX")) return StubHttpMessageHandler.JsonResponse(miIndexMissingFields);
                if (url.Contains("MI_MARGN")) return StubHttpMessageHandler.JsonResponse(miMargnMissingFields);
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("") };
            });

            var result = await provider.FetchAsync();

            var day = Assert.Single(result.MarketIndex);
            Assert.Equal(0m, day.TAIEX);
            Assert.Equal(0L, day.TradeVolume);

            var sector = Assert.Single(result.SectorIndices);
            Assert.Equal(0m, sector.CloseIndex);

            Assert.NotNull(result.Margin);
            Assert.Equal(0L, result.Margin!.MarginBuyTotal);
        }

        [Fact]
        public async Task FetchAsync_HttpFailure_DegradesToEmptyResultsInsteadOfThrowing()
        {
            var provider = CreateProvider(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

            var result = await provider.FetchAsync();

            Assert.Empty(result.MarketIndex);
            Assert.Empty(result.SectorIndices);
            Assert.Null(result.Margin);
        }

        [Fact]
        public async Task FetchAsync_NetworkFailure_DegradesToEmptyResultsInsteadOfThrowing()
        {
            var provider = CreateProvider(_ => throw new HttpRequestException("network down"));

            var result = await provider.FetchAsync();

            Assert.Empty(result.MarketIndex);
            Assert.Empty(result.SectorIndices);
            Assert.Null(result.Margin);
        }
    }
}
