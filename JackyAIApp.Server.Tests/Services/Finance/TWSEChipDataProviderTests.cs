using System.Net;
using JackyAIApp.Server.Services.Finance.DataProviders;
using JackyAIApp.Server.Tests.TestHelpers;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace JackyAIApp.Server.Tests.Services.Finance
{
    /// <summary>
    /// Parses real response shapes captured from TWSE OpenAPI's chip (籌碼面) endpoints — margin
    /// (MI_MARGN, per-stock), foreign holding (MI_QFIIS_sort_20, top-20 only), securities lending
    /// (SBL/TWT96U), director holdings (t187ap11_L), major shareholders (t187ap02_L), and day
    /// trading suspension (TWTB4U) — fixtures only, no network calls.
    /// THIRD_PARTY_CONTRACT_TESTS_PLAN.md section B3.
    /// </summary>
    public class TWSEChipDataProviderTests
    {
        private const string FixtureDir = "Fixtures/TWSE";

        private static string ReadFixture(string name) =>
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, FixtureDir, name));

        // TWSEChipDataProvider disposes the HttpClient it gets from the factory after each of its
        // 6 parallel per-endpoint fetches (`using var httpClient = ...`). A mock that always
        // `.Returns(sameInstance)` would break after the first fetch disposes it — matching real
        // IHttpClientFactory semantics (a fresh HttpClient per CreateClient() call, wrapping a
        // shared handler) means the factory must hand out a new client each call.
        private static TWSEChipDataProvider CreateProvider(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            var httpClientFactory = new Mock<IHttpClientFactory>();
            httpClientFactory.Setup(f => f.CreateClient(It.IsAny<string>()))
                .Returns(() => StubHttpMessageHandler.CreateClient(responder));
            var cache = new MemoryCache(new MemoryCacheOptions());
            return new TWSEChipDataProvider(httpClientFactory.Object, cache, NullLogger<TWSEChipDataProvider>.Instance);
        }

        private static HttpResponseMessage RouteNormal(HttpRequestMessage request)
        {
            var url = request.RequestUri!.ToString();
            if (url.Contains("MI_MARGN")) return StubHttpMessageHandler.JsonResponse(ReadFixture("mi_margn_chip.json"));
            if (url.Contains("MI_QFIIS_sort_20")) return StubHttpMessageHandler.JsonResponse(ReadFixture("mi_qfiis_sort_20.json"));
            if (url.Contains("SBL/TWT96U")) return StubHttpMessageHandler.JsonResponse(ReadFixture("sbl_twt96u.json"));
            if (url.Contains("t187ap11_L")) return StubHttpMessageHandler.JsonResponse(ReadFixture("director_t187ap11_l.json"));
            if (url.Contains("t187ap02_L")) return StubHttpMessageHandler.JsonResponse(ReadFixture("major_shareholders_t187ap02_l.json"));
            if (url.Contains("TWTB4U")) return StubHttpMessageHandler.JsonResponse(ReadFixture("twtb4u.json"));
            throw new InvalidOperationException($"Unexpected URL in test: {url}");
        }

        [Fact]
        public async Task FetchAsync_KnownStock_ParsesMarginSblDirectorAndDayTradingFields()
        {
            var provider = CreateProvider(RouteNormal);

            var result = await provider.FetchAsync("2330");

            Assert.Equal("2330", result.StockCode);
            var chips = result.Chips!;

            // Margin (per-stock row in mi_margn_chip.json).
            Assert.Equal(679L, chips.MarginBuyVolume);
            Assert.Equal(2231L, chips.MarginSellVolume);
            Assert.Equal(32542L, chips.MarginBalance);
            Assert.Equal(89L, chips.ShortBalance);

            // SBL — "12,866,860" comma-formatted volume must parse.
            Assert.Equal(12866860L, chips.SBLAvailableVolume);

            // Director holdings — fixture has 3 rows for 2330.
            Assert.NotNull(chips.DirectorHoldings);
            Assert.Equal(3, chips.DirectorHoldings!.Count);
            var expectedTotalHeld = 7452349 + 1653709980 + 0;
            var expectedTotalPledged = 1600000 + 0 + 0;
            Assert.Equal(expectedTotalHeld, chips.TotalDirectorShares);
            Assert.Equal(expectedTotalPledged, chips.TotalDirectorPledged);
            Assert.Equal(Math.Round((decimal)expectedTotalPledged / expectedTotalHeld * 100, 2), chips.DirectorPledgeRatio);

            // Day trading — 2330's fixture row has an empty Suspension field, i.e. not suspended.
            Assert.False(chips.DayTradingSuspended);

            // 2330 is not in this test's trimmed top-20 foreign-holding fixture or the
            // major-shareholders fixture — both should degrade to "not found" rather than throw.
            Assert.Null(chips.ForeignHoldingShares);
            Assert.Null(chips.MajorShareholders);
        }

        [Fact]
        public async Task FetchAsync_StockInForeignTop20_ParsesForeignHoldingFields()
        {
            var provider = CreateProvider(RouteNormal);

            var result = await provider.FetchAsync("2923");

            var chips = result.Chips!;
            Assert.Equal(1730611563L, chips.ForeignHoldingShares);
            Assert.Equal(99.44m, chips.ForeignHoldingPercentage);
            Assert.Equal(9685419L, chips.ForeignAvailableShares);
            Assert.Equal(100.00m, chips.ForeignUpperLimit);
        }

        [Fact]
        public async Task FetchAsync_StockWithMultipleMajorShareholders_ParsesAllNames()
        {
            var provider = CreateProvider(RouteNormal);

            // 1104's fixture rows include 3 distinct >10% shareholders.
            var result = await provider.FetchAsync("1104");

            var chips = result.Chips!;
            Assert.NotNull(chips.MajorShareholders);
            Assert.Equal(3, chips.MajorShareholders!.Count);
            Assert.Contains("宇聲投資股份有限公司", chips.MajorShareholders);
        }

        [Fact]
        public async Task FetchAsync_StockNotFoundInAnySource_ReturnsDefaultsWithoutThrowing()
        {
            var provider = CreateProvider(RouteNormal);

            var result = await provider.FetchAsync("0000");

            var chips = result.Chips!;
            Assert.Equal(0L, chips.MarginBuyVolume);
            Assert.Null(chips.SBLAvailableVolume);
            Assert.Null(chips.DirectorHoldings);
            Assert.Null(chips.ForeignHoldingShares);
            Assert.Null(chips.MajorShareholders);
            Assert.False(chips.DayTradingSuspended);
        }

        [Fact]
        public async Task FetchAsync_MissingOptionalFields_DefaultsGracefullyInsteadOfThrowing()
        {
            const string marginMissingFields = """[{"股票代號":"2330"}]"""; // only the key field present
            var provider = CreateProvider(req =>
            {
                var url = req.RequestUri!.ToString();
                if (url.Contains("MI_MARGN")) return StubHttpMessageHandler.JsonResponse(marginMissingFields);
                if (url.Contains("MI_QFIIS_sort_20")) return StubHttpMessageHandler.JsonResponse("[]");
                if (url.Contains("SBL/TWT96U")) return StubHttpMessageHandler.JsonResponse("[]");
                if (url.Contains("t187ap11_L")) return StubHttpMessageHandler.JsonResponse("[]");
                if (url.Contains("t187ap02_L")) return StubHttpMessageHandler.JsonResponse("[]");
                if (url.Contains("TWTB4U")) return StubHttpMessageHandler.JsonResponse("[]");
                throw new InvalidOperationException($"Unexpected URL in test: {url}");
            });

            var result = await provider.FetchAsync("2330");

            var chips = result.Chips!;
            Assert.Equal(0L, chips.MarginBuyVolume);
            // Field present in the row lookup but absent from JSON parses to 0, not null —
            // ParseLong never returns null, only the "row not found at all" path leaves the
            // nullable field at its default.
            Assert.Equal(0L, chips.MarginBalance);
        }

        [Fact]
        public async Task FetchAsync_HttpFailure_ReturnsDefaultsInsteadOfThrowing()
        {
            var provider = CreateProvider(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

            var result = await provider.FetchAsync("2330");

            var chips = result.Chips!;
            Assert.Equal(0L, chips.MarginBuyVolume);
            Assert.Null(chips.DirectorHoldings);
        }

        [Fact]
        public async Task FetchAsync_NetworkFailure_ReturnsDefaultsInsteadOfThrowing()
        {
            var provider = CreateProvider(_ => throw new HttpRequestException("network down"));

            var result = await provider.FetchAsync("2330");

            var chips = result.Chips!;
            Assert.Equal(0L, chips.MarginBuyVolume);
            Assert.Null(chips.DirectorHoldings);
        }
    }
}
