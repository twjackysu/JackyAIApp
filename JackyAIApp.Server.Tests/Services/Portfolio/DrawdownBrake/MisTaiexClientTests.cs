using JackyAIApp.Server.Services.Portfolio.DrawdownBrake;
using JackyAIApp.Server.Tests.TestHelpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TWStockLib.Models;
using TWStockLib.Services;
using TWStockLib.Twse.Http;

namespace JackyAIApp.Server.Tests.Services.Portfolio.DrawdownBrake
{
    /// <summary>The index through TWStockLib: what is asked, what is believed, and that nothing stands between the site and MIS.</summary>
    public class MisTaiexClientTests
    {
        // 2026-10-12 13:25:05 Taipei = 05:25:05 UTC; TWStockLib hands it over as UTC wall-clock time, Kind unspecified.
        private static readonly DateTime SyncTime = new(2026, 10, 12, 5, 25, 5, DateTimeKind.Unspecified);

        private static StockQuote Quote(
            decimal? last = 23100.12m, decimal? yesterday = 22950.00m, DateTime? sync = null, bool noSync = false, string? symbol = "t00") =>
            new()
            {
                Symbol = symbol!,
                Name = "發行量加權股價指數",
                Market = MarketType.TSE,
                LastPrice = last,
                YesterdayClosingPrice = yesterday,
                SyncTime = noSync ? null : sync ?? SyncTime,
            };

        // ── with the library mocked ─────────────────────────────────────────

        public class WithAMockedLibrary
        {
            private readonly Mock<IStockMarketService> _stocks = new();

            private MisTaiexClient Create(TimeSpan? timeout = null)
            {
                var services = new ServiceCollection();
                // As the library registers it: scoped.
                services.AddScoped(_ => _stocks.Object);
                return new MisTaiexClient(
                    services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
                    NullLogger<MisTaiexClient>.Instance, timeout);
            }

            private void LibraryAnswers(StockResult<StockQuote> result) =>
                _stocks.Setup(s => s.GetRealtimeQuoteAsync(It.IsAny<string>(), It.IsAny<MarketType>(), true, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(result);

            [Fact]
            public async Task AsksForTheWeightedIndex_AsAListedSymbol_SkippingTheLibrarysCache()
            {
                LibraryAnswers(StockResult<StockQuote>.Ok(Quote()));

                var quote = await Create().GetAsync();

                // bypassCache: true — the library's own 30-second cache must not stand between the site and MIS.
                _stocks.Verify(s => s.GetRealtimeQuoteAsync("t00", MarketType.TSE, true, It.IsAny<CancellationToken>()), Times.Once);
                _stocks.Verify(s => s.GetRealtimeQuoteAsync(It.IsAny<string>(), It.IsAny<MarketType>(), It.IsAny<CancellationToken>()), Times.Never);
                Assert.Equal(23100.12m, quote!.Last);
                Assert.Equal(22950.00m, quote.PreviousClose);
            }

            [Fact]
            public async Task TheLibrarysTime_IsTreatedAsUtc_AndTheReadingIsInTaipeiTime()
            {
                LibraryAnswers(StockResult<StockQuote>.Ok(Quote()));

                var quote = await Create().GetAsync();

                Assert.Equal(new DateTimeOffset(2026, 10, 12, 5, 25, 5, TimeSpan.Zero), quote!.QuotedAt);
                Assert.Equal(TimeSpan.FromHours(8), quote.QuotedAt.Offset);
                Assert.Equal(new DateTime(2026, 10, 12, 13, 25, 5), quote.QuotedAt.DateTime);
                Assert.Equal(new DateOnly(2026, 10, 12), quote.TradeDate);
            }

            [Fact]
            public async Task TheTradeDate_IsTheTaipeiDay_NotTheUtcDay()
            {
                // 16:30 UTC on the 8th is 00:30 on the 9th in Taipei.
                LibraryAnswers(StockResult<StockQuote>.Ok(Quote(sync: new DateTime(2026, 10, 8, 16, 30, 0))));

                var quote = await Create().GetAsync();

                Assert.Equal(new DateOnly(2026, 10, 9), quote!.TradeDate);
            }

            [Fact]
            public async Task EveryCall_OpensItsOwnScope_BecauseTheLibraryServiceIsScoped()
            {
                LibraryAnswers(StockResult<StockQuote>.Ok(Quote()));
                var scopes = 0;
                var services = new ServiceCollection();
                services.AddScoped(_ => { scopes++; return _stocks.Object; });
                var client = new MisTaiexClient(
                    services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(), NullLogger<MisTaiexClient>.Instance);

                await client.GetAsync();
                await client.GetAsync();

                Assert.Equal(2, scopes);
            }

            [Theory]
            [InlineData(StockErrorCodes.NotFound)]
            [InlineData(StockErrorCodes.UpstreamError)]
            [InlineData(StockErrorCodes.SourceNotFound)]
            public async Task ALibraryFailure_IsNoReading_NotAnException(string code)
            {
                LibraryAnswers(StockResult<StockQuote>.Fail(code, "nope"));

                Assert.Null(await Create().GetAsync());
            }

            [Fact]
            public async Task ALibraryThatThrows_IsNoReading_NotAnException()
            {
                _stocks.Setup(s => s.GetRealtimeQuoteAsync(It.IsAny<string>(), It.IsAny<MarketType>(), true, It.IsAny<CancellationToken>()))
                    .ThrowsAsync(new HttpRequestException("connection reset"));

                Assert.Null(await Create().GetAsync());
            }

            [Fact]
            public async Task ALibraryThatNeverAnswers_IsGivenUpOnAtTheTimeLimit()
            {
                _stocks.Setup(s => s.GetRealtimeQuoteAsync(It.IsAny<string>(), It.IsAny<MarketType>(), true, It.IsAny<CancellationToken>()))
                    .Returns(new TaskCompletionSource<StockResult<StockQuote>>().Task);

                var started = DateTime.UtcNow;
                Assert.Null(await Create(TimeSpan.FromMilliseconds(50)).GetAsync());

                Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(20), "the request was not cut off");
            }

            [Fact]
            public async Task TheCallersOwnCancellation_IsStillACancellation()
            {
                _stocks.Setup(s => s.GetRealtimeQuoteAsync(It.IsAny<string>(), It.IsAny<MarketType>(), true, It.IsAny<CancellationToken>()))
                    .Returns(new TaskCompletionSource<StockResult<StockQuote>>().Task);
                using var cts = new CancellationTokenSource();

                var pending = Create(TimeSpan.FromMinutes(1)).GetAsync(cts.Token);
                await cts.CancelAsync();

                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
            }

            [Fact]
            public void TheTimeout_IsShort_BecauseAPersonIsWaitingBehindIt()
            {
                Assert.True(MisTaiexClient.DefaultTimeout <= TimeSpan.FromSeconds(10));
            }
        }

