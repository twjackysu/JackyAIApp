using JackyAIApp.Server.Common;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.Data.Models.Portfolio;
using JackyAIApp.Server.DTO.Finance;
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
    /// GetDrawdownAnalysisAsync reuses ValueAsOfDate (the day-change valuator) for the per-day
    /// total instead of re-deriving asset-type valuation rules — these tests exercise the
    /// drawdown-specific math on top of that: running-peak tracking, worst-episode selection, and
    /// recovery detection. Same fixed-past-Monday date pattern as PortfolioNetWorthHistoryTests,
    /// for the same reason (service skips Sat/Sun; tests must be day-of-week stable).
    /// </summary>
    public class PortfolioDrawdownTests : IDisposable
    {
        private const string TestUserId = "drawdown-test-user";

        private static readonly DateOnly AnchorMonday = MostRecentMonday(DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-28));

        private readonly AzureSQLDBContext _dbContext;
        private readonly Mock<IMarketDataProviderFactory> _providerFactory;
        private readonly PortfolioService _service;

        public PortfolioDrawdownTests()
        {
            var options = new DbContextOptionsBuilder<AzureSQLDBContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;
            _dbContext = new AzureSQLDBContext(options);

            _providerFactory = new Mock<IMarketDataProviderFactory>();
            var fundData = new Mock<IFundDataService>();
            var dividendData = new Mock<IDividendService>();
            dividendData.Setup(x => x.GetStockDividendsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<DividendRecord>());
            dividendData.Setup(x => x.GetFundDividendsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<DividendRecord>());

            var httpClientFactory = new Mock<IHttpClientFactory>();
            var cache = new MemoryCache(new MemoryCacheOptions());
            var openAI = new Mock<IOpenAIPromptService>();
            var creditService = new Mock<ICreditService>();
            var categoryService = new Mock<IStockCategoryService>();

            _service = new PortfolioService(
                _dbContext,
                _providerFactory.Object,
                fundData.Object,
                dividendData.Object,
                categoryService.Object,
                stockService: null!,
                httpClientFactory.Object,
                cache,
                openAI.Object,
                creditService.Object,
                NullLogger<PortfolioService>.Instance);
        }

        public void Dispose() => _dbContext.Dispose();

        [Fact]
        public async Task GetDrawdown_MonotonicIncrease_NoDrawdownEpisode()
        {
            var snapshotDate = Weekday(0);
            await SeedSnapshotAsync(snapshotDate,
                new HoldingPosition
                {
                    AssetType = AssetType.STOCK, Symbol = "2330", Name = "台積電",
                    Market = "TW", Currency = "TWD", Quantity = 100,
                });
            SetupStockProvider("2330", MarketRegion.TW,
                (Weekday(0), 1000m), (Weekday(1), 1010m), (Weekday(2), 1020m));

            var result = await _service.GetDrawdownAnalysisAsync(TestUserId, days: 30);

            Assert.Equal(0m, result.MaxDrawdownPct);
            Assert.Null(result.PeakDate);
            Assert.Null(result.TroughDate);
            Assert.Null(result.RecoveryDate);
            Assert.Null(result.UnderwaterDays);
            Assert.False(result.StillUnderwater);
            Assert.Equal(0m, result.CurrentDrawdownPct);
        }

        [Fact]
        public async Task GetDrawdown_DipThenRecovery_ReportsWorstEpisodeAndRecoveryDate()
        {
            var snapshotDate = Weekday(0);
            await SeedSnapshotAsync(snapshotDate,
                new HoldingPosition
                {
                    AssetType = AssetType.STOCK, Symbol = "2330", Name = "台積電",
                    Market = "TW", Currency = "TWD", Quantity = 100,
                });
            // day0 peak (100,000) -> day2 trough (80,000, -20%) -> day4 recovers to 100,000.
            SetupStockProvider("2330", MarketRegion.TW,
                (Weekday(0), 1000m), (Weekday(1), 900m), (Weekday(2), 800m),
                (Weekday(3), 950m), (Weekday(4), 1000m));

            var result = await _service.GetDrawdownAnalysisAsync(TestUserId, days: 30);

            Assert.Equal(-20m, result.MaxDrawdownPct);
            Assert.Equal(Weekday(0).ToString("yyyy-MM-dd"), result.PeakDate);
            Assert.Equal(Weekday(2).ToString("yyyy-MM-dd"), result.TroughDate);
            Assert.Equal(Weekday(4).ToString("yyyy-MM-dd"), result.RecoveryDate);
            // Weekday(0) is a Monday, Weekday(4) the same week's Friday — 4 calendar days apart.
            Assert.Equal(4, result.UnderwaterDays);
            Assert.False(result.StillUnderwater);
            Assert.Equal(0m, result.CurrentDrawdownPct); // last point is back at the running peak
        }

        [Fact]
        public async Task GetDrawdown_OngoingDip_StillUnderwaterWithNoRecoveryDate()
        {
            var snapshotDate = Weekday(0);
            await SeedSnapshotAsync(snapshotDate,
                new HoldingPosition
                {
                    AssetType = AssetType.STOCK, Symbol = "2330", Name = "台積電",
                    Market = "TW", Currency = "TWD", Quantity = 100,
                });
            // Only two prices given — ValueAsOfDate forward-fills the last known close (700) for
            // every business day after Weekday(1) up through "today", so the window's last point
            // (and hence UnderwaterDays' end) is today's last business day, not Weekday(1). Compute
            // that dynamically rather than hardcoding a day count that would drift daily.
            SetupStockProvider("2330", MarketRegion.TW, (Weekday(0), 1000m), (Weekday(1), 700m));

            var result = await _service.GetDrawdownAnalysisAsync(TestUserId, days: 30);
            var lastBusinessDay = LastBusinessDayOnOrBefore(DateOnly.FromDateTime(DateTime.UtcNow));
            var expectedUnderwaterDays = (lastBusinessDay.ToDateTime(TimeOnly.MinValue)
                - Weekday(0).ToDateTime(TimeOnly.MinValue)).Days;

            Assert.Equal(-30m, result.MaxDrawdownPct);
            Assert.Equal(Weekday(0).ToString("yyyy-MM-dd"), result.PeakDate);
            Assert.Equal(Weekday(1).ToString("yyyy-MM-dd"), result.TroughDate);
            Assert.Null(result.RecoveryDate);
            Assert.Equal(expectedUnderwaterDays, result.UnderwaterDays);
            Assert.True(result.StillUnderwater);
            Assert.Equal(-30m, result.CurrentDrawdownPct);
        }

        [Fact]
        public async Task GetDrawdown_NoSnapshots_ReturnsEmptyAnalysis()
        {
            var result = await _service.GetDrawdownAnalysisAsync(TestUserId, days: 30);

            Assert.Empty(result.Points);
            Assert.Equal(0m, result.MaxDrawdownPct);
            Assert.Null(result.PeakDate);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static DateOnly LastBusinessDayOnOrBefore(DateOnly date)
        {
            while (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) date = date.AddDays(-1);
            return date;
        }

        private static DateOnly MostRecentMonday(DateOnly from)
        {
            var d = from;
            while (d.DayOfWeek != DayOfWeek.Monday) d = d.AddDays(-1);
            return d;
        }

        private static DateOnly Weekday(int businessDaysOffset)
        {
            var d = AnchorMonday;
            var remaining = businessDaysOffset;
            while (remaining > 0)
            {
                d = d.AddDays(1);
                if (d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)) remaining--;
            }
            return d;
        }

        private async Task SeedSnapshotAsync(DateOnly effectiveDate, params HoldingPosition[] positions)
        {
            _dbContext.HoldingSnapshots.Add(new HoldingSnapshot
            {
                UserId = TestUserId,
                EffectiveDate = effectiveDate,
                UsdTwdRate = 32m,
                Positions = positions.ToList(),
            });
            await _dbContext.SaveChangesAsync();
        }

        private void SetupStockProvider(string symbol, MarketRegion region, params (DateOnly Date, decimal Close)[] prices)
        {
            _providerFactory.Setup(x => x.DetectRegion(symbol)).Returns(region);

            var provider = new Mock<IMarketDataProvider>();
            provider.Setup(x => x.FetchAsync(symbol, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new MarketData
                {
                    StockCode = symbol,
                    HistoricalPrices = prices
                        .Select(p => new DailyPrice { Date = p.Date.ToDateTime(TimeOnly.MinValue), Close = p.Close })
                        .ToList(),
                });

            _providerFactory.Setup(x => x.GetMarketDataProvider(region)).Returns(provider.Object);
        }
    }
}
