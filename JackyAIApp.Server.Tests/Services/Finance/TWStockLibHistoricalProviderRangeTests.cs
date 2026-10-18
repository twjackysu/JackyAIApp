using JackyAIApp.Server.Services.ExternalApi;
using JackyAIApp.Server.Services.Finance.DataProviders;
using JackyAIApp.Server.Services.Portfolio;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using TWStockLib.Models;
using TWStockLib.Services;

namespace JackyAIApp.Server.Tests.Services.Finance
{
    /// <summary>
    /// A date range of TW history goes to the exchange one month at a time, each month through the
    /// <see cref="IThirdPartyGateway"/> — so a long range is paced and the same stock-month asked for
    /// twice at once is fetched once — while the result is still exactly what one ranged call gave.
    /// </summary>
    public class TWStockLibHistoricalProviderRangeTests
    {
        private readonly Mock<IStockMarketService> _stockService = new();
        private readonly Mock<IThirdPartyGateway> _spyGateway = new();
        private readonly List<(string Source, string Key, GatewayPriority Priority)> _requests = [];

        private TWStockLibHistoricalProvider Provider(IThirdPartyGateway? gateway = null) => new(
            _stockService.Object, Mock.Of<ITwListedCompanyService>(), new MemoryCache(new MemoryCacheOptions()),
            gateway ?? RealGateway(), NullLogger<TWStockLibHistoricalProvider>.Instance);

        /// <summary>The real gateway with the default limits (a burst far bigger than any test uses).</summary>
        private static ThirdPartyGateway RealGateway()
        {
            var options = new Mock<IOptionsMonitor<ThirdPartyGatewayOptions>>();
            options.Setup(o => o.CurrentValue).Returns(new ThirdPartyGatewayOptions());
            return new ThirdPartyGateway(options.Object, TimeProvider.System, NullLogger<ThirdPartyGateway>.Instance);
        }

        // StockHistory is built from the exchange's own row: date, volume, turnover, O, H, L, C, change, deals.
        private static StockHistory Bar(DateTime day, decimal close = 100m) => new(
            [day.ToString("yyyy-MM-dd"), "1000", "100000", $"{close}", $"{close}", $"{close}", $"{close}", "0", "10"]);

        private void MonthReturns(string symbol, MarketType market, int year, int month, params StockHistory[] bars) =>
            _stockService
                .Setup(s => s.GetHistoricalDataAsync(symbol, new DateTime(year, month, 1),
                    new DateTime(year, month, 1).AddMonths(1).AddDays(-1), market, It.IsAny<CancellationToken>()))
                .ReturnsAsync(StockResult<IReadOnlyList<StockHistory>>.Ok(bars));

        [Fact]
        public async Task ARangeIsFetchedOneWholeMonthAtATime_AndTheResultIsTheDaysInTheRange()
        {
            MonthReturns("2330", MarketType.TSE, 2026, 8, Bar(new(2026, 8, 3), 10), Bar(new(2026, 8, 31), 11));
            MonthReturns("2330", MarketType.TSE, 2026, 9, Bar(new(2026, 9, 1), 12), Bar(new(2026, 9, 30), 13));
            MonthReturns("2330", MarketType.TSE, 2026, 10, Bar(new(2026, 10, 1), 14), Bar(new(2026, 10, 16), 15));

            var data = await Provider().FetchRangeAsync(
                "2330", new DateTime(2026, 8, 15), new DateTime(2026, 10, 10), MarketType.TSE);

            // The cut is the range's own: 08-03 is before it, 10-16 after it.
            Assert.Equal(
                [new DateTime(2026, 8, 31), new DateTime(2026, 9, 1), new DateTime(2026, 9, 30), new DateTime(2026, 10, 1)],
                data.HistoricalPrices.Select(p => p.Date));
            _stockService.Verify(s => s.GetHistoricalDataAsync(
                "2330", It.IsAny<DateTime>(), It.IsAny<DateTime>(), MarketType.TSE, It.IsAny<CancellationToken>()), Times.Exactly(3));
        }

