using System.Net;
using JackyAIApp.Server.Configuration;
using JackyAIApp.Server.Services.Finance.DataProviders;
using JackyAIApp.Server.Tests.TestHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace JackyAIApp.Server.Tests.Services.Finance
{
    /// <summary>
    /// Parses a real response shape captured from FRED's series/observations endpoint — fixture
    /// only, no network calls. THIRD_PARTY_CONTRACT_TESTS_PLAN.md section B10. The offline tests
    /// never need a real API key: the HTTP layer is fully stubbed, so <see cref="FredIndicatorProvider"/>
    /// is constructed with a fake configured key.
    /// </summary>
    public class FredIndicatorProviderTests
    {
        private const string FixtureDir = "Fixtures/Fred";

        private static string ReadFixture(string name) =>
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, FixtureDir, name));

        private static FredIndicatorProvider CreateProvider(Func<HttpRequestMessage, HttpResponseMessage> responder, string? apiKey = "fake-test-key")
        {
            var httpClient = StubHttpMessageHandler.CreateClient(responder);
            var httpClientFactory = new Mock<IHttpClientFactory>();
            httpClientFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);

            var settings = new Settings { Google = new Google(), FredApiKey = apiKey };
            var optionsMonitor = new Mock<IOptionsMonitor<Settings>>();
            optionsMonitor.Setup(o => o.CurrentValue).Returns(settings);

            return new FredIndicatorProvider(httpClientFactory.Object, optionsMonitor.Object, NullLogger<FredIndicatorProvider>.Instance);
        }

        [Fact]
        public void IsConfigured_ReflectsWhetherApiKeyIsPresent()
        {
            Assert.True(CreateProvider(_ => throw new InvalidOperationException(), "real-key").IsConfigured);
            Assert.False(CreateProvider(_ => throw new InvalidOperationException(), null).IsConfigured);
        }

        [Fact]
        public async Task FetchAsync_ParsesObservationsAndSkipsMissingDataSentinel()
        {
            // Every series in FredIndicatorProvider.Indicators hits the same endpoint shape —
            // route them all to the same fixture (a trimmed DGS10 response) since the parser
            // doesn't branch on series id.
            var provider = CreateProvider(_ => StubHttpMessageHandler.JsonResponse(ReadFixture("observations_dgs10.json")));

            var result = await provider.FetchAsync();

            Assert.Equal(6, result.Indicators.Count); // one per entry in the Indicators array
            var us10y = result.Indicators.Single(i => i.Id == "us10y");
            // Fixture has 6 raw observations but 2 are "." (FRED's missing-data sentinel) — must
            // be filtered out, leaving 4 usable points.
            Assert.Equal(4, us10y.Points.Count);
            Assert.Equal("2026-07-08", us10y.LatestDate);
            Assert.Equal(4.30m, us10y.LatestValue);
            Assert.Equal(4.29m, us10y.PreviousValue);
        }

        [Fact]
        public async Task FetchAsync_NoApiKeyConfigured_Throws()
        {
            var provider = CreateProvider(_ => throw new InvalidOperationException("should not be called"), apiKey: null);

            await Assert.ThrowsAsync<InvalidOperationException>(() => provider.FetchAsync());
        }

        [Fact]
        public async Task FetchAsync_AllValuesAreMissingSentinel_DropsThatSeriesInsteadOfThrowing()
        {
            const string allMissing = """{"observations":[{"date":"2026-07-01","value":"."},{"date":"2026-07-02","value":"."}]}""";
            var provider = CreateProvider(_ => StubHttpMessageHandler.JsonResponse(allMissing));

            var result = await provider.FetchAsync();

            Assert.Empty(result.Indicators); // every series has zero usable points, so all drop out
        }

        [Fact]
        public async Task FetchAsync_SeriesHttpFailure_DropsThatSeriesInsteadOfThrowing()
        {
            var provider = CreateProvider(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

            var result = await provider.FetchAsync();

            Assert.Empty(result.Indicators);
        }

        [Fact]
        public async Task FetchAsync_NetworkFailure_DropsThatSeriesInsteadOfThrowing()
        {
            var provider = CreateProvider(_ => throw new HttpRequestException("network down"));

            var result = await provider.FetchAsync();

            Assert.Empty(result.Indicators);
        }
    }
}
