using System.Net;
using System.Text.Json;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.Data.Models.SQL;
using JackyAIApp.Server.Services.Portfolio;
using JackyAIApp.Server.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace JackyAIApp.Server.Tests.Services.Portfolio
{
    /// <summary>
    /// 集保 publishes one settlement rate per business day, at a time it explicitly refuses to fix
    /// ("依每日批次作業完成時間而異"), and answers 404 for any date it has not published — which is
    /// the same answer for "too early today", "weekend", and "holiday". These tests pin that the
    /// service treats 404 as "look further back" rather than as an error, and that it never invents
    /// a rate for a date the source has no rate for.
    ///
    /// Response shapes are the real ones captured from fundclear.com.tw.
    /// </summary>
    public class TdccFxServiceTests : IDisposable
    {
        private readonly AzureSQLDBContext _db;
        private readonly TestDbContextFactory _dbFactory;

        public TdccFxServiceTests()
        {
            var options = new DbContextOptionsBuilder<AzureSQLDBContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            _db = new AzureSQLDBContext(options);
            _dbFactory = new TestDbContextFactory(options);
        }

        public void Dispose() => _db.Dispose();

        // Real payload for 2026/08/27, trimmed to three currencies.
        private const string Aug27 = """
        [{"currencyName":"日幣(JPY)","buyRate":"0.197400","sellRate":"0.200400"},
         {"currencyName":"歐元(EUR)","buyRate":"36.818000","sellRate":"37.038000"},
         {"currencyName":"美元(USD)","buyRate":"31.657500","sellRate":"31.712500"}]
        """;

        private const string Aug26 = """
        [{"currencyName":"美元(USD)","buyRate":"31.797500","sellRate":"31.857500"}]
        """;

        [Fact]
        public async Task GetBuyRate_PublishedDate_ParsesCodeOutOfDisplayNameAndKeepsSixDecimals()
        {
            var service = Create(_ => StubHttpMessageHandler.JsonResponse(Aug27));

            var rate = await service.GetBuyRateAsync("USD", new DateOnly(2026, 8, 27));

            // Six decimals matter: this is the number that has to reconcile against 基富通.
            Assert.Equal(31.657500m, rate);
        }

        [Fact]
        public async Task GetBuyRate_PersistsEveryCurrencyReturned_NotJustTheOneAskedFor()
        {
            // One request already carries all 14 currencies, so storing only the requested one
            // would mean re-fetching the same payload for the next fund in another currency.
            var service = Create(_ => StubHttpMessageHandler.JsonResponse(Aug27));
            await service.GetBuyRateAsync("USD", new DateOnly(2026, 8, 27));

            var stored = await _db.FxRates.OrderBy(r => r.Currency).ToListAsync();
            Assert.Equal(["EUR", "JPY", "USD"], stored.Select(r => r.Currency));
            Assert.All(stored, r => Assert.Equal(FxRateSources.Tdcc, r.Source));
            Assert.Equal(0.197400m, stored.Single(r => r.Currency == "JPY").BuyRate);
        }

        [Fact]
        public async Task GetBuyRate_NotYetPublishedToday_FallsBackToYesterday()
        {
            // The reported behaviour: open the app in the morning and you see yesterday's rate,
            // because today's settlement batch has not run. 基富通 does exactly this.
            var requested = new List<string>();
            var service = Create(req =>
            {
                var date = DateFromBody(req);
                requested.Add(date);
                return date == "2026/08/26"
                    ? StubHttpMessageHandler.JsonResponse(Aug26)
                    : new HttpResponseMessage(HttpStatusCode.NotFound);
            });

            var rate = await service.GetBuyRateAsync("USD", new DateOnly(2026, 8, 27));

            Assert.Equal(31.797500m, rate);
            Assert.Equal(["2026/08/27", "2026/08/26"], requested);
        }

        [Fact]
        public async Task GetBuyRate_NeverAsksForWeekendDates()
        {
            // Verified against the source: every Saturday and Sunday in August 2026 is a 404.
            // Probing them wastes two requests out of a bounded budget and can only ever fail.
            var requested = new List<string>();
            var service = Create(req =>
            {
                requested.Add(DateFromBody(req));
                return DateFromBody(req) == "2026/08/28"
                    ? StubHttpMessageHandler.JsonResponse(Aug26)
                    : new HttpResponseMessage(HttpStatusCode.NotFound);
            });

            // 2026/08/29 is a Saturday, 08/30 a Sunday — asking on Saturday must reach Friday.
            var rate = await service.GetBuyRateAsync("USD", new DateOnly(2026, 8, 29));

            Assert.Equal(31.797500m, rate);
            Assert.DoesNotContain("2026/08/29", requested);
            Assert.Equal("2026/08/28", requested[0]);
        }

        [Fact]
        public async Task GetBuyRate_AlreadyStored_DoesNotHitTheNetworkAgain()
        {
            var calls = 0;
            var service = Create(_ => { calls++; return StubHttpMessageHandler.JsonResponse(Aug27); });

            await service.GetBuyRateAsync("USD", new DateOnly(2026, 8, 27));
            await service.GetBuyRateAsync("USD", new DateOnly(2026, 8, 27));

            // A published rate never changes, so one fetch per date is all it can ever need.
            Assert.Equal(1, calls);
        }

        [Fact]
        public async Task GetBuyRate_InvertedBuySellPair_IsStoredAsPublished()
        {
            // 集保 published 2026/08/21 with sell BELOW buy. Whatever that is on their side, this
            // service records what was published rather than "correcting" it or rejecting the day.
            const string inverted = """
            [{"currencyName":"美元(USD)","buyRate":"31.892500","sellRate":"31.887500"}]
            """;
            var service = Create(_ => StubHttpMessageHandler.JsonResponse(inverted));

            var rate = await service.GetBuyRateAsync("USD", new DateOnly(2026, 8, 21));

            Assert.Equal(31.892500m, rate);
            var row = await _db.FxRates.SingleAsync();
            Assert.True(row.SellRate < row.BuyRate);
        }

        [Fact]
        public async Task GetBuyRate_NothingPublishedWithinCarryForwardWindow_ReturnsNull()
        {
            // Better to return null and let the caller fall back to a mid-market rate than to
            // value a holding at a rate from an unbounded distance in the past.
            var service = Create(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

            Assert.Null(await service.GetBuyRateAsync("USD", new DateOnly(2026, 8, 27)));
        }

        [Fact]
        public async Task GetBuyRate_UnknownCurrency_ReturnsNullWithoutInventingOne()
        {
            var service = Create(_ => StubHttpMessageHandler.JsonResponse(Aug27));

            Assert.Null(await service.GetBuyRateAsync("GBP", new DateOnly(2026, 8, 27)));
        }

        [Fact]
        public async Task GetBuyRate_SourceUnreachable_ReturnsNullRatherThanThrowing()
        {
            // An FX lookup must never be the thing that stops a portfolio from being valued.
            var service = Create(_ => throw new HttpRequestException("boom"));

            Assert.Null(await service.GetBuyRateAsync("USD", new DateOnly(2026, 8, 27)));
        }

        [Fact]
        public async Task GetBuyRateSeries_CarriesEachPublishedRateForwardOverGaps()
        {
            // History walks day by day, including the weekends and holidays that have no rate of
            // their own; each must value at the last rate actually published before it.
            _db.FxRates.AddRange(
                Row(new DateOnly(2026, 8, 26), 31.797500m),
                Row(new DateOnly(2026, 8, 27), 31.657500m));
            await _db.SaveChangesAsync();
            var service = Create(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

            var series = await service.GetBuyRateSeriesAsync(
                "USD", new DateOnly(2026, 8, 26), new DateOnly(2026, 8, 31));

            Assert.Equal(31.797500m, series[new DateOnly(2026, 8, 26)]);
            Assert.Equal(31.657500m, series[new DateOnly(2026, 8, 27)]);
            Assert.Equal(31.657500m, series[new DateOnly(2026, 8, 29)]); // Saturday
            Assert.Equal(31.657500m, series[new DateOnly(2026, 8, 31)]); // Monday, not yet published
        }

        [Fact]
        public async Task GetBuyRateSeries_DatesBeforeTheFirstPublishedRate_AreAbsentNotGuessed()
        {
            _db.FxRates.Add(Row(new DateOnly(2026, 8, 27), 31.657500m));
            await _db.SaveChangesAsync();
            var service = Create(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

            var series = await service.GetBuyRateSeriesAsync(
                "USD", new DateOnly(2026, 8, 25), new DateOnly(2026, 8, 27));

            // Backfilling a rate onto a date that predates anything we know would silently rewrite
            // history; the caller falls back to the snapshot's own stored rate instead.
            Assert.False(series.ContainsKey(new DateOnly(2026, 8, 25)));
            Assert.True(series.ContainsKey(new DateOnly(2026, 8, 27)));
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static FxRate Row(DateOnly date, decimal buy) => new()
        {
            RateDate = date, Currency = "USD", Source = FxRateSources.Tdcc,
            BuyRate = buy, SellRate = buy + 0.05m,
        };

        private static string DateFromBody(HttpRequestMessage req)
        {
            var json = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return JsonDocument.Parse(json).RootElement.GetProperty("dataDate").GetString()!;
        }

        private TdccFxService Create(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            var factory = new Mock<IHttpClientFactory>();
            factory.Setup(f => f.CreateClient(It.IsAny<string>()))
                .Returns(StubHttpMessageHandler.CreateClient(responder));
            return new TdccFxService(
                _dbFactory, factory.Object, new MemoryCache(new MemoryCacheOptions()),
                NullLogger<TdccFxService>.Instance);
        }
    }
}
