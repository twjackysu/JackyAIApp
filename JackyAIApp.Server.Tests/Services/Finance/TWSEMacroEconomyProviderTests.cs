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
    /// section B2. Also covers the Bank of Taiwan FX CSV parser (B5) and CBC bank-rate CSV parser
    /// (B6, Phase 3) — both fetched in the same Task.WhenAll as the JSON endpoints above.
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

        private static HttpResponseMessage CsvResponse(string csv, HttpStatusCode status = HttpStatusCode.OK) =>
            new(status) { Content = new StringContent(csv) };

        private static HttpResponseMessage RouteNormal(HttpRequestMessage request)
        {
            var url = request.RequestUri!.ToString();
            if (url.Contains("FMTQIK")) return StubHttpMessageHandler.JsonResponse(ReadFixture("fmtqik.json"));
            if (url.Contains("MI_INDEX")) return StubHttpMessageHandler.JsonResponse(ReadFixture("mi_index.json"));
            if (url.Contains("MI_MARGN")) return StubHttpMessageHandler.JsonResponse(ReadFixture("mi_margn.json"));
            if (url.Contains("rate.bot.com.tw")) return CsvResponse(ReadFixture("bot_fx.csv"));
            if (url.Contains("cbc.gov.tw")) return CsvResponse(ReadFixture("cbc_a13rate.csv"));
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

            // === B5: Bank of Taiwan FX CSV ===
            Assert.Equal(5, result.ExchangeRates.Count); // USD/JPY/EUR/CNY/GBP in bot_fx.csv fixture
            var usd = result.ExchangeRates.Single(r => r.Currency == "USD");
            Assert.Equal("美元", usd.DisplayName);
            Assert.Equal(32.245m, usd.BuyRate);  // cols[3], 即期匯率買入
            Assert.Equal(32.345m, usd.SellRate); // cols[13], 即期匯率賣出

            // === B6: CBC bank-rate CSV ===
            Assert.NotNull(result.BankRate);
            // 陽信銀行/華南銀行 are not 臺灣銀行/第一銀行 so must be filtered out; the parser walks
            // rows bottom-to-top (Reverse()) and returns the first match — with the fixture order
            // 陽信/第一/臺灣/華南, reversed iteration hits 華南(skip) then 臺灣銀行 first.
            Assert.Equal("臺灣銀行", result.BankRate!.BankName);
            Assert.Equal("11409", result.BankRate.Period);
            Assert.Equal(1.700m, result.BankRate.OneYearFixed);    // cols[14]
            Assert.Equal(1.690m, result.BankRate.OneYearFloating); // cols[15]
            // cols[30] (基準利率-固定/fixed base lending rate) is genuinely blank in this real CBC
            // row — only 基準利率-機動 (floating, cols[31]) is published for most banks. The
            // production parser reads cols[30] as-is, so BaseLendingRate legitimately comes back
            // null here; this fixture captures that real-world shape rather than a synthetic
            // always-populated row.
            Assert.Null(result.BankRate.BaseLendingRate);
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

            // Empty CSV body (the fallback for bot/cbc above) must degrade to empty/null, not throw.
            Assert.Empty(result.ExchangeRates);
            Assert.Null(result.BankRate);
        }

        [Fact]
        public async Task FetchAsync_HttpFailure_DegradesToEmptyResultsInsteadOfThrowing()
        {
            var provider = CreateProvider(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

            var result = await provider.FetchAsync();

            Assert.Empty(result.MarketIndex);
            Assert.Empty(result.SectorIndices);
            Assert.Null(result.Margin);
            Assert.Empty(result.ExchangeRates);
            Assert.Null(result.BankRate);
        }

        [Fact]
        public async Task FetchAsync_NetworkFailure_DegradesToEmptyResultsInsteadOfThrowing()
        {
            var provider = CreateProvider(_ => throw new HttpRequestException("network down"));

            var result = await provider.FetchAsync();

            Assert.Empty(result.MarketIndex);
            Assert.Empty(result.SectorIndices);
            Assert.Null(result.Margin);
            Assert.Empty(result.ExchangeRates);
            Assert.Null(result.BankRate);
        }

        [Fact]
        public async Task FetchExchangeRates_BotAntiBotChallengeHtml_DegradesToEmptyInsteadOfThrowing()
        {
            // Bank of Taiwan is known to serve a "Challenge Validation" HTML page to non-browser
            // clients instead of the CSV (verified live while implementing this test — see
            // TwseMacroCsvContractTests). The parser has no special-case detection for this; it
            // just splits lines/commas as usual. Confirms that garbage-shaped input degrades to an
            // empty list rather than throwing, so the rest of FetchAsync's Task.WhenAll still
            // completes.
            const string challengeHtml = """<!DOCTYPE html><html><head><title>Challenge Validation</title></head><body>blocked</body></html>""";

            var provider = CreateProvider(req =>
            {
                var url = req.RequestUri!.ToString();
                if (url.Contains("rate.bot.com.tw")) return CsvResponse(challengeHtml);
                if (url.Contains("cbc.gov.tw")) return CsvResponse(ReadFixture("cbc_a13rate.csv"));
                if (url.Contains("FMTQIK")) return StubHttpMessageHandler.JsonResponse(ReadFixture("fmtqik.json"));
                if (url.Contains("MI_INDEX")) return StubHttpMessageHandler.JsonResponse(ReadFixture("mi_index.json"));
                if (url.Contains("MI_MARGN")) return StubHttpMessageHandler.JsonResponse(ReadFixture("mi_margn.json"));
                throw new InvalidOperationException($"Unexpected URL: {url}");
            });

            var result = await provider.FetchAsync();

            Assert.Empty(result.ExchangeRates);
            Assert.NotNull(result.BankRate); // CBC parse is unaffected by the BOT failure
        }

        [Fact]
        public async Task FetchBankRates_ColumnCountBelowThreshold_SkipsRowInsteadOfThrowing()
        {
            const string malformedCsv = "銀行,年月\n臺灣銀行,11409\n"; // far fewer than the required 31 columns

            var provider = CreateProvider(req =>
            {
                var url = req.RequestUri!.ToString();
                if (url.Contains("cbc.gov.tw")) return CsvResponse(malformedCsv);
                if (url.Contains("rate.bot.com.tw")) return CsvResponse(ReadFixture("bot_fx.csv"));
                if (url.Contains("FMTQIK")) return StubHttpMessageHandler.JsonResponse(ReadFixture("fmtqik.json"));
                if (url.Contains("MI_INDEX")) return StubHttpMessageHandler.JsonResponse(ReadFixture("mi_index.json"));
                if (url.Contains("MI_MARGN")) return StubHttpMessageHandler.JsonResponse(ReadFixture("mi_margn.json"));
                throw new InvalidOperationException($"Unexpected URL: {url}");
            });

            var result = await provider.FetchAsync();

            Assert.Null(result.BankRate);
            Assert.NotEmpty(result.ExchangeRates); // BOT parse is unaffected by the CBC failure
        }
    }
}
