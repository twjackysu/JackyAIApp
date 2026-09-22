using JackyAIApp.Server.Data;
using JackyAIApp.Server.Data.Models.SQL;
using JackyAIApp.Server.DTO.Finance;
using JackyAIApp.Server.Services.Finance.DataProviders;
using JackyAIApp.Server.Services.Finance.Prediction;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace JackyAIApp.Server.Tests.Services.Finance.Prediction
{
    /// <summary>
    /// Evaluation-path tests for TASK-013. Covers: due-vs-not-due filtering, correct
    /// bullish/bearish/neutral classification against the ±3% band for both a TW and a US symbol
    /// (via the existing <see cref="IMarketDataProviderFactory"/> test seam), and that one row's
    /// lookup failure doesn't abandon the rest of the batch.
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

        private PredictionEvaluationService BuildService(IMarketDataProviderFactory providerFactory) =>
            new(new TestDbContextFactory(_options), providerFactory, NullLogger<PredictionEvaluationService>.Instance);

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

        private static Mock<IMarketDataProvider> MockProviderReturning(decimal closeAtTargetDate, DateTime targetDate) =>
            MockProviderReturning(new MarketData
            {
                HistoricalPrices = [new DailyPrice { Date = targetDate, Close = closeAtTargetDate }],
            });

        private static Mock<IMarketDataProvider> MockProviderReturning(MarketData data)
        {
            var mock = new Mock<IMarketDataProvider>();
            mock.Setup(p => p.FetchAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(data);
            return mock;
        }

        [Fact]
        public async Task EvaluateDuePredictionsAsync_HorizonNotYetElapsed_IsNeverTouched()
        {
            var row = BuildRow("2330", "TW", DateTime.UtcNow, horizonMin: 3, horizonMax: 12, priceAtPrediction: 100m);
            _dbContext.StockPredictions.Add(row);
            await _dbContext.SaveChangesAsync();

            var providerFactory = new Mock<IMarketDataProviderFactory>();
            var service = BuildService(providerFactory.Object);

            await service.EvaluateDuePredictionsAsync();

            providerFactory.Verify(f => f.GetMarketDataProvider(It.IsAny<MarketRegion>()), Times.Never);
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

            var providerFactory = new Mock<IMarketDataProviderFactory>();
            var service = BuildService(providerFactory.Object);

            await service.EvaluateDuePredictionsAsync();

            providerFactory.Verify(f => f.GetMarketDataProvider(It.IsAny<MarketRegion>()), Times.Never);
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
            var providerMock = MockProviderReturning(closeAtTargetDate: 105m, targetDate);
            var providerFactory = new Mock<IMarketDataProviderFactory>();
            providerFactory.Setup(f => f.GetMarketDataProvider(MarketRegion.TW)).Returns(providerMock.Object);

            var service = BuildService(providerFactory.Object);
            await service.EvaluateDuePredictionsAsync();

            var saved = await ReloadAsync(row.Id);
            Assert.NotNull(saved.EvaluatedAtUtc);
            Assert.Equal("bullish", saved.ActualTrend);
            Assert.Equal(105m, saved.ActualPriceAtEvaluation);
            Assert.True(saved.WasCorrect);
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
            var providerMock = MockProviderReturning(closeAtTargetDate: 180m, targetDate);
            var providerFactory = new Mock<IMarketDataProviderFactory>();
            providerFactory.Setup(f => f.GetMarketDataProvider(MarketRegion.US)).Returns(providerMock.Object);

            var service = BuildService(providerFactory.Object);
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
            var providerMock = MockProviderReturning(closeAtTargetDate: 101m, targetDate);
            var providerFactory = new Mock<IMarketDataProviderFactory>();
            providerFactory.Setup(f => f.GetMarketDataProvider(MarketRegion.TW)).Returns(providerMock.Object);

            var service = BuildService(providerFactory.Object);
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

            var failingProvider = new Mock<IMarketDataProvider>();
            failingProvider
                .Setup(p => p.FetchAsync("BADSYM", It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("upstream error"));
            var okProvider = MockProviderReturning(closeAtTargetDate: 55m, targetDate);

            var providerFactory = new Mock<IMarketDataProviderFactory>();
            var callOrder = 0;
            providerFactory
                .Setup(f => f.GetMarketDataProvider(MarketRegion.TW))
                .Returns(() => callOrder++ == 0 ? failingProvider.Object : okProvider.Object);

            var service = BuildService(providerFactory.Object);
            // Oldest-due-first ordering (PredictedAtUtc ascending) means failingRow is processed
            // before okRow, so the first factory call maps to the failing provider.
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

            var providerMock = MockProviderReturning(new MarketData
            {
                HistoricalPrices = [new DailyPrice { Date = DateTime.UtcNow, Close = 100m }],
            });
            var providerFactory = new Mock<IMarketDataProviderFactory>();
            providerFactory.Setup(f => f.GetMarketDataProvider(MarketRegion.TW)).Returns(providerMock.Object);

            var service = BuildService(providerFactory.Object);
            await service.EvaluateDuePredictionsAsync(batchSize: 1);

            var savedOlder = await ReloadAsync(older.Id);
            var savedNewer = await ReloadAsync(newer.Id);
            Assert.NotNull(savedOlder.EvaluatedAtUtc);
            Assert.Null(savedNewer.EvaluatedAtUtc);
        }
    }
}
