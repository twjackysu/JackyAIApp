using System.Net;
using JackyAIApp.Server.Services.Finance.DataProviders;
using JackyAIApp.Server.Tests.TestHelpers;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using JackyAIApp.Server.Services.ExternalApi.Twse;

namespace JackyAIApp.Server.Tests.Services.Finance
{
    /// <summary>
    /// Parses real response shapes captured from TWSE OpenAPI's market-wide endpoints (FMTQIK,
    /// MI_INDEX, MI_MARGN) — fixtures only, no network calls. Also covers the exchange rates
    /// (open.er-api.com) and the CBC bank-rate CSV parser, fetched in the same Task.WhenAll.
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
            return new TWSEMacroEconomyProvider(httpClientFactory.Object, cache, NullLogger<TWSEMacroEconomyProvider>.Instance,
                new TwseOpenApi(httpClientFactory.Object, cache, NullLogger<TwseOpenApi>.Instance, TimeProvider.System));
        }

        /// <summary>open.er-api.com's latest/USD as it answered on 2026-10-22, trimmed to the
        /// currencies read plus one that is not.</summary>
        private const string ErApiLatestUsd = """
            {"result":"success","base_code":"USD","time_last_update_utc":"Thu, 22 Oct 2026 00:02:31 +0000",
             "rates":{"USD":1,"TWD":32.411205,"JPY":158.100039,"EUR":0.890292,"CNY":6.689327,"GBP":0.746668,"AUD":1.52}}
            """;

        private static HttpResponseMessage CsvResponse(string csv, HttpStatusCode status = HttpStatusCode.OK) =>
            new(status) { Content = new StringContent(csv) };

        private static HttpResponseMessage RouteNormal(HttpRequestMessage request)
        {
            var url = request.RequestUri!.ToString();
            if (url.Contains("FMTQIK")) return StubHttpMessageHandler.JsonResponse(ReadFixture("fmtqik.json"));
            if (url.Contains("MI_INDEX")) return StubHttpMessageHandler.JsonResponse(ReadFixture("mi_index.json"));
            if (url.Contains("MI_MARGN")) return StubHttpMessageHandler.JsonResponse(ReadFixture("mi_margn.json"));
            if (url.Contains("open.er-api.com")) return StubHttpMessageHandler.JsonResponse(ErApiLatestUsd);
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

            // === Exchange rates: TWD per unit, from rates quoted per USD ===
            Assert.Equal(
                [("USD", "美元", 32.4112m), ("JPY", "日圓", 0.2050m), ("EUR", "歐元", 36.4051m),
                 ("CNY", "人民幣", 4.8452m), ("GBP", "英鎊", 43.4078m)],
                result.ExchangeRates.Select(r => (r.Currency, r.DisplayName, r.Rate)));

            // === CBC bank-rate CSV ===
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
        public async Task FetchAsync_MarginChange_IsTodaysBalanceMinusPreviousDays_IncludingRepayments()
        {
            // Two stocks whose sums are 2026-10-20's market-wide 信用交易統計. Buy − sell would give
            // +13,339 / +26,124; the balances, which include 融資現金償還 (5,418) and 融券現券償還
            // (12,123), give +7,921 / +14,001.
            const string miMargn = """
                [
                  {"股票代號":"2330","融資買進":"200000","融資賣出":"180000","融資現金償還":"3000","融資前日餘額":"5000000","融資今日餘額":"5017000",
                   "融券買進":"10000","融券賣出":"30000","融券現券償還":"8000","融券前日餘額":"100000","融券今日餘額":"112000"},
                  {"股票代號":"2317","融資買進":"89957","融資賣出":"96618","融資現金償還":"2418","融資前日餘額":"4592706","融資今日餘額":"4583627",
                   "融券買進":"6058","融券賣出":"12182","融券現券償還":"4123","融券前日餘額":"101911","融券今日餘額":"103912"}
                ]
                """;
            var provider = CreateProvider(req =>
                req.RequestUri!.ToString().Contains("MI_MARGN") ? StubHttpMessageHandler.JsonResponse(miMargn) : RouteNormal(req));

            var margin = (await provider.FetchAsync()).Margin!;

            Assert.Equal(289957L, margin.MarginBuyTotal);
            Assert.Equal(276618L, margin.MarginSellTotal);
            Assert.Equal(9592706L, margin.MarginPrevBalanceTotal);
            Assert.Equal(9600627L, margin.MarginBalanceTotal);
            Assert.Equal(7921L, margin.MarginChange);

            Assert.Equal(42182L, margin.ShortSellTotal);
            Assert.Equal(16058L, margin.ShortBuyTotal);
            Assert.Equal(201911L, margin.ShortPrevBalanceTotal);
            Assert.Equal(215912L, margin.ShortBalanceTotal);
            Assert.Equal(14001L, margin.ShortChange);
        }

        [Fact]
        public async Task FetchAsync_MarginWithoutPreviousBalances_HasNoChangeRatherThanTheWholeBalance()
        {
            const string miMargn = """[{"股票代號":"2330","融資今日餘額":"34401","融券今日餘額":"14"}]""";
            var provider = CreateProvider(req =>
                req.RequestUri!.ToString().Contains("MI_MARGN") ? StubHttpMessageHandler.JsonResponse(miMargn) : RouteNormal(req));

            var margin = (await provider.FetchAsync()).Margin!;

            Assert.Equal(34401L, margin.MarginBalanceTotal);
            Assert.Null(margin.MarginChange);
            Assert.Null(margin.ShortChange);
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

            // An empty body (the fallback for er-api/cbc above) must degrade to empty/null, not throw.
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

        [Theory]
        [InlineData("""{"result":"error","error-type":"quota-reached"}""")]
        [InlineData("""{"result":"success","rates":{"USD":1,"JPY":158.1}}""")] // no TWD to quote against
        [InlineData("""<!DOCTYPE html><html><head><title>Challenge Validation</title></head></html>""")]
        public async Task FetchExchangeRates_AnAnswerWithoutRates_IsEmpty_AndTheRestStillArrives(string answer)
        {
            var provider = CreateProvider(req => req.RequestUri!.ToString().Contains("open.er-api.com")
                ? StubHttpMessageHandler.JsonResponse(answer)
                : RouteNormal(req));

            var result = await provider.FetchAsync();

            Assert.Empty(result.ExchangeRates);
            Assert.NotNull(result.BankRate);
            Assert.NotEmpty(result.MarketIndex);
        }

        [Fact]
        public async Task FetchExchangeRates_ACurrencyWithoutARate_IsLeftOut()
        {
            const string withoutJpy = """{"result":"success","rates":{"USD":1,"TWD":32.4,"JPY":null,"EUR":0.9,"CNY":"6.7","GBP":0}}""";
            var provider = CreateProvider(req => req.RequestUri!.ToString().Contains("open.er-api.com")
                ? StubHttpMessageHandler.JsonResponse(withoutJpy)
                : RouteNormal(req));

            var result = await provider.FetchAsync();

            Assert.Equal([("USD", 32.4m), ("EUR", 36m)], result.ExchangeRates.Select(r => (r.Currency, r.Rate)));
        }

        [Fact]
        public async Task FetchBankRates_ColumnCountBelowThreshold_SkipsRowInsteadOfThrowing()
        {
            const string malformedCsv = "銀行,年月\n臺灣銀行,11409\n"; // far fewer than the required 31 columns

            var provider = CreateProvider(req =>
            {
                var url = req.RequestUri!.ToString();
                if (url.Contains("cbc.gov.tw")) return CsvResponse(malformedCsv);
                if (url.Contains("open.er-api.com")) return StubHttpMessageHandler.JsonResponse(ErApiLatestUsd);
                if (url.Contains("FMTQIK")) return StubHttpMessageHandler.JsonResponse(ReadFixture("fmtqik.json"));
                if (url.Contains("MI_INDEX")) return StubHttpMessageHandler.JsonResponse(ReadFixture("mi_index.json"));
                if (url.Contains("MI_MARGN")) return StubHttpMessageHandler.JsonResponse(ReadFixture("mi_margn.json"));
                throw new InvalidOperationException($"Unexpected URL: {url}");
            });

            var result = await provider.FetchAsync();

            Assert.Null(result.BankRate);
            Assert.NotEmpty(result.ExchangeRates); // the rates are unaffected by the CBC failure
        }
    }
}
