using JackyAIApp.Server.Common;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.Data.Models.Portfolio;
using JackyAIApp.Server.DTO.Finance;
using JackyAIApp.Server.DTO.Portfolio;
using JackyAIApp.Server.Services;
using JackyAIApp.Server.Services.Finance.DataProviders;
using JackyAIApp.Server.Services.OpenAI;
using JackyAIApp.Server.Services.Portfolio;
using JackyAIApp.Server.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace JackyAIApp.Server.Tests.Services.Portfolio
{
    /// <summary>
    /// GetNetWorthHistoryAsync used to re-filter + re-sort each symbol's whole price history on
    /// every single day of the requested range, and re-scan the whole snapshot list to find the
    /// "active" one. It was rewritten to walk forward-only cursors instead (both `date` and the
    /// price/snapshot lists only ever move forward), since prices are asc-sorted and requested
    /// ranges can span up to 180 days. These tests pin the exact per-day values so an off-by-one
    /// in that cursor walk would fail loudly rather than silently returning a stale price.
    ///
    /// Dates are built from <see cref="Weekday"/> (business-day offsets from a fixed past Monday)
    /// rather than raw calendar-day offsets from "today", since the service skips Sat/Sun and the
    /// test must produce the same weekday regardless of which day it happens to run on.
    /// </summary>
    [Collection(SingleFlightCollection.Name)]
    public class PortfolioNetWorthHistoryTests : IDisposable
    {
        private const string TestUserId = "networth-test-user";

        // Comfortably more than 180 days queries wide back? No — just far enough in the past
        // that `days: 90` below always clamps startDate to the snapshot's own EffectiveDate
        // regardless of which real day the suite happens to run on.
        private static readonly DateOnly AnchorMonday = MostRecentMonday(DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-28));

        private readonly AzureSQLDBContext _dbContext;
        private readonly Mock<IMarketDataProviderFactory> _providerFactory;
        private readonly Mock<IFundDataService> _fundData;
        private readonly Mock<IStockCategoryService> _categoryService;
        private readonly NetWorthHistoryService _netWorth;

        public PortfolioNetWorthHistoryTests()
        {
            var options = new DbContextOptionsBuilder<AzureSQLDBContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;
            _dbContext = new AzureSQLDBContext(options);

            _providerFactory = new Mock<IMarketDataProviderFactory>();
            _fundData = new Mock<IFundDataService>();
            var dividendData = new Mock<IDividendService>();
            dividendData.Setup(x => x.GetStockDividendsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<DividendRecord>());
            dividendData.Setup(x => x.GetFundDividendsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<DividendRecord>());

            var httpClientFactory = new Mock<IHttpClientFactory>();
            var cache = new MemoryCache(new MemoryCacheOptions());
            var openAI = new Mock<IOpenAIPromptService>();
            _categoryService = new Mock<IStockCategoryService>();

            var builder = new PortfolioServiceBuilder(_dbContext)
            {
                Providers = _providerFactory.Object,
                FundData = _fundData.Object,
                Dividends = dividendData.Object,
                Categories = _categoryService.Object,
                HttpClientFactory = httpClientFactory.Object,
                Cache = cache,
            };
            _netWorth = builder.BuildNetWorthHistory();
        }

        public void Dispose() => _dbContext.Dispose();

        [Fact]
        public async Task GetNetWorthHistory_UnsortedStockPrices_UsesLatestCloseAtOrBeforeEachDay()
        {
            var snapshotDate = Weekday(0);
            await SeedSnapshotAsync(snapshotDate,
                new HoldingPosition
                {
                    AssetType = AssetType.STOCK, Symbol = "2330", Name = "台積電",
                    Market = "TW", Currency = "TWD", Quantity = 100,
                });

            // Deliberately out of date order — the cursor walk depends on the service re-sorting
            // this before scanning it, not on the upstream provider's ordering.
            SetupStockProvider("2330", MarketRegion.TW,
                (Weekday(6), 700m),
                (Weekday(2), 500m),
                (Weekday(4), 600m));

            var history = await _netWorth.GetNetWorthHistoryAsync(TestUserId, days: 90);
            var byDate = history.ToDictionary(p => p.Date);

            // Before the first price point exists at all: no data for that day (matches the old
            // "close <= 0 => skip" behavior when nothing qualifies as "at or before" yet).
            Assert.False(byDate.ContainsKey(snapshotDate.ToString("yyyy-MM-dd")));

            // Weekday(2) = first price point; Weekday(3) holds (no newer point yet); Weekday(4)
            // advances to the next point; Weekday(5) holds; Weekday(6) advances to the last point.
            Assert.Equal(50_000m, GroupValue(byDate[Weekday(2).ToString("yyyy-MM-dd")], "TwStock"));
            Assert.Equal(50_000m, GroupValue(byDate[Weekday(3).ToString("yyyy-MM-dd")], "TwStock"));
            Assert.Equal(60_000m, GroupValue(byDate[Weekday(4).ToString("yyyy-MM-dd")], "TwStock"));
            Assert.Equal(60_000m, GroupValue(byDate[Weekday(5).ToString("yyyy-MM-dd")], "TwStock"));
            Assert.Equal(70_000m, GroupValue(byDate[Weekday(6).ToString("yyyy-MM-dd")], "TwStock"));
        }

        [Fact]
        public async Task GetNetWorthHistory_TwoSnapshots_UsesActiveSnapshotAtEachDate()
        {
            var firstDate = Weekday(0);
            var secondDate = Weekday(6);

            // Cash-only positions so the snapshot-selection cursor is isolated from price cursors.
            await SeedSnapshotAsync(firstDate,
                new HoldingPosition { AssetType = AssetType.CASH, Name = "現金", Currency = "TWD", Quantity = 100_000 });
            await SeedSnapshotAsync(secondDate,
                new HoldingPosition { AssetType = AssetType.CASH, Name = "現金", Currency = "TWD", Quantity = 250_000 });

            var history = await _netWorth.GetNetWorthHistoryAsync(TestUserId, days: 90);
            var byDate = history.ToDictionary(p => p.Date);

            Assert.Equal(100_000m, byDate[firstDate.ToString("yyyy-MM-dd")].TotalTWD);
            Assert.Equal(100_000m, byDate[Weekday(5).ToString("yyyy-MM-dd")].TotalTWD);
            Assert.Equal(250_000m, byDate[secondDate.ToString("yyyy-MM-dd")].TotalTWD);
            Assert.Equal(250_000m, byDate[Weekday(7).ToString("yyyy-MM-dd")].TotalTWD);
        }

        [Fact]
        public async Task GetNetWorthHistory_UnsortedFundNav_UsesLatestNavAtOrBeforeEachDay()
        {
            var snapshotDate = Weekday(0);
            await SeedSnapshotAsync(snapshotDate,
                new HoldingPosition
                {
                    AssetType = AssetType.FUND, Symbol = "FUND1", Name = "Test Fund",
                    Currency = "TWD", Quantity = 1000, UnitPrice = 10m,
                });

            // Out of order, mirroring the stock test — the fund cursor must also re-sort first.
            _fundData.Setup(x => x.GetFundNavHistoryAsync("FUND1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new FundNavHistory("TWD", new List<PricePoint>
                {
                    new() { Date = Weekday(4).ToString("yyyy-MM-dd"), Close = 15m },
                    new() { Date = Weekday(1).ToString("yyyy-MM-dd"), Close = 12m },
                }));

            var history = await _netWorth.GetNetWorthHistoryAsync(TestUserId, days: 90);
            var byDate = history.ToDictionary(p => p.Date);

            // Before any NAV point: falls back to the stored UnitPrice (10), matching the live
            // valuation loop's fallback semantics.
            Assert.Equal(10_000m, GroupValue(byDate[snapshotDate.ToString("yyyy-MM-dd")], "Fund"));
            Assert.Equal(12_000m, GroupValue(byDate[Weekday(1).ToString("yyyy-MM-dd")], "Fund"));
            Assert.Equal(12_000m, GroupValue(byDate[Weekday(2).ToString("yyyy-MM-dd")], "Fund"));
            Assert.Equal(15_000m, GroupValue(byDate[Weekday(4).ToString("yyyy-MM-dd")], "Fund"));
        }

        [Fact]
        public async Task GetNetWorthHistory_GroupByCategory_BucketsPositionsByAiCategory()
        {
            var snapshotDate = Weekday(0);
            await SeedSnapshotAsync(snapshotDate,
                new HoldingPosition
                {
                    AssetType = AssetType.STOCK, Symbol = "2330", Name = "台積電",
                    Market = "TW", Currency = "TWD", Quantity = 100,
                },
                new HoldingPosition { AssetType = AssetType.CASH, Name = "現金", Currency = "TWD", Quantity = 50_000 });

            SetupStockProvider("2330", MarketRegion.TW, (snapshotDate, 500m));
            _categoryService.Setup(x => x.GetCategoryAsync(
                    AssetType.STOCK, "2330", "台積電", "TW", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new StockCategoryInfo(PortfolioCategory.Semiconductor, "晶圓代工", ["晶圓代工", "AI"]));

            var history = await _netWorth.GetNetWorthHistoryAsync(TestUserId, days: 90, NetWorthGroupBy.Category);
            var point = history.Single(p => p.Date == snapshotDate.ToString("yyyy-MM-dd"));

            // AI-classified stock lands in its own category; CASH is mapped directly (no AI call).
            Assert.Equal(50_000m, GroupValue(point, "Semiconductor"));
            Assert.Equal(50_000m, GroupValue(point, "Cash"));
            Assert.Equal(100_000m, point.TotalTWD);
            _categoryService.Verify(x => x.GetCategoryAsync(
                AssetType.CASH, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task GetNetWorthHistory_GroupBySubCategory_BucketsPositionsByAiSubCategory()
        {
            var snapshotDate = Weekday(0);
            await SeedSnapshotAsync(snapshotDate,
                new HoldingPosition
                {
                    AssetType = AssetType.STOCK, Symbol = "2330", Name = "台積電",
                    Market = "TW", Currency = "TWD", Quantity = 100,
                },
                new HoldingPosition { AssetType = AssetType.CASH, Name = "現金", Currency = "TWD", Quantity = 50_000 });

            SetupStockProvider("2330", MarketRegion.TW, (snapshotDate, 500m));
            _categoryService.Setup(x => x.GetCategoryAsync(
                    AssetType.STOCK, "2330", "台積電", "TW", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new StockCategoryInfo(PortfolioCategory.Semiconductor, "晶圓代工", ["晶圓代工", "AI"]));

            var history = await _netWorth.GetNetWorthHistoryAsync(TestUserId, days: 90, NetWorthGroupBy.SubCategory);
            var point = history.Single(p => p.Date == snapshotDate.ToString("yyyy-MM-dd"));

            // AI-classified stock lands under its free-form subCategory label; CASH gets a plain
            // Chinese key directly (no AI call, no PortfolioCategory enum name involved at all).
            Assert.Equal(50_000m, GroupValue(point, "晶圓代工"));
            Assert.Equal(50_000m, GroupValue(point, "現金"));
            Assert.Equal(100_000m, point.TotalTWD);
        }

        [Fact]
        public async Task GetNetWorthHistory_GroupBySubCategory_NullSubCategoryFallsBackToUnclassified()
        {
            var snapshotDate = Weekday(0);
            await SeedSnapshotAsync(snapshotDate,
                new HoldingPosition
                {
                    AssetType = AssetType.STOCK, Symbol = "9999", Name = "Mystery Corp",
                    Market = "US", Currency = "USD", Quantity = 10,
                });

            SetupStockProvider("9999", MarketRegion.US, (snapshotDate, 100m));
            _categoryService.Setup(x => x.GetCategoryAsync(
                    AssetType.STOCK, "9999", "Mystery Corp", "US", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new StockCategoryInfo(PortfolioCategory.Other, null, []));

            var history = await _netWorth.GetNetWorthHistoryAsync(TestUserId, days: 90, NetWorthGroupBy.SubCategory);
            var point = history.Single(p => p.Date == snapshotDate.ToString("yyyy-MM-dd"));

            Assert.Equal(32_000m, GroupValue(point, "未分類")); // 10 * 100 * FX 32
        }

        [Fact]
        public async Task GetNetWorthHistory_GroupByCurrency_BucketsPositionsByDeclaredCurrency()
        {
            var snapshotDate = Weekday(0);
            await SeedSnapshotAsync(snapshotDate,
                new HoldingPosition { AssetType = AssetType.CASH, Name = "台幣現金", Currency = "TWD", Quantity = 100_000 },
                new HoldingPosition { AssetType = AssetType.CASH, Name = "美金現金", Currency = "USD", Quantity = 1_000 });

            var history = await _netWorth.GetNetWorthHistoryAsync(TestUserId, days: 90, NetWorthGroupBy.Currency);
            var point = history.Single(p => p.Date == snapshotDate.ToString("yyyy-MM-dd"));

            Assert.Equal(100_000m, GroupValue(point, "TWD"));
            // USD cash converted at the snapshot's stored FX rate (32) — see SeedSnapshotAsync.
            Assert.Equal(32_000m, GroupValue(point, "USD"));
        }

        [Fact]
        public async Task GetNetWorthHistory_GroupByHolding_BucketsByEachPositionsOwnName()
        {
            var snapshotDate = Weekday(0);
            await SeedSnapshotAsync(snapshotDate,
                new HoldingPosition
                {
                    AssetType = AssetType.STOCK, Symbol = "00631L", Name = "元大台灣50正2",
                    Market = "TW", Currency = "TWD", Quantity = 100,
                },
                new HoldingPosition { AssetType = AssetType.CASH, Name = "台幣活存", Currency = "TWD", Quantity = 50_000 },
                new HoldingPosition
                {
                    AssetType = AssetType.REAL_ESTATE, Name = "自住房", Currency = "TWD", Quantity = 16_000_000,
                });

            SetupStockProvider("00631L", MarketRegion.TW, (snapshotDate, 200m));

            var history = await _netWorth.GetNetWorthHistoryAsync(TestUserId, days: 90, NetWorthGroupBy.Holding);
            var point = history.Single(p => p.Date == snapshotDate.ToString("yyyy-MM-dd"));

            // STOCK/FUND use the position's own Name verbatim; CASH/LIABILITY/REAL_ESTATE collapse
            // into one fixed bucket each regardless of Name — same labels as SubCategory mode —
            // so multiple cash/liability entries don't clutter the per-holding legend.
            Assert.Equal(20_000m, GroupValue(point, "元大台灣50正2"));
            Assert.Equal(50_000m, GroupValue(point, "現金"));
            Assert.Equal(16_000_000m, GroupValue(point, "不動產"));
            _categoryService.Verify(x => x.GetCategoryAsync(
                It.IsAny<AssetType>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>Missing group key = that group contributed nothing that day (Add() only ever
        /// creates an entry when a position actually contributes), so 0 is the correct default —
        /// mirrors the old fixed-field DTO's implicit 0 for an untouched asset-type field.</summary>
        private static decimal GroupValue(NetWorthDataPoint point, string key) =>
            point.Groups.FirstOrDefault(g => g.Key == key)?.ValueTWD ?? 0m;

        private static DateOnly MostRecentMonday(DateOnly from)
        {
            var d = from;
            while (d.DayOfWeek != DayOfWeek.Monday) d = d.AddDays(-1);
            return d;
        }

        /// <summary>The <paramref name="businessDaysOffset"/>-th weekday on/after <see cref="AnchorMonday"/>
        /// (0 = the Monday itself). Weekends are never counted or returned.</summary>
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