        // ── what is believed ────────────────────────────────────────────────

        public class WhatIsBelieved
        {
            private static MisIndexQuote? Read(StockQuote quote, out string? rejection) =>
                MisTaiexClient.Read(StockResult<StockQuote>.Ok(quote), out rejection);

            [Theory]
            [InlineData(null)]   // before the day's first calculation ("-")
            public void NoIndexValueYet_IsNoReading_AndNotAnError(int? last)
            {
                Assert.Null(Read(Quote(last: last), out var rejection));
                Assert.Null(rejection); // the ordinary state at the open, not worth a warning
            }

            [Theory]
            [InlineData(0)]
            [InlineData(-1)]
            public void ANonPositiveIndexValue_IsRejected(int last)
            {
                Assert.Null(Read(Quote(last: last), out var rejection));
                Assert.NotNull(rejection);
            }

            [Theory]
            [InlineData(null)]
            [InlineData(0)]      // what the library makes of a missing or "-" y
            [InlineData(-5)]
            public void NoUsablePreviousClose_IsRejected(int? yesterday)
            {
                Assert.Null(Read(Quote(yesterday: yesterday), out var rejection));
                Assert.NotNull(rejection);
            }

            [Fact]
            public void NoTime_IsRejected_BecauseTheEstimateMustSayHowOldItIs()
            {
                Assert.Null(Read(Quote(noSync: true), out var rejection));
                Assert.NotNull(rejection);
            }

            [Theory]
            [InlineData("2330")]
            [InlineData("")]
            [InlineData(null)]
            public void AnAnswerAboutSomethingElse_IsRejected(string? symbol)
            {
                Assert.Null(Read(Quote(symbol: symbol), out var rejection));
                Assert.NotNull(rejection);
            }

            [Fact]
            public void TheIndexCode_IsMatchedIgnoringCase()
            {
                Assert.NotNull(Read(Quote(symbol: "T00"), out _));
            }

