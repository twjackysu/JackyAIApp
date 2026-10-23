using JackyAIApp.Server.DTO.Finance;
using JackyAIApp.Server.Services.Market;
using JackyAIApp.Server.Tests.TestHelpers;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace JackyAIApp.Server.Tests.Services.Finance
{
    /// <summary>
    /// Covers InvalidateAsync — every registered <see cref="IMarketDataProvider"/> is wrapped by
    /// this decorator (see FinanceServiceExtensions), so a no-op override here would silently
    /// swallow any caller's invalidation request before it ever reaches the inner provider. This is
    /// exactly the bug that made Portfolio's "重新抓取報價" reload button do nothing for 00864B:
    /// the request reached CachedMarketDataProvider, hit the interface's default no-op, and never
    /// reset PersistentTwHistoricalProvider's stuck DB-persisted market type.
    /// </summary>
    public class CachedMarketDataProviderTests
    {
        private const string Symbol = "00864B";

        [Fact]
        public async Task AnEntry_IsForTheDayInTaipei_SoANewOneStartsAtMidnightThere()
        {
            var inner = new Mock<IMarketDataProvider>();
            inner.Setup(p => p.Type).Returns(DataProviderType.HistoricalPrice);
            inner.Setup(p => p.FetchLatestAsync(Symbol, It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => new MarketData { StockCode = Symbol, HistoricalPrices = [new DailyPrice { Date = DateTime.UtcNow, Close = 47m }] });
            var clock = new TestClock(TestClock.Taipei(new DateOnly(2026, 10, 21), 23, 50));
            var provider = new CachedMarketDataProvider(
                inner.Object, new MemoryCache(new MemoryCacheOptions()), NullLogger<CachedMarketDataProvider>.Instance,
                timeProvider: clock);

            await provider.FetchLatestAsync(Symbol);
            clock.Now = TestClock.Taipei(new DateOnly(2026, 10, 22), 0, 10);   // still the 21st in UTC
            await provider.FetchLatestAsync(Symbol);
            await provider.InvalidateAsync(Symbol);   // clears the 22nd's entry, the one now in use
            await provider.FetchLatestAsync(Symbol);

            inner.Verify(p => p.FetchLatestAsync(Symbol, It.IsAny<CancellationToken>()), Times.Exactly(3));
        }

        [Fact]
        public async Task InvalidateAsync_DelegatesToInnerProvider()
        {
            var inner = new Mock<IMarketDataProvider>();
            inner.Setup(p => p.InvalidateAsync(Symbol, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            var cache = new MemoryCache(new MemoryCacheOptions());
            var provider = new CachedMarketDataProvider(inner.Object, cache, NullLogger<CachedMarketDataProvider>.Instance);

            await provider.InvalidateAsync(Symbol);

            inner.Verify(p => p.InvalidateAsync(Symbol, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task InvalidateAsync_ClearsCachedEntry_SoNextFetchHitsInnerAgain()
        {
            var inner = new Mock<IMarketDataProvider>();
            inner.Setup(p => p.Type).Returns(DataProviderType.HistoricalPrice);
            var staleData = new MarketData
            {
                StockCode = Symbol,
                HistoricalPrices = [new DailyPrice { Date = DateTime.UtcNow, Close = 0 }],
            };
            var freshData = new MarketData
            {
                StockCode = Symbol,
                HistoricalPrices = [new DailyPrice { Date = DateTime.UtcNow, Close = 47.22m }],
            };
            inner.SetupSequence(p => p.FetchLatestAsync(Symbol, It.IsAny<CancellationToken>()))
                .ReturnsAsync(staleData)
                .ReturnsAsync(freshData);
            inner.Setup(p => p.InvalidateAsync(Symbol, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            var cache = new MemoryCache(new MemoryCacheOptions());
            var provider = new CachedMarketDataProvider(inner.Object, cache, NullLogger<CachedMarketDataProvider>.Instance);

            var first = await provider.FetchLatestAsync(Symbol);
            Assert.Equal(0m, first.HistoricalPrices[0].Close);

            // Without invalidation this would still be served from the 4h cache — assert the
            // cache-hit path actually would fire by NOT invalidating first isn't the point here;
            // instead prove invalidation busts it.
            await provider.InvalidateAsync(Symbol);
            var second = await provider.FetchLatestAsync(Symbol);

            Assert.Equal(47.22m, second.HistoricalPrices[0].Close);
            inner.Verify(p => p.FetchLatestAsync(Symbol, It.IsAny<CancellationToken>()), Times.Exactly(2));
        }
    }
}
