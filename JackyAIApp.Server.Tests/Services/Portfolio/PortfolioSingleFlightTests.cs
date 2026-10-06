using JackyAIApp.Server.Data;
using JackyAIApp.Server.Data.Models.Portfolio;
using JackyAIApp.Server.DTO.Finance;
using JackyAIApp.Server.DTO.Portfolio;
using JackyAIApp.Server.Services;
using JackyAIApp.Server.Services.Finance.DataProviders;
using JackyAIApp.Server.Services.OpenAI;
using JackyAIApp.Server.Services.Portfolio;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace JackyAIApp.Server.Tests.Services.Portfolio
{
    /// <summary>
    /// TASK-025: cold-cache single-flight de-duplication, shallow/deep stock-fetch sharing, and
    /// the short-TTL snapshots cache in PortfolioService. See SingleFlightTests for coverage of
    /// the underlying generic de-dup mechanism itself; this file covers how PortfolioService wires
    /// it up.
    /// </summary>
    [Collection(SingleFlightCollection.Name)]
    public class PortfolioSingleFlightTests : IDisposable
    {
        private const string TestUserId = "single-flight-test-user";

        // A fixed Monday, so date-range walks (GetNetWorthHistoryAsync) never land on a weekend
        // depending on which real-world day this test happens to run.
        private static readonly DateOnly FixedToday = new(2026, 1, 5);

        private readonly AzureSQLDBContext _dbContext;
        private readonly PortfolioService _service;
        private readonly Mock<IMarketDataProviderFactory> _providerFactory = new();
        private readonly Mock<IMarketDataProvider> _provider = new();
        private readonly IMemoryCache _cache;

        public PortfolioSingleFlightTests()
        {
            var options = new DbContextOptionsBuilder<AzureSQLDBContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;
            _dbContext = new AzureSQLDBContext(options);

            _providerFactory.Setup(f => f.DetectRegion(It.IsAny<string>())).Returns(MarketRegion.TW);
            _providerFactory.Setup(f => f.GetMarketDataProvider(MarketRegion.TW)).Returns(_provider.Object);

            var fundData = new Mock<IFundDataService>();
            var dividendData = new Mock<IDividendService>();
            dividendData.Setup(x => x.GetStockDividendsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync([]);
            dividendData.Setup(x => x.GetFundDividendsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync([]);
            var httpClientFactory = new Mock<IHttpClientFactory>();
            _cache = new MemoryCache(new MemoryCacheOptions());
            var openAI = new Mock<IOpenAIPromptService>();
            var usageLimitService = new Mock<IUsageLimitService>();
            var categoryService = new Mock<IStockCategoryService>();

            _service = new PortfolioService(
                _dbContext,
                _providerFactory.Object,
                fundData.Object,
                dividendData.Object,
                categoryService.Object,
                twListedCompanies: null!,
                httpClientFactory.Object,
                _cache,
                openAI.Object,
                usageLimitService.Object,
                NullLogger<PortfolioService>.Instance,
                new FakeTimeProvider(new DateTimeOffset(FixedToday.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)));
        }

        private sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
        {
            public override DateTimeOffset GetUtcNow() => now;
        }

        public void Dispose() => _dbContext.Dispose();

        // ── Stock-price single-flight ───────────────────────────────────────

        [Fact]
        public async Task LookupStockAsync_ConcurrentCallsSameSymbol_OnlyFetchesUpstreamOnce()
        {
            // SingleFlight's de-dup table is process-wide by design (see its own doc comment) —
            // correct for production (the same symbol really is the same upstream fetch no matter
            // which request triggers it), but it means a symbol held "in flight" here for any
            // length of time is a shared key every other test in this assembly could collide with
            // if xUnit happens to run them concurrently. A GUID-based symbol can never collide with
            // another test's fixture data (a real bug this exact test caught once already — see
            // the FundDataService/DividendService concurrency tests for the same reasoning).
            var symbol = $"SF{Guid.NewGuid():N}"[..12].ToUpperInvariant();
            var callCount = 0;
            var gate = new TaskCompletionSource();
            var data = new MarketData
            {
                StockCode = symbol,
                HistoricalPrices = [new DailyPrice { Date = DateTime.UtcNow, Close = 500m }],
            };
            _provider.Setup(p => p.FetchLatestAsync(symbol, It.IsAny<CancellationToken>()))
                .Returns(async () =>
                {
                    Interlocked.Increment(ref callCount);
                    await gate.Task;
                    return data;
                });

            var calls = Enumerable.Range(0, 5)
                .Select(_ => _service.LookupStockAsync("TW", symbol))
                .ToList();

            await Task.Delay(50); // let every call reach the cache-miss/single-flight path first
            gate.SetResult();
            var results = await Task.WhenAll(calls);

            Assert.Equal(1, callCount);
            Assert.All(results, r => Assert.Equal(500m, r!.CurrentPrice));
        }

        // ── Shallow/deep sharing ─────────────────────────────────────────────

        [Fact]
        public async Task LookupStockAsync_AfterDeepFetchAlreadyCached_ReusesItWithoutALatestOnlyFetch()
        {
            // Unique symbol — see the comment in the test above for why a shared fixture symbol
            // (e.g. "2330") would risk colliding with another test through SingleFlight's
            // process-wide de-dup table.
            var symbol = $"SF{Guid.NewGuid():N}"[..12].ToUpperInvariant();
            var data = new MarketData
            {
                StockCode = symbol,
                HistoricalPrices =
                [
                    new DailyPrice { Date = DateTime.UtcNow.AddDays(-1), Close = 490m },
                    new DailyPrice { Date = DateTime.UtcNow, Close = 500m },
                ],
            };
            _provider.Setup(p => p.FetchAsync(symbol, It.IsAny<CancellationToken>())).ReturnsAsync(data);
            await SeedSnapshotAsync(new HoldingPosition
            {
                AssetType = AssetType.STOCK, Symbol = symbol, Name = "台積電",
                Market = "TW", Currency = "TWD", Quantity = 10, CostBasis = 4_000,
            });

            // Deep fetch (price-history chart) populates the deep_ cache.
            var history = await _service.GetHoldingPriceHistoryAsync(TestUserId, "TW", symbol);
            Assert.NotNull(history);

            // A shallow caller for the same symbol must be served from the deep cache instead of
            // issuing its own latest-only fetch.
            var lookup = await _service.LookupStockAsync("TW", symbol);

            Assert.Equal(500m, lookup!.CurrentPrice);
            _provider.Verify(p => p.FetchLatestAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
            _provider.Verify(p => p.FetchAsync(symbol, It.IsAny<CancellationToken>()), Times.Once);
        }

        // ── Snapshots cache (AllSnapshotsAsync) ─────────────────────────────

        [Fact]
        public async Task GetNetWorthHistoryAsync_WarmSnapshotsCache_UsesCachedSnapshotsInsteadOfDb()
        {
            // Nothing seeded in the DB — the only way this can return data is the pre-populated cache.
            var cachedSnapshot = new HoldingSnapshot
            {
                UserId = TestUserId,
                EffectiveDate = FixedToday,
                UsdTwdRate = 32m,
                Positions = [new HoldingPosition { AssetType = AssetType.CASH, Currency = "TWD", Quantity = 12_345 }],
            };
            _cache.Set("portfolio_snapshots_" + TestUserId, new List<HoldingSnapshot> { cachedSnapshot }, TimeSpan.FromSeconds(30));

            var history = await _service.GetNetWorthHistoryAsync(TestUserId, 7);

            Assert.NotEmpty(history);
            Assert.Empty(await _dbContext.HoldingSnapshots.ToListAsync());
        }

        [Fact]
        public async Task SaveHoldingsAsync_InvalidatesSnapshotsCache()
        {
            _cache.Set("portfolio_snapshots_" + TestUserId, new List<HoldingSnapshot>(), TimeSpan.FromSeconds(30));

            await _service.SaveHoldingsAsync(TestUserId, new SaveHoldingsRequest
            {
                EffectiveDate = DateOnly.FromDateTime(DateTime.UtcNow),
                Positions = [],
            });

            Assert.False(_cache.TryGetValue("portfolio_snapshots_" + TestUserId, out _));
        }

        [Fact]
        public async Task DeleteSnapshotAsync_InvalidatesSnapshotsCache()
        {
            _dbContext.HoldingSnapshots.Add(new HoldingSnapshot
            {
                UserId = TestUserId,
                EffectiveDate = DateOnly.FromDateTime(DateTime.UtcNow),
                UsdTwdRate = 32m,
                Positions = [],
            });
            await _dbContext.SaveChangesAsync();
            var snapshotId = (await _dbContext.HoldingSnapshots.SingleAsync()).Id;
            _cache.Set("portfolio_snapshots_" + TestUserId, new List<HoldingSnapshot>(), TimeSpan.FromSeconds(30));

            var deleted = await _service.DeleteSnapshotAsync(TestUserId, snapshotId);

            Assert.True(deleted);
            Assert.False(_cache.TryGetValue("portfolio_snapshots_" + TestUserId, out _));
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private async Task SeedSnapshotAsync(params HoldingPosition[] positions)
        {
            _dbContext.HoldingSnapshots.Add(new HoldingSnapshot
            {
                UserId = TestUserId,
                EffectiveDate = DateOnly.FromDateTime(DateTime.UtcNow),
                UsdTwdRate = 32m,
                Positions = positions.ToList(),
            });
            await _dbContext.SaveChangesAsync();
        }
    }
}
