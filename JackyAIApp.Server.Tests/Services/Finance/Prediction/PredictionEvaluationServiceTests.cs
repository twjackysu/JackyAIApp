using JackyAIApp.Server.Data;
using JackyAIApp.Server.Data.Models.SQL;
using JackyAIApp.Server.DTO.Finance;
using JackyAIApp.Server.Services.Market;
using JackyAIApp.Server.Services.Market.US;
using JackyAIApp.Server.Services.Finance.Prediction;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TWStockLib.Models;

namespace JackyAIApp.Server.Tests.Services.Finance.Prediction
{
    /// <summary>
    /// Evaluation-path tests for TASK-013. Covers: due-vs-not-due filtering, correct
    /// bullish/bearish/neutral classification against the ±3% band for both a TW and a US symbol
    /// — including a long-horizon (12mo) prediction, whose target date sits well outside
    /// <c>IMarketDataProvider.FetchAsync</c>'s fixed ~6-month trailing window, which is exactly why
    /// evaluation fetches a small range centered on the target date instead (see
    /// <c>PredictionEvaluationService.RangeWindowDays</c>) — and that one row's lookup failure, or a
    /// fetched range with nothing reasonably close to the target date, doesn't abandon the batch or
    /// get silently graded against a distant price.
    /// </summary>
    public class PredictionEvaluationServiceTests : IDisposable
    {
        private readonly DbContextOptions<AzureSQLDBContext> _options;
        private readonly AzureSQLDBContext _dbContext;

        public PredictionEvaluationServiceTests()
        {
            _options = new DbContextOptionsBuilder<AzureSQLDBContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;
            _dbContext = new AzureSQLDBContext(_options);
        }

        public void Dispose() => _dbContext.Dispose();

        private class TestDbContextFactory(DbContextOptions<AzureSQLDBContext> options)
            : IDbContextFactory<AzureSQLDBContext>
        {
            public AzureSQLDBContext CreateDbContext() => new(options);

            public Task<AzureSQLDBContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
                Task.FromResult(new AzureSQLDBContext(options));
        }

        private static Mock<YahooFinanceMarketDataProvider> NewYahooMock() =>
            new(Mock.Of<IHttpClientFactory>(), Mock.Of<Microsoft.Extensions.Caching.Memory.IMemoryCache>(),
                NullLogger<YahooFinanceMarketDataProvider>.Instance);

        private PredictionEvaluationService BuildService(
            Mock<ITWStockLibHistoricalProvider>? twProvider = null,
            Mock<YahooFinanceMarketDataProvider>? yahooProvider = null,
            Mock<IMarketDataProviderFactory>? providerFactory = null) =>
            new(new TestDbContextFactory(_options),
                (providerFactory ?? new Mock<IMarketDataProviderFactory>()).Object,
                (twProvider ?? new Mock<ITWStockLibHistoricalProvider>()).Object,
                (yahooProvider ?? NewYahooMock()).Object,
                NullLogger<PredictionEvaluationService>.Instance);

        private static StockPrediction BuildRow(
            string stockCode, string market, DateTime predictedAtUtc, int horizonMin, int horizonMax,
            decimal? priceAtPrediction, string predictedTrend = "bullish") => new()
        {
            Id = Guid.NewGuid().ToString(),
            StockCode = stockCode,
            Market = market,
            UserId = "user-1",
            PredictedAtUtc = predictedAtUtc,
            PriceAtPrediction = priceAtPrediction,
            HorizonMonthsMin = horizonMin,
            HorizonMonthsMax = horizonMax,
            PredictedTrend = predictedTrend,
        };

        private async Task<StockPrediction> ReloadAsync(string id)
        {
            await using var db = new AzureSQLDBContext(_options);
            return await db.StockPredictions.SingleAsync(p => p.Id == id);
        }

        private static MarketData SinglePrice(decimal close, DateTime date) => new()
        {
            HistoricalPrices = [new DailyPrice { Date = date, Close = close }],
        };

        [Fact]
        public async Task EvaluateDuePredictionsAsync_HorizonNotYetElapsed_IsNeverTouched()
        {
            var row = BuildRow("2330", "TW", DateTime.UtcNow, horizonMin: 3, horizonMax: 12, priceAtPrediction: 100m);
            _dbContext.StockPredictions.Add(row);
            await _dbContext.SaveChangesAsync();

            var twProvider = new Mock<ITWStockLibHistoricalProvider>();
            var service = BuildService(twProvider: twProvider);

            await service.EvaluateDuePredictionsAsync();

            twProvider.Verify(p => p.FetchRangeAsync(
                It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<MarketType?>(), It.IsAny<CancellationToken>()),
                Times.Never);
            var saved = await ReloadAsync(row.Id);
            Assert.Null(saved.EvaluatedAtUtc);
        }

