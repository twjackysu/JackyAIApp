using System.Net;
using JackyAIApp.Server.Configuration;
using JackyAIApp.Server.Services.Market;
using JackyAIApp.Server.Tests.TestHelpers;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace JackyAIApp.Server.Tests.Services.Finance
{
    /// <summary>
    /// Parses a real response shape captured from Finnhub's general news endpoint — fixture only,
    /// no network calls. THIRD_PARTY_CONTRACT_TESTS_PLAN.md section B9. The offline tests never
    /// need a real API key: the HTTP layer is fully stubbed, so <see cref="FinnhubNewsProvider"/>
    /// is constructed with a fake configured key.
    /// </summary>
    public class FinnhubNewsProviderTests
    {
        private const string FixtureDir = "Fixtures/Finnhub";

        private static string ReadFixture(string name) =>
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, FixtureDir, name));

        private static FinnhubNewsProvider CreateProvider(Func<HttpRequestMessage, HttpResponseMessage> responder, string? apiKey = "fake-test-key")
        {
            var httpClient = StubHttpMessageHandler.CreateClient(responder);
            var httpClientFactory = new Mock<IHttpClientFactory>();
            httpClientFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);

            var settings = new Settings { Google = new Google(), FinnhubApiKey = apiKey };
            var optionsMonitor = new Mock<IOptionsMonitor<Settings>>();
            optionsMonitor.Setup(o => o.CurrentValue).Returns(settings);

            var cache = new MemoryCache(new MemoryCacheOptions());
            return new FinnhubNewsProvider(httpClientFactory.Object, optionsMonitor.Object, cache, NullLogger<FinnhubNewsProvider>.Instance);
        }

        [Fact]
        public void IsConfigured_ReflectsWhetherApiKeyIsPresent()
        {
            Assert.True(CreateProvider(_ => throw new InvalidOperationException(), "real-key").IsConfigured);
            Assert.False(CreateProvider(_ => throw new InvalidOperationException(), null).IsConfigured);
            Assert.False(CreateProvider(_ => throw new InvalidOperationException(), "  ").IsConfigured);
        }

        [Fact]
        public async Task FetchAsync_ParsesHeadlineSummarySourceDatetime()
        {
            // FinnhubNewsProvider filters out anything older than LOOKBACK_HOURS (24h), so the
            // fixture's timestamps must be rebased onto "now" or this test rots as wall-clock time
            // moves past the recorded dates. Rewrite each item's datetime to a recent value while
            // keeping the fixture as the source of truth for the response *shape*.
            var expectedFirstDatetime = DateTimeOffset.UtcNow.AddMinutes(-5).ToUnixTimeSeconds();
            var json = WithRecentDatetimes(ReadFixture("news_general.json"), expectedFirstDatetime);
            var provider = CreateProvider(_ => StubHttpMessageHandler.JsonResponse(json));

            var result = await provider.FetchAsync();

            Assert.Equal(3, result.Count);
            var first = result[0];
            Assert.Equal("Fed signals steady rates as inflation cools further", first.Headline);
            Assert.Equal("Reuters", first.Source);
            Assert.Equal(expectedFirstDatetime, first.Datetime);
            Assert.Contains("Federal Reserve", first.Summary);
        }

        /// <summary>
        /// Rewrites every item's "datetime" to a recent unix timestamp (first item at
        /// <paramref name="firstDatetime"/>, each subsequent one a minute earlier) so the provider's
        /// 24-hour recency filter keeps them all — makes the fixture time-independent.
        /// </summary>
        private static string WithRecentDatetimes(string arrayJson, long firstDatetime)
        {
            var arr = System.Text.Json.Nodes.JsonNode.Parse(arrayJson)!.AsArray();
            for (var i = 0; i < arr.Count; i++)
                arr[i]!["datetime"] = firstDatetime - i * 60;
            return arr.ToJsonString();
        }

        [Fact]
        public async Task FetchAsync_NoApiKeyConfigured_ReturnsEmptyInsteadOfThrowing()
        {
            var provider = CreateProvider(_ => throw new InvalidOperationException("should not be called"), apiKey: null);

            var result = await provider.FetchAsync();

            Assert.Empty(result);
        }

        [Fact]
        public async Task FetchAsync_MissingFieldsOnItem_SkipsItemInsteadOfThrowing()
        {
            const string json = """[{"datetime":9999999999,"summary":"no headline here","source":"X"}]""";
            var provider = CreateProvider(_ => StubHttpMessageHandler.JsonResponse(json));

            var result = await provider.FetchAsync();

            Assert.Empty(result); // blank headline is filtered out
        }

        [Fact]
        public async Task FetchAsync_HttpFailure_ReturnsEmptyInsteadOfThrowing()
        {
            var provider = CreateProvider(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

            var result = await provider.FetchAsync();

            Assert.Empty(result);
        }

        [Fact]
        public async Task FetchAsync_NetworkFailure_ReturnsEmptyInsteadOfThrowing()
        {
            var provider = CreateProvider(_ => throw new HttpRequestException("network down"));

            var result = await provider.FetchAsync();

            Assert.Empty(result);
        }
    }
}
