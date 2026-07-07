using JackyAIApp.Server.Services.Portfolio;
using JackyAIApp.Server.Tests.TestHelpers;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace JackyAIApp.Server.Tests.Services.Portfolio
{
    /// <summary>
    /// Parses real response shapes captured from FundRich's search/NAV-chart endpoints (a
    /// reverse-engineered private API, no official docs — see FundDataService's class doc) —
    /// fixtures only, no network calls. THIRD_PARTY_CONTRACT_TESTS_PLAN.md section A2/A3.
    /// </summary>
    public class FundDataServiceTests
    {
        private static FundDataService CreateService(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            var httpClient = StubHttpMessageHandler.CreateClient(responder);
            var httpClientFactory = new Mock<IHttpClientFactory>();
            httpClientFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);
            var cache = new MemoryCache(new MemoryCacheOptions());
            return new FundDataService(httpClientFactory.Object, cache, NullLogger<FundDataService>.Instance);
        }

        private static HttpResponseMessage JsonResponse(string json, System.Net.HttpStatusCode status = System.Net.HttpStatusCode.OK) =>
            StubHttpMessageHandler.JsonResponse(json, status);

        [Fact]
        public async Task SearchFundsAsync_ParsesResultList()
        {
            const string json = """
            {"status":0,"data":{"list":[
                {"fundId":"076009","name":"DWS投資全球高股息美元避險年配息級別 USD LDH(P)","isSell":true},
                {"fundId":"ALI006","name":"安聯台灣科技基金","isSell":false}
            ]}}
            """;
            var service = CreateService(_ => JsonResponse(json));

            var items = await service.SearchFundsAsync("高股息");

            Assert.Equal(2, items.Count);
            Assert.Equal("076009", items[0].FundId);
            Assert.True(items[0].IsSell);
            Assert.Equal("ALI006", items[1].FundId);
            Assert.False(items[1].IsSell);
        }

        [Fact]
        public async Task SearchFundsAsync_EmptyKeyword_ReturnsEmptyWithoutCallingApi()
        {
            var called = false;
            var service = CreateService(_ => { called = true; return JsonResponse("{}"); });

            var items = await service.SearchFundsAsync("   ");

            Assert.Empty(items);
            Assert.False(called);
        }

        [Fact]
        public async Task SearchFundsAsync_MissingListField_ReturnsEmptyInsteadOfThrowing()
        {
            const string json = """{"status":0,"data":{}}""";
            var service = CreateService(_ => JsonResponse(json));

            var items = await service.SearchFundsAsync("test");

            Assert.Empty(items);
        }

        [Fact]
        public async Task SearchFundsAsync_HttpFailure_ReturnsEmptyInsteadOfThrowing()
        {
            var service = CreateService(_ => JsonResponse("", System.Net.HttpStatusCode.InternalServerError));

            var items = await service.SearchFundsAsync("test");

            Assert.Empty(items);
        }

        [Fact]
        public async Task SearchFundsAsync_NetworkFailure_ReturnsEmptyInsteadOfThrowing()
        {
            var service = CreateService(_ => throw new HttpRequestException("network down"));

            var items = await service.SearchFundsAsync("test");

            Assert.Empty(items);
        }

        [Fact]
        public async Task GetFundNavHistoryAsync_ParsesPriceHistoryAndSortsAscending()
        {
            const string json = """
            {"data":{"tableRow":{"currency":"TWD","priceHistory":[
                {"date":"2026/03/02","price":"18.50"},
                {"date":"2026/03/01","price":"18.20"}
            ]}}}
            """;
            var service = CreateService(_ => JsonResponse(json));

            var history = await service.GetFundNavHistoryAsync("ALI006");

            Assert.NotNull(history);
            Assert.Equal("TWD", history!.Currency);
            Assert.Equal(2, history.Prices.Count);
            // API returns newest-first; service normalizes to ascending by date.
            Assert.Equal("2026-03-01", history.Prices[0].Date);
            Assert.Equal(18.20m, history.Prices[0].Close);
            Assert.Equal("2026-03-02", history.Prices[1].Date);
        }

        [Fact]
        public async Task GetFundNavHistoryAsync_NoUsableFundIdOrTableRow_ReturnsNull()
        {
            const string json = """{"data":{}}""";
            var service = CreateService(_ => JsonResponse(json));

            var history = await service.GetFundNavHistoryAsync("076009");

            Assert.Null(history);
        }

        [Fact]
        public async Task GetFundNavHistoryAsync_AllPricesUnparsableOrZero_ReturnsNull()
        {
            const string json = """
            {"data":{"tableRow":{"currency":"TWD","priceHistory":[
                {"date":"2026/03/02","price":"0"},
                {"date":"not-a-date","price":"18.20"}
            ]}}}
            """;
            var service = CreateService(_ => JsonResponse(json));

            var history = await service.GetFundNavHistoryAsync("ALI006");

            Assert.Null(history);
        }

        [Fact]
        public async Task GetFundNavHistoryAsync_NetworkFailure_ReturnsNullInsteadOfThrowing()
        {
            var service = CreateService(_ => throw new HttpRequestException("network down"));

            var history = await service.GetFundNavHistoryAsync("ALI006");

            Assert.Null(history);
        }
    }
}
