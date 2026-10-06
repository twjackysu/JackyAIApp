using JackyAIApp.Server.Common;
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
    /// The summary's stage timings (Server-Timing header), and the dividend fetch running
    /// alongside the price fetch instead of after it.
    /// </summary>
    [Collection(SingleFlightCollection.Name)]
    public class PortfolioSummaryTimingTests : IDisposable
    {
        private const string TestUserId = "summary-timing-test-user";
        private static readonly DateOnly Today = new(2026, 10, 6);

        private readonly AzureSQLDBContext _dbContext;
        private readonly Mock<IMarketDataProviderFactory> _providerFactory = new();
        private readonly Mock<IDividendService> _dividendData = new();
        private readonly ServerTiming _timing = new();
        private readonly PortfolioService _service;

        public PortfolioSummaryTimingTests()
        {
            var options = new DbContextOptionsBuilder<AzureSQLDBContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;
            _dbContext = new AzureSQLDBContext(options);

            _dividendData.Setup(x => x.GetStockDividendsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<DividendRecord>());

            _service = new PortfolioService(
                _dbContext,
                _providerFactory.Object,
                new Mock<IFundDataService>().Object,
                _dividendData.Object,
                new Mock<IStockCategoryService>().Object,
                twListedCompanies: null!,
                new Mock<IHttpClientFactory>().Object,
                new MemoryCache(new MemoryCacheOptions()),
                new Mock<IOpenAIPromptService>().Object,
                new Mock<IUsageLimitService>().Object,
                NullLogger<PortfolioService>.Instance,
                timeProvider: new FixedTime(Today),
                timing: _timing);
        }

        public void Dispose() => _dbContext.Dispose();

        private async Task SeedHoldingAsync(string symbol, decimal quantity)
        {
            _dbContext.HoldingSnapshots.Add(new HoldingSnapshot
            {
                UserId = TestUserId,
                EffectiveDate = Today.AddDays(-10),
                UsdTwdRate = 32m,
                Positions =
                [
                    new HoldingPosition
                    {
                        AssetType = AssetType.STOCK, Symbol = symbol, Name = symbol,
                        Market = "TW", Currency = "TWD", Quantity = quantity,
                    },
                ],
            });
            await _dbContext.SaveChangesAsync();
        }

        private void SetupPrice(string symbol, Func<Task<MarketData>> fetch)
        {
            var provider = new Mock<IMarketDataProvider>();
            provider.Setup(p => p.FetchLatestAsync(symbol, It.IsAny<CancellationToken>())).Returns(fetch);
            _providerFactory.Setup(f => f.DetectRegion(symbol)).Returns(MarketRegion.TW);
            _providerFactory.Setup(f => f.GetMarketDataProvider(MarketRegion.TW)).Returns(provider.Object);
        }

        private static MarketData Price(string symbol, decimal close) => new()
        {
            StockCode = symbol,
            HistoricalPrices = [new DailyPrice { Date = Today.AddDays(-1).ToDateTime(TimeOnly.MinValue), Close = close }],
        };

        [Fact]
        public async Task GetSummary_FetchesDividendsWhilePricesAreStillLoading()
        {
            // The price fetch can only finish once the dividend fetch has started. Fetched one
            // after the other, as before, the price would wait out the timeout instead.
            var dividendsStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _dividendData.Setup(x => x.GetStockDividendsAsync("2330", "TW", It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    dividendsStarted.TrySetResult();
                    return Task.FromResult(new List<DividendRecord> { new(Today.AddDays(-5), AmountPerUnit: 5m, Currency: "TWD") });
                });
            SetupPrice("2330", async () =>
            {
                var started = await Task.WhenAny(dividendsStarted.Task, Task.Delay(TimeSpan.FromSeconds(5)));
                Assert.Same(dividendsStarted.Task, started);
                return Price("2330", 1000m);
            });
            await SeedHoldingAsync("2330", 100);

            var summary = await _service.GetPortfolioSummaryAsync(TestUserId);

            // Both halves still land where they did before.
            var position = Assert.Single(summary.Positions);
            Assert.Equal(100_000m, position.MarketValueTWD);
            Assert.Equal(500m, position.DividendsTWD);
            Assert.Equal(500m, summary.TotalDividendsTWD);
        }

        [Fact]
        public async Task GetSummary_RecordsEachStage()
        {
            SetupPrice("2330", () => Task.FromResult(Price("2330", 1000m)));
            await SeedHoldingAsync("2330", 100);

            await _service.GetPortfolioSummaryAsync(TestUserId);

            var header = _timing.ToHeaderValue();
            Assert.NotNull(header);
            var names = header!.Split(", ").Select(e => e.Split(';')[0]).ToList();
            // stocks/funds/dividends run in parallel, so their order isn't fixed.
            Assert.Equal(
                new[] { "dividends", "funds", "fx", "fxrates", "snapshots", "stocks", "tags", "total" },
                names.Order(StringComparer.Ordinal));
            Assert.Contains("snapshots;dur=", header);
            Assert.Contains("desc=\"1 snapshots / 1 positions\"", header);
            Assert.Contains("desc=\"1 symbols\"", header);
        }

        private sealed class FixedTime(DateOnly today) : TimeProvider
        {
            public override DateTimeOffset GetUtcNow() => new(today.ToDateTime(new TimeOnly(4, 0)), TimeSpan.Zero);
        }
    }

    public class ServerTimingTests
    {
        [Fact]
        public void ToHeaderValue_NothingRecorded_IsNull() =>
            Assert.Null(new ServerTiming().ToHeaderValue());

        [Fact]
        public void ToHeaderValue_FormatsEntriesInRecordedOrder()
        {
            var timing = new ServerTiming();
            timing.Add("snapshots", TimeSpan.FromMilliseconds(12.34), "3 snapshots");
            timing.Add("total", TimeSpan.FromMilliseconds(1500));

            Assert.Equal("snapshots;dur=12.3;desc=\"3 snapshots\", total;dur=1500.0", timing.ToHeaderValue());
        }

        [Fact]
        public void Add_DescriptionKeepsOnlyCharactersAHeaderCanCarry()
        {
            // Kestrel refuses a response whose header value isn't ASCII, and an unescaped quote
            // would end the desc string early.
            var timing = new ServerTiming();
            timing.Add("x", TimeSpan.Zero, "台積電 \"2330\" a\\b");

            Assert.Equal("x;dur=0.0;desc=\" 2330 ab\"", timing.ToHeaderValue());
        }

        [Fact]
        public async Task MeasureAsync_FromParallelTasks_KeepsEveryEntry()
        {
            var timing = new ServerTiming();

            await Task.WhenAll(Enumerable.Range(0, 200).Select(i =>
                timing.MeasureAsync($"s{i}", async () => { await Task.Yield(); return i; })));

            Assert.Equal(200, timing.ToHeaderValue()!.Split(", ").Length);
        }
    }
}