        [Fact]
        public async Task ARangeInsideOneMonth_IsOneRequest()
        {
            MonthReturns("2330", MarketType.TSE, 2026, 10, Bar(new(2026, 10, 14)), Bar(new(2026, 10, 15)));

            var data = await Provider().FetchRangeAsync(
                "2330", new DateTime(2026, 10, 12), new DateTime(2026, 10, 16), MarketType.TSE);

            Assert.Equal(2, data.HistoricalPrices.Count);
            _stockService.Verify(s => s.GetHistoricalDataAsync(
                It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<MarketType>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task EachMonthGoesThroughTheGateway_OnTheLaneOfItsMarket_AsAnInteractiveRequest()
        {
            _spyGateway
                .Setup(g => g.RunAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<GatewayPriority>(),
                    It.IsAny<Func<CancellationToken, Task<StockResult<IReadOnlyList<StockHistory>>>>>(), It.IsAny<CancellationToken>()))
                .Returns((string source, string key, GatewayPriority priority,
                    Func<CancellationToken, Task<StockResult<IReadOnlyList<StockHistory>>>> fetch, CancellationToken _) =>
                {
                    _requests.Add((source, key, priority));
                    return Task.FromResult(StockResult<IReadOnlyList<StockHistory>>.Ok([]));
                });

            await Provider(_spyGateway.Object).FetchRangeAsync("6488", new DateTime(2026, 9, 10), new DateTime(2026, 10, 10), MarketType.OTC);
            await Provider(_spyGateway.Object).FetchRangeAsync("2330", new DateTime(2026, 10, 1), new DateTime(2026, 10, 10), MarketType.TSE);

            Assert.Equal(
                [
                    (ThirdPartySources.TpexHistory, "OTC:6488:202609", GatewayPriority.Interactive),
                    (ThirdPartySources.TpexHistory, "OTC:6488:202610", GatewayPriority.Interactive),
                    (ThirdPartySources.TwseHistory, "TSE:2330:202610", GatewayPriority.Interactive),
                ],
                _requests);
        }

        [Fact]
        public async Task TheSameStockMonthAskedForAtOnce_IsFetchedOnce()
        {
            var release = new TaskCompletionSource();
            var calls = 0;
            _stockService
                .Setup(s => s.GetHistoricalDataAsync("2330", It.IsAny<DateTime>(), It.IsAny<DateTime>(), MarketType.TSE, It.IsAny<CancellationToken>()))
                .Returns(async () =>
                {
                    Interlocked.Increment(ref calls);
                    await release.Task;
                    return StockResult<IReadOnlyList<StockHistory>>.Ok([Bar(new(2026, 10, 14))]);
                });
            var gateway = RealGateway();

            // Two requests (two page loads, say) for the same month of the same stock.
            var first = Provider(gateway).FetchRangeAsync("2330", new DateTime(2026, 10, 1), new DateTime(2026, 10, 16), MarketType.TSE);
            var second = Provider(gateway).FetchRangeAsync("2330", new DateTime(2026, 10, 5), new DateTime(2026, 10, 20), MarketType.TSE);
            while (gateway.GetStats(ThirdPartySources.TwseHistory).Running == 0) await Task.Delay(5);
            await Task.Delay(50);
            release.SetResult();
            await Task.WhenAll(first, second);

            Assert.Equal(1, calls);
        }

        [Fact]
        public async Task IfAnyMonthFails_TheWholeRangeFails_AsItDidWhenTheRangeWasOneCall()
        {
            MonthReturns("2330", MarketType.TSE, 2026, 9, Bar(new(2026, 9, 1)));
            _stockService
                .Setup(s => s.GetHistoricalDataAsync("2330", new DateTime(2026, 10, 1), It.IsAny<DateTime>(), MarketType.TSE, It.IsAny<CancellationToken>()))
                .ReturnsAsync(StockResult<IReadOnlyList<StockHistory>>.Fail(StockErrorCodes.UpstreamError, "503"));

            var data = await Provider().FetchRangeAsync("2330", new DateTime(2026, 9, 1), new DateTime(2026, 10, 10), MarketType.TSE);

            // Not the September bar alone: a history with October missing must not look complete.
            Assert.Empty(data.HistoricalPrices);
        }

        [Fact]
        public async Task FetchMonth_ReturnsTheWholeMonthAsDelivered_WithoutDaysThatHaveNoClose()
        {
            MonthReturns("2330", MarketType.TSE, 2026, 8,
                Bar(new(2026, 8, 31), 11), Bar(new(2026, 8, 3), 10), Bar(new(2026, 8, 4), 0));

            var month = await Provider().FetchMonthAsync("2330", new DateOnly(2026, 8, 1), MarketType.TSE, GatewayPriority.Background);

            Assert.True(month.Succeeded);
            Assert.Equal([new DateTime(2026, 8, 3), new DateTime(2026, 8, 31)], month.Bars.Select(b => b.Date));
            Assert.Equal(1000, month.Bars[0].Volume);   // as delivered; the exchange's own unit
        }

        [Fact]
        public async Task FetchMonth_ForAMonthWithNoTrading_SucceedsWithNothing()
        {
            MonthReturns("2330", MarketType.TSE, 2026, 8);

            var month = await Provider().FetchMonthAsync("2330", new DateOnly(2026, 8, 1), MarketType.TSE, GatewayPriority.Background);

            Assert.True(month.Succeeded);
            Assert.Empty(month.Bars);
        }

        [Fact]
        public async Task FetchMonth_TellsAMonthThatFailedFromOneThatWasEmpty()
        {
            _stockService
                .Setup(s => s.GetHistoricalDataAsync("2330", It.IsAny<DateTime>(), It.IsAny<DateTime>(), MarketType.TSE, It.IsAny<CancellationToken>()))
                .ReturnsAsync(StockResult<IReadOnlyList<StockHistory>>.Fail(StockErrorCodes.UpstreamError, "503"));

            var month = await Provider().FetchMonthAsync("2330", new DateOnly(2026, 8, 1), MarketType.TSE, GatewayPriority.Background);

            Assert.False(month.Succeeded);
        }

        [Fact]
        public async Task FetchMonth_GoesThroughTheGatewayWithTheCallersPriority_OnTheLaneOfItsMarket()
        {
            _spyGateway
                .Setup(g => g.RunAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<GatewayPriority>(),
                    It.IsAny<Func<CancellationToken, Task<StockResult<IReadOnlyList<StockHistory>>>>>(), It.IsAny<CancellationToken>()))
                .Returns((string source, string key, GatewayPriority priority,
                    Func<CancellationToken, Task<StockResult<IReadOnlyList<StockHistory>>>> fetch, CancellationToken _) =>
                {
                    _requests.Add((source, key, priority));
                    return Task.FromResult(StockResult<IReadOnlyList<StockHistory>>.Ok([]));
                });

            await Provider(_spyGateway.Object).FetchMonthAsync("6488", new DateOnly(2026, 8, 17), MarketType.OTC, GatewayPriority.Background);

            // The same key a ranged fetch of that month uses, so the two share one request.
            Assert.Equal([(ThirdPartySources.TpexHistory, "OTC:6488:202608", GatewayPriority.Background)], _requests);
        }

        [Fact]
        public async Task FetchMonth_AGatewayFailure_IsAFailedMonth_NotAnException()
        {
            _spyGateway
                .Setup(g => g.RunAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<GatewayPriority>(),
                    It.IsAny<Func<CancellationToken, Task<StockResult<IReadOnlyList<StockHistory>>>>>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new TimeoutException("twse-history did not answer"));

            var month = await Provider(_spyGateway.Object).FetchMonthAsync(
                "2330", new DateOnly(2026, 8, 1), MarketType.TSE, GatewayPriority.Background);

            Assert.False(month.Succeeded);
        }

        [Fact]
        public async Task FetchMonth_WhenTheCallerCancels_ThatIsNotSwallowedAsAFailedMonth()
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            _spyGateway
                .Setup(g => g.RunAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<GatewayPriority>(),
                    It.IsAny<Func<CancellationToken, Task<StockResult<IReadOnlyList<StockHistory>>>>>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new OperationCanceledException(cts.Token));

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Provider(_spyGateway.Object).FetchMonthAsync(
                "2330", new DateOnly(2026, 8, 1), MarketType.TSE, GatewayPriority.Background, cts.Token));
        }

        [Fact]
        public async Task AGatewayFailure_IsAnEmptyResult_NotAnException()
        {
            // Same contract as before: this method never throws for an upstream problem.
            _spyGateway
                .Setup(g => g.RunAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<GatewayPriority>(),
                    It.IsAny<Func<CancellationToken, Task<StockResult<IReadOnlyList<StockHistory>>>>>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new TimeoutException("twse-history did not answer"));

            var data = await Provider(_spyGateway.Object).FetchRangeAsync(
                "2330", new DateTime(2026, 10, 1), new DateTime(2026, 10, 10), MarketType.TSE);

            Assert.Empty(data.HistoricalPrices);
        }
    }
}
