using JackyAIApp.Server.Services.Portfolio;
using JackyAIApp.Server.Tests.TestHelpers;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace JackyAIApp.Server.Tests.Services.Portfolio
{
    /// <summary>
    /// TASK-025: on a cold cache, several Portfolio endpoints can request the same fund/dividend
    /// key at once (same page load). These assert the upstream HTTP call only happens once per
    /// key, no matter how many concurrent callers miss the cache together.
    /// </summary>
    public class FundDividendSingleFlightTests
    {
        /// <summary>
        /// Like <see cref="StubHttpMessageHandler"/> but holds every request open until
        /// <paramref name="gate"/> completes, so concurrent callers are guaranteed to overlap
        /// instead of resolving one-by-one before the next starts.
        /// </summary>
        private sealed class GatedHandler(Func<HttpRequestMessage, HttpResponseMessage> responder, TaskCompletionSource gate)
            : HttpMessageHandler
        {
            public int CallCount;

            protected override async Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Interlocked.Increment(ref CallCount);
                await gate.Task;
                return responder(request);
            }
        }

        /// <summary>
        /// SingleFlight's de-dup table (see Common/SingleFlight.cs) is process-wide by design —
        /// correct for production, since the same fundId/symbol really is the same upstream fetch
        /// no matter which request triggers it. But it means a key held "in flight" for any length
        /// of time (as these tests deliberately do, to force real overlap) is a shared key any
        /// other test in this assembly could collide with if xUnit runs them concurrently and
        /// happens to reuse the same literal fixture id/symbol (e.g. "ALI006", "AAPL" — both used
        /// elsewhere in this suite). A fresh GUID per test run can never collide with anything.
        /// </summary>
        private static string UniqueId() => $"SF{Guid.NewGuid():N}"[..12];

        [Fact]
        public async Task FundDataService_GetFundNavHistoryAsync_ConcurrentCallsSameFundId_OnlyFetchesUpstreamOnce()
        {
            var fundId = UniqueId();
            const string json = """
            {"data":{"tableRow":{"currency":"TWD","priceHistory":[{"date":"2026/03/02","price":"18.50"}]}}}
            """;
            var gate = new TaskCompletionSource();
            var handler = new GatedHandler(_ => StubHttpMessageHandler.JsonResponse(json), gate);
            var httpClientFactory = new Mock<IHttpClientFactory>();
            httpClientFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(new HttpClient(handler));
            var service = new FundDataService(
                httpClientFactory.Object, new MemoryCache(new MemoryCacheOptions()), NullLogger<FundDataService>.Instance);

            var calls = Enumerable.Range(0, 5).Select(_ => service.GetFundNavHistoryAsync(fundId)).ToList();
            await Task.Delay(50);
            gate.SetResult();
            var results = await Task.WhenAll(calls);

            Assert.Equal(1, handler.CallCount);
            Assert.All(results, r => Assert.Equal("TWD", r!.Currency));
        }

        [Fact]
        public async Task DividendService_GetStockDividendsAsync_ConcurrentCallsSameSymbol_OnlyFetchesUpstreamOnce()
        {
            var symbol = UniqueId();
            const string json = """
            {"chart":{"result":[{"meta":{"currency":"USD"},"events":{"dividends":{"1754919000":{"amount":0.26,"date":1754919000}}}}]}}
            """;
            var gate = new TaskCompletionSource();
            var handler = new GatedHandler(_ => StubHttpMessageHandler.JsonResponse(json), gate);
            var httpClientFactory = new Mock<IHttpClientFactory>();
            httpClientFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(new HttpClient(handler));
            var service = new DividendService(
                httpClientFactory.Object, new MemoryCache(new MemoryCacheOptions()), NullLogger<DividendService>.Instance);

            var calls = Enumerable.Range(0, 5).Select(_ => service.GetStockDividendsAsync(symbol, "US")).ToList();
            await Task.Delay(50);
            gate.SetResult();
            var results = await Task.WhenAll(calls);

            Assert.Equal(1, handler.CallCount);
            Assert.All(results, r => Assert.Equal(0.26m, Assert.Single(r).AmountPerUnit));
        }

        [Fact]
        public async Task DividendService_GetFundDividendsAsync_ConcurrentCallsSameFundId_OnlyFetchesUpstreamOnce()
        {
            var fundId = UniqueId();
            const string json = """
            {"data":{"tableRow":{"history":[{"exDividendDate":"2026/01/15","perUnitPrice":"0.5"}]}}}
            """;
            var gate = new TaskCompletionSource();
            var handler = new GatedHandler(_ => StubHttpMessageHandler.JsonResponse(json), gate);
            var httpClientFactory = new Mock<IHttpClientFactory>();
            httpClientFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(new HttpClient(handler));
            var service = new DividendService(
                httpClientFactory.Object, new MemoryCache(new MemoryCacheOptions()), NullLogger<DividendService>.Instance);

            var calls = Enumerable.Range(0, 5).Select(_ => service.GetFundDividendsAsync(fundId)).ToList();
            await Task.Delay(50);
            gate.SetResult();
            var results = await Task.WhenAll(calls);

            Assert.Equal(1, handler.CallCount);
            Assert.All(results, r => Assert.Equal(0.5m, Assert.Single(r).AmountPerUnit));
        }
    }
}
