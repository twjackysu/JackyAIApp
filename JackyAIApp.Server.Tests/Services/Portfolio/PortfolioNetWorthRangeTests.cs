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
    /// The 淨值走勢 chart now takes a start and end date instead of a preset 1M/3M/6M span. These
    /// tests pin how a requested window resolves against "today" and the user's first snapshot —
    /// in particular the default the chart opens with: one month back, or from the first record
    /// when there is less than a month of history.
    ///
    /// "Today" is fixed through TimeProvider (Wednesday 2026-10-07), unlike the older history
    /// tests that derive dates from the real clock, so every expected date here is literal.
    /// </summary>
    public class PortfolioNetWorthRangeTests : IDisposable
    {
        private const string UserId = "networth-range-user";
        private static readonly DateOnly Today = new(2026, 10, 7); // Wednesday

        private readonly AzureSQLDBContext _db;
        private readonly Mock<IMarketDataProviderFactory> _providers = new();
        private readonly PortfolioService _service;

        public PortfolioNetWorthRangeTests()
        {
            _db = new AzureSQLDBContext(new DbContextOptionsBuilder<AzureSQLDBContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);

            var dividends = new Mock<IDividendService>();
            dividends.Setup(x => x.GetStockDividendsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<DividendRecord>());
            dividends.Setup(x => x.GetFundDividendsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<DividendRecord>());

            _service = new PortfolioService(
                _db,
                _providers.Object,
                new Mock<IFundDataService>().Object,
                dividends.Object,
                new Mock<IStockCategoryService>().Object,
                stockService: null!,
                new Mock<IHttpClientFactory>().Object,
                new MemoryCache(new MemoryCacheOptions()),
                new Mock<IOpenAIPromptService>().Object,
                new Mock<IUsageLimitService>().Object,
                NullLogger<PortfolioService>.Instance,
                timeProvider: new FixedTime(Today));

            // A flat price on every day from well before any snapshot to past today, so every
            // weekday inside a window produces a point and the window edges are what's measured.
            SetupFlatStockPrice("2330", from: new DateOnly(2019, 12, 1), to: Today.AddDays(30));
        }

        public void Dispose() => _db.Dispose();

        // ── The default the chart opens with ──────────────────────────────────

        [Fact]
        public async Task NoDates_CoversOneCalendarMonthEndingToday()
        {
            await SeedSnapshotAsync(new DateOnly(2026, 6, 1));

            var history = await _service.GetNetWorthHistoryAsync(UserId, from: null, to: null);

            Assert.Equal("2026-09-07", history[0].Date); // 10-07 minus one calendar month (a Monday)
            Assert.Equal("2026-10-07", history[^1].Date);
        }

        [Fact]
        public async Task LessThanAMonthOfHistory_StartsAtTheFirstRecord()
        {
            // The requested behaviour for a newer user: not an empty month, but everything they have.
            await SeedSnapshotAsync(new DateOnly(2026, 9, 28));

            var history = await _service.GetNetWorthHistoryAsync(UserId, from: new DateOnly(2026, 9, 7), to: Today);

            Assert.Equal("2026-09-28", history[0].Date);
            Assert.Equal("2026-10-07", history[^1].Date);
        }

        // ── An explicit window ────────────────────────────────────────────────

        [Fact]
        public async Task ExplicitWindow_ReturnsOnlyDaysInsideIt()
        {
            await SeedSnapshotAsync(new DateOnly(2026, 6, 1));

            var history = await _service.GetNetWorthHistoryAsync(
                UserId, from: new DateOnly(2026, 9, 1), to: new DateOnly(2026, 9, 15));

            Assert.Equal("2026-09-01", history[0].Date);
            Assert.Equal("2026-09-15", history[^1].Date);
            Assert.All(history, p => Assert.InRange(p.Date, "2026-09-01", "2026-09-15"));
        }

        [Fact]
        public async Task EndInTheFuture_StopsAtToday()
        {
            await SeedSnapshotAsync(new DateOnly(2026, 6, 1));

            var history = await _service.GetNetWorthHistoryAsync(
                UserId, from: new DateOnly(2026, 9, 1), to: new DateOnly(2026, 12, 31));

            Assert.Equal("2026-10-07", history[^1].Date);
        }

        [Fact]
        public async Task StartAfterEnd_IsSwappedRatherThanEmpty()
        {
            // Date inputs are edited one at a time, so a momentarily inverted pair is normal input.
            await SeedSnapshotAsync(new DateOnly(2026, 6, 1));

            var history = await _service.GetNetWorthHistoryAsync(
                UserId, from: new DateOnly(2026, 9, 15), to: new DateOnly(2026, 9, 1));

            Assert.Equal("2026-09-01", history[0].Date);
            Assert.Equal("2026-09-15", history[^1].Date);
        }

        [Fact]
        public async Task OnlyAnEnd_GoesBackOneMonthFromThatEnd()
        {
            await SeedSnapshotAsync(new DateOnly(2026, 6, 1));

            var history = await _service.GetNetWorthHistoryAsync(UserId, from: null, to: new DateOnly(2026, 9, 15));

            Assert.Equal("2026-08-17", history[0].Date); // 08-15 is a Saturday; first weekday after it
            Assert.Equal("2026-09-15", history[^1].Date);
        }

        [Fact]
        public async Task WindowEndingBeforeTheFirstRecord_IsEmpty()
        {
            await SeedSnapshotAsync(new DateOnly(2026, 9, 28));

            var history = await _service.GetNetWorthHistoryAsync(
                UserId, from: new DateOnly(2026, 8, 1), to: new DateOnly(2026, 8, 31));

            Assert.Empty(history);
        }

        [Fact]
        public async Task VeryWideWindow_KeepsTheMostRecentThreeYears()
        {
            await SeedSnapshotAsync(new DateOnly(2020, 1, 1));

            var history = await _service.GetNetWorthHistoryAsync(UserId, from: new DateOnly(2020, 1, 1), to: Today);

            // Today − 1095 days is Sunday 2023-10-08, so the first weekday point is the Monday after.
            Assert.Equal("2023-10-09", history[0].Date);
            Assert.Equal("2026-10-07", history[^1].Date);
        }

        [Fact]
        public async Task NoSnapshots_IsEmpty()
        {
            Assert.Empty(await _service.GetNetWorthHistoryAsync(UserId, from: null, to: null));
        }

        // ── The preset-span overload the other charts still use ────────────────

        [Fact]
        public async Task DaysOverload_IsUnchanged()
        {
            await SeedSnapshotAsync(new DateOnly(2026, 6, 1));

            var history = await _service.GetNetWorthHistoryAsync(UserId, days: 30);

            Assert.Equal("2026-09-07", history[0].Date); // 10-07 minus 30 days is Monday 09-07
            Assert.Equal("2026-10-07", history[^1].Date);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private async Task SeedSnapshotAsync(DateOnly effectiveDate)
        {
            _db.HoldingSnapshots.Add(new HoldingSnapshot
            {
                UserId = UserId,
                EffectiveDate = effectiveDate,
                UsdTwdRate = 32m,
                Positions =
                [
                    new HoldingPosition
                    {
                        AssetType = AssetType.STOCK, Symbol = "2330", Name = "台積電",
                        Market = "TW", Currency = "TWD", Quantity = 10,
                    },
                ],
            });
            await _db.SaveChangesAsync();
        }

        private void SetupFlatStockPrice(string symbol, DateOnly from, DateOnly to)
        {
            var prices = new List<DailyPrice>();
            for (var d = from; d <= to; d = d.AddDays(1))
                prices.Add(new DailyPrice { Date = d.ToDateTime(TimeOnly.MinValue), Close = 100m });

            var provider = new Mock<IMarketDataProvider>();
            provider.Setup(x => x.FetchAsync(symbol, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new MarketData { StockCode = symbol, HistoricalPrices = prices });

            _providers.Setup(x => x.DetectRegion(symbol)).Returns(MarketRegion.TW);
            _providers.Setup(x => x.GetMarketDataProvider(MarketRegion.TW)).Returns(provider.Object);
        }

        private sealed class FixedTime(DateOnly today) : TimeProvider
        {
            public override DateTimeOffset GetUtcNow() => new(today.ToDateTime(new TimeOnly(4, 0)), TimeSpan.Zero);
        }
    }
}