            [Theory]
            [InlineData(115, 100)]  // +15% exactly: believed
            [InlineData(85, 100)]   // −15% exactly: believed
            public void TheLargestBelievedMove_IsFifteenPercent_Inclusive(int last, int yesterday)
            {
                Assert.NotNull(Read(Quote(last: last, yesterday: yesterday), out _));
            }

            [Theory]
            [InlineData(11501, 10000)]  // +15.01%
            [InlineData(8499, 10000)]   // −15.01%
            [InlineData(23100, 100)]    // a unit mix-up
            public void AMoveBeyondFifteenPercent_IsBadData_NotNews(int last, int yesterday)
            {
                Assert.Null(Read(Quote(last: last, yesterday: yesterday), out var rejection));
                Assert.Contains("move", rejection);
            }
        }

        // ── through the real library, with MIS's recorded answer ────────────

        public class ThroughTheRealLibrary
        {
            private sealed class FakeFetcher(Func<string> body) : IStockHttpFetcher
            {
                public List<string> Urls { get; } = [];

                public Task<string> GetStringAsync(string url, CancellationToken ct = default)
                {
                    Urls.Add(url);
                    return Task.FromResult(body());
                }

                public Task<string> GetBig5StringAsync(string url, CancellationToken ct = default) => throw new NotSupportedException();
            }

            private static (MisTaiexClient Client, FakeFetcher Fetcher, ServiceProvider Provider) Build(Func<string> body)
            {
                var fetcher = new FakeFetcher(body);
                var services = new ServiceCollection();
                services.AddLogging();
                services.AddTwStock();
                services.AddSingleton<IStockHttpFetcher>(fetcher); // after AddTwStock: replaces the real HTTP fetcher
                var provider = services.BuildServiceProvider();
                var client = new MisTaiexClient(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<MisTaiexClient>.Instance);
                return (client, fetcher, provider);
            }

            [Fact]
            public async Task TheRecordedRealAnswer_IsReadIntoTheReadingTheEstimateNeeds()
            {
                var (client, fetcher, provider) = Build(() => MisTaiexSamples.RecordedJson());
                using var _ = provider;

                var quote = await client.GetAsync();

                Assert.Contains("ex_ch=tse_t00.tw", Assert.Single(fetcher.Urls));
                Assert.Equal(49313.44m, quote!.Last);
                Assert.Equal(49806.37m, quote.PreviousClose);
                Assert.Equal(MisTaiexSamples.RecordedQuotedAt, quote.QuotedAt);
                Assert.Equal(new DateOnly(2026, 10, 8), quote.TradeDate);
            }

            [Fact]
            public async Task BeforeTheFirstTrade_ZIsADash_AndThereIsNoReading()
            {
                var (client, _, provider) = Build(() => MisTaiexSamples.RecordedJson(z: "-"));
                using var _ = provider;

                Assert.Null(await client.GetAsync());
            }

            [Fact]
            public async Task ADifferentCodeInTheAnswer_IsNoReading()
            {
                var (client, _, provider) = Build(() => MisTaiexSamples.RecordedJson(c: "2330"));
                using var _ = provider;

                Assert.Null(await client.GetAsync());
            }

            [Fact]
            public async Task EveryCall_ReallyGoesToMis_NotToTheLibrarysThirtySecondCache()
            {
                var price = 49000m;
                var (client, fetcher, provider) = Build(() => MisTaiexSamples.RecordedJson(z: (price += 10).ToString("0.00")));
                using var _ = provider;

                var first = await client.GetAsync();
                var second = await client.GetAsync(); // moments later, well inside the library's 30 seconds

                Assert.Equal(2, fetcher.Urls.Count);
                Assert.Equal(49010m, first!.Last);
                Assert.Equal(49020m, second!.Last);
            }

            [Fact]
            public async Task WithoutBypassCache_TheLibraryWouldHaveServedTheOldReading()
            {
                // The control for the test above: this is why the client must bypass.
                var price = 49000m;
                var (_, fetcher, provider) = Build(() => MisTaiexSamples.RecordedJson(z: (price += 10).ToString("0.00")));
                using var __ = provider;
                using var scope = provider.CreateScope();
                var stocks = scope.ServiceProvider.GetRequiredService<IStockMarketService>();

                var first = await stocks.GetRealtimeQuoteAsync("t00", MarketType.TSE);
                var second = await stocks.GetRealtimeQuoteAsync("t00", MarketType.TSE);

                Assert.Single(fetcher.Urls);
                Assert.Equal(first.Value!.LastPrice, second.Value!.LastPrice);
            }
        }
    }
}