        [Fact]
        public async Task EvaluateDuePredictionsAsync_NullPriceAtPrediction_IsNeverTouchedEvenIfDue()
        {
            var row = BuildRow(
                "2330", "TW", DateTime.UtcNow.AddMonths(-2), horizonMin: 1, horizonMax: 3, priceAtPrediction: null);
            _dbContext.StockPredictions.Add(row);
            await _dbContext.SaveChangesAsync();

            var twProvider = new Mock<ITWStockLibHistoricalProvider>();
            var service = BuildService(twProvider: twProvider);

            await service.EvaluateDuePredictionsAsync();

            twProvider.Verify(p => p.FetchRangeAsync(
                It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<MarketType?>(), It.IsAny<CancellationToken>()),
                Times.Never);
            var saved = await ReloadAsync(row.Id);
            Assert.Null(saved.EvaluatedAtUtc);
        }

        [Fact]
        public async Task EvaluateDuePredictionsAsync_DueTwRow_EvaluatesBullishMoveCorrectly()
        {
            var predictedAt = DateTime.UtcNow.AddMonths(-2);
            var targetDate = predictedAt.AddMonths(1);
            var row = BuildRow("2330", "TW", predictedAt, horizonMin: 1, horizonMax: 3, priceAtPrediction: 100m, predictedTrend: "bullish");
            _dbContext.StockPredictions.Add(row);
            await _dbContext.SaveChangesAsync();

            // +5% move — clears the ±3% neutral band, so this should classify as bullish and match
            // the row's own "bullish" prediction.
            var twProvider = new Mock<ITWStockLibHistoricalProvider>();
            twProvider
                .Setup(p => p.FetchRangeAsync("2330", It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<MarketType?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(SinglePrice(105m, targetDate));

            var service = BuildService(twProvider: twProvider);
            await service.EvaluateDuePredictionsAsync();

            var saved = await ReloadAsync(row.Id);
            Assert.NotNull(saved.EvaluatedAtUtc);
            Assert.Equal("bullish", saved.ActualTrend);
            Assert.Equal(105m, saved.ActualPriceAtEvaluation);
            Assert.True(saved.WasCorrect);
        }

        [Fact]
        public async Task EvaluateDuePredictionsAsync_DueLongHorizonTwRow_FetchesAroundTargetDateNotJustRecentHistory()
        {
            // Regression: evaluation used to call IMarketDataProvider.FetchAsync, whose ~6-month
            // trailing-from-now window can't possibly contain a 12mo-horizon prediction's target
            // date (itself ~1yr in the past once due). This pins that evaluation now fetches a
            // range actually covering the target date instead.
            var predictedAt = DateTime.UtcNow.AddMonths(-13);
            var targetDate = predictedAt.AddMonths(12);
            var row = BuildRow("2330", "TW", predictedAt, horizonMin: 12, horizonMax: 36, priceAtPrediction: 100m, predictedTrend: "bearish");
            _dbContext.StockPredictions.Add(row);
            await _dbContext.SaveChangesAsync();

            var twProvider = new Mock<ITWStockLibHistoricalProvider>();
            twProvider
                .Setup(p => p.FetchRangeAsync("2330", It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<MarketType?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(SinglePrice(88m, targetDate));

            var service = BuildService(twProvider: twProvider);
            await service.EvaluateDuePredictionsAsync();

            var saved = await ReloadAsync(row.Id);
            Assert.NotNull(saved.EvaluatedAtUtc);
            Assert.Equal("bearish", saved.ActualTrend);
            Assert.True(saved.WasCorrect);

            // The whole point of this test: the fetched range must actually straddle the target
            // date (~1yr in the past once a 12mo-horizon prediction is due), not just cover recent
            // history the way IMarketDataProvider.FetchAsync's fixed ~6-month window would.
            twProvider.Verify(p => p.FetchRangeAsync(
                "2330",
                It.Is<DateTime>(d => d <= targetDate && (targetDate - d).TotalDays <= 15),
                It.Is<DateTime>(d => d >= targetDate.AddDays(-1) && (d - targetDate).TotalDays <= 15),
                It.IsAny<MarketType?>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task EvaluateDuePredictionsAsync_NoPriceReasonablyCloseToTargetDate_LeftUnevaluated()
        {
            // Fetch succeeded but every returned price is far from the target date (e.g. a range
            // fetch that came back sparse/wrong) — must not be silently graded against it.
            var predictedAt = DateTime.UtcNow.AddMonths(-2);
            var targetDate = predictedAt.AddMonths(1);
            var row = BuildRow("2330", "TW", predictedAt, horizonMin: 1, horizonMax: 3, priceAtPrediction: 100m);
            _dbContext.StockPredictions.Add(row);
            await _dbContext.SaveChangesAsync();

            var twProvider = new Mock<ITWStockLibHistoricalProvider>();
            twProvider
                .Setup(p => p.FetchRangeAsync("2330", It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<MarketType?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(SinglePrice(105m, targetDate.AddDays(60)));

            var service = BuildService(twProvider: twProvider);
            await service.EvaluateDuePredictionsAsync();

            var saved = await ReloadAsync(row.Id);
            Assert.Null(saved.EvaluatedAtUtc);
            Assert.Null(saved.ActualTrend);
        }

        [Fact]
        public async Task EvaluateDuePredictionsAsync_DueUsRow_EvaluatesBearishMoveCorrectly()
        {
            var predictedAt = DateTime.UtcNow.AddMonths(-4);
            var targetDate = predictedAt.AddMonths(3);
            var row = BuildRow("AAPL", "US", predictedAt, horizonMin: 3, horizonMax: 12, priceAtPrediction: 200m, predictedTrend: "bearish");
            _dbContext.StockPredictions.Add(row);
            await _dbContext.SaveChangesAsync();

            // -10% move — should classify as bearish, matching the row's own "bearish" prediction.
            var yahooProvider = NewYahooMock();
            yahooProvider
                .Setup(p => p.FetchRangeAsync("AAPL", It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(SinglePrice(180m, targetDate));

            var service = BuildService(yahooProvider: yahooProvider);
            await service.EvaluateDuePredictionsAsync();

            var saved = await ReloadAsync(row.Id);
            Assert.NotNull(saved.EvaluatedAtUtc);
            Assert.Equal("bearish", saved.ActualTrend);
            Assert.True(saved.WasCorrect);
        }

        [Fact]
        public async Task EvaluateDuePredictionsAsync_SmallMove_ClassifiesAsNeutral()
        {
            var predictedAt = DateTime.UtcNow.AddMonths(-2);
            var targetDate = predictedAt.AddMonths(1);
            var row = BuildRow("2330", "TW", predictedAt, horizonMin: 1, horizonMax: 3, priceAtPrediction: 100m, predictedTrend: "neutral");
            _dbContext.StockPredictions.Add(row);
            await _dbContext.SaveChangesAsync();

            // +1% — inside the ±3% neutral band.
            var twProvider = new Mock<ITWStockLibHistoricalProvider>();
            twProvider
                .Setup(p => p.FetchRangeAsync("2330", It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<MarketType?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(SinglePrice(101m, targetDate));

            var service = BuildService(twProvider: twProvider);
            await service.EvaluateDuePredictionsAsync();

            var saved = await ReloadAsync(row.Id);
            Assert.Equal("neutral", saved.ActualTrend);
            Assert.True(saved.WasCorrect);
        }

        [Fact]
        public async Task EvaluateDuePredictionsAsync_OneRowLookupFails_OtherRowsInBatchStillEvaluated()
        {
            var predictedAt = DateTime.UtcNow.AddMonths(-2);
            var targetDate = predictedAt.AddMonths(1);

            var failingRow = BuildRow("BADSYM", "TW", predictedAt, horizonMin: 1, horizonMax: 3, priceAtPrediction: 100m);
            var okRow = BuildRow("2330", "TW", predictedAt.AddMinutes(1), horizonMin: 1, horizonMax: 3, priceAtPrediction: 50m, predictedTrend: "bullish");
            _dbContext.StockPredictions.AddRange(failingRow, okRow);
            await _dbContext.SaveChangesAsync();

            var twProvider = new Mock<ITWStockLibHistoricalProvider>();
            twProvider
                .Setup(p => p.FetchRangeAsync("BADSYM", It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<MarketType?>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("upstream error"));
            twProvider
                .Setup(p => p.FetchRangeAsync("2330", It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<MarketType?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(SinglePrice(55m, targetDate));

            var service = BuildService(twProvider: twProvider);
            await service.EvaluateDuePredictionsAsync(batchSize: 5);

            var savedFailing = await ReloadAsync(failingRow.Id);
            Assert.Null(savedFailing.EvaluatedAtUtc);

            var savedOk = await ReloadAsync(okRow.Id);
            Assert.NotNull(savedOk.EvaluatedAtUtc);
            Assert.Equal("bullish", savedOk.ActualTrend);
        }

        [Fact]
        public async Task EvaluateDuePredictionsAsync_RespectsBatchSize_OldestDueFirst()
        {
            var older = BuildRow("2330", "TW", DateTime.UtcNow.AddMonths(-5), horizonMin: 1, horizonMax: 3, priceAtPrediction: 100m);
            var newer = BuildRow("2317", "TW", DateTime.UtcNow.AddMonths(-2), horizonMin: 1, horizonMax: 3, priceAtPrediction: 100m);
            _dbContext.StockPredictions.AddRange(older, newer);
            await _dbContext.SaveChangesAsync();

            var twProvider = new Mock<ITWStockLibHistoricalProvider>();
            twProvider
                .Setup(p => p.FetchRangeAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<MarketType?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string code, DateTime start, DateTime end, MarketType? mt, CancellationToken ct) =>
                    SinglePrice(100m, start.AddDays(1)));

            var service = BuildService(twProvider: twProvider);
            await service.EvaluateDuePredictionsAsync(batchSize: 1);

            var savedOlder = await ReloadAsync(older.Id);
            var savedNewer = await ReloadAsync(newer.Id);
            Assert.NotNull(savedOlder.EvaluatedAtUtc);
            Assert.Null(savedNewer.EvaluatedAtUtc);
        }
    }
}
