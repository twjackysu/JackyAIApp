using System.Net;
using JackyAIApp.Server.Services.Market;
using JackyAIApp.Server.Tests.TestHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using JackyAIApp.Server.Services.ExternalApi.Twse;
using Microsoft.Extensions.Caching.Memory;

namespace JackyAIApp.Server.Tests.Services.Finance
{
    /// <summary>
    /// The 加權指數 on the market dashboard comes from TWSE (MIS t00, else FMTQIK), never Yahoo,
    /// whose ^TWII chartPreviousClose has been a trading day behind. Figures are 2026-10-20's:
    /// close 46,292.93, previous close (10/19) 46,822.08, so −529.15 (−1.13%) — not the −818.37
    /// that 10/16's 47,111.26 as the previous close gave.
    /// </summary>
    public class MarketOverviewProviderTests
    {
        private const string Fmtqik = """
            [
              {"Date":"1151016","TradeVolume":"11820630612","TradeValue":"993814830090","Transaction":"4986590","TAIEX":"47111.26","Change":"141.39"},
              {"Date":"1151020","TradeVolume":"11317471077","TradeValue":"802947441658","Transaction":"5155199","TAIEX":"46292.93","Change":"-529.15"},
              {"Date":"1151019","TradeVolume":"12253221834","TradeValue":"866711859964","Transaction":"5507146","TAIEX":"46822.08","Change":"-289.18"}
            ]
            """;

        private readonly Mock<IMisTaiexQuoteSource> _quotes = new();
        private readonly List<string> _requested = new();

        private MarketOverviewProvider CreateProvider(Func<HttpRequestMessage, HttpResponseMessage> twse)
        {
            var httpClient = StubHttpMessageHandler.CreateClient(req =>
            {
                var url = req.RequestUri!.ToString();
                lock (_requested) _requested.Add(url);
                // Every Yahoo quote fails, so the board holds only what TWSE gave.
                return url.Contains("FMTQIK") ? twse(req) : new HttpResponseMessage(HttpStatusCode.NotFound);
            });
            var factory = new Mock<IHttpClientFactory>();
            factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);
            return new MarketOverviewProvider(factory.Object, _quotes.Object,
                new TwseOpenApi(factory.Object, new MemoryCache(new MemoryCacheOptions()), NullLogger<TwseOpenApi>.Instance, TimeProvider.System),
                NullLogger<MarketOverviewProvider>.Instance);
        }

        private void MisAnswers(MisIndexQuote? quote) =>
            _quotes.Setup(q => q.GetAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new MisQuoteSnapshot(quote, DateTimeOffset.UtcNow));

        private void AssertNeverAskedYahooForTaiex() =>
            Assert.DoesNotContain(_requested, u => u.Contains("TWII"));

        [Fact]
        public async Task Taiex_FromMis_ChangeIsLastMinusMisPreviousClose()
        {
            var at = new DateTimeOffset(2026, 10, 20, 13, 30, 0, TimeSpan.FromHours(8));
            MisAnswers(new MisIndexQuote(new DateOnly(2026, 10, 20), at, 46292.93m, 46822.08m));
            var provider = CreateProvider(_ => throw new InvalidOperationException("FMTQIK is only the fallback"));

            var result = await provider.FetchAsync();

            var taiex = Assert.Single(result.Quotes);
            Assert.Equal("^TWII", taiex.Symbol);
            Assert.Equal("TW", taiex.Group);
            Assert.Equal(46292.93m, taiex.Price);
            Assert.Equal(-529.15m, taiex.Change);
            Assert.Equal(-1.13m, taiex.ChangePercent);
            AssertNeverAskedYahooForTaiex();
        }

        [Fact]
        public async Task Taiex_NoMisReading_UsesLatestFmtqikDayAndItsOfficialChange()
        {
            MisAnswers(null); // before the open, or MIS down
            var provider = CreateProvider(_ => StubHttpMessageHandler.JsonResponse(Fmtqik));

            var result = await provider.FetchAsync();

            // Rows are deliberately out of order: the latest is chosen by Date, not position.
            var taiex = Assert.Single(result.Quotes);
            Assert.Equal(46292.93m, taiex.Price);
            Assert.Equal(-529.15m, taiex.Change);
            Assert.Equal(-1.13m, taiex.ChangePercent); // against 46,292.93 + 529.15 = 10/19's 46,822.08
            AssertNeverAskedYahooForTaiex();
        }

        [Fact]
        public async Task Taiex_MisThrows_FallsBackToFmtqik()
        {
            _quotes.Setup(q => q.GetAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("boom"));
            var provider = CreateProvider(_ => StubHttpMessageHandler.JsonResponse(Fmtqik));

            var result = await provider.FetchAsync();

            var taiex = Assert.Single(result.Quotes);
            Assert.Equal(-529.15m, taiex.Change);
        }

        [Fact]
        public async Task Taiex_BothSourcesFail_IsOmittedInsteadOfThrowing()
        {
            MisAnswers(null);
            var provider = CreateProvider(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

            var result = await provider.FetchAsync();

            Assert.Empty(result.Quotes);
            AssertNeverAskedYahooForTaiex();
        }

        [Fact]
        public async Task Taiex_FmtqikRowsWithoutUsableNumbers_AreSkipped()
        {
            MisAnswers(null);
            const string fmtqik = """
                [
                  {"Date":"1151019","TAIEX":"46822.08","Change":"-289.18"},
                  {"Date":"1151020","TAIEX":"","Change":""}
                ]
                """;
            var provider = CreateProvider(_ => StubHttpMessageHandler.JsonResponse(fmtqik));

            var result = await provider.FetchAsync();

            var taiex = Assert.Single(result.Quotes);
            Assert.Equal(46822.08m, taiex.Price);
            Assert.Equal(-289.18m, taiex.Change);
        }
    }
}
