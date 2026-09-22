using JackyAIApp.Server.Data;
using JackyAIApp.Server.DTO;
using JackyAIApp.Server.Services.Finance.Prediction;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace JackyAIApp.Server.Tests.Services.Finance.Prediction
{
    /// <summary>
    /// Write-path tests for TASK-013 (Finance prediction-accuracy tracking). One `analyze-stock`
    /// AI call must persist exactly 3 <c>StockPrediction</c> rows — one per horizon — mirroring
    /// the response's per-horizon trend fields and current price.
    /// </summary>
    public class StockPredictionRecorderTests : IDisposable
    {
        private readonly AzureSQLDBContext _dbContext;
        private readonly StockPredictionRecorder _recorder;

        public StockPredictionRecorderTests()
        {
            var options = new DbContextOptionsBuilder<AzureSQLDBContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;
            _dbContext = new AzureSQLDBContext(options);
            _recorder = new StockPredictionRecorder(_dbContext, NullLogger<StockPredictionRecorder>.Instance);
        }

        public void Dispose() => _dbContext.Dispose();

        private static StockTrendAnalysis BuildAnalysis(decimal? currentPrice = 100m) => new()
        {
            StockCode = "2330",
            CompanyName = "台積電",
            CurrentPrice = currentPrice,
            ShortTermTrend = "bullish",
            MediumTermTrend = "neutral",
            LongTermTrend = "bearish",
        };

        [Fact]
        public async Task RecordAsync_WritesExactlyThreeRows_OnePerHorizon()
        {
            await _recorder.RecordAsync("2330", "TW", "user-1", BuildAnalysis(), CancellationToken.None);

            var rows = _dbContext.StockPredictions.Where(p => p.StockCode == "2330").ToList();
            Assert.Equal(3, rows.Count);
        }

        [Fact]
        public async Task RecordAsync_MapsEachHorizonsTrendAndSharedFields_Correctly()
        {
            await _recorder.RecordAsync("2330", "TW", "user-1", BuildAnalysis(123.45m), CancellationToken.None);

            var rows = _dbContext.StockPredictions.Where(p => p.StockCode == "2330").ToList();

            var shortRow = Assert.Single(rows, r => r.HorizonMonthsMin == 1 && r.HorizonMonthsMax == 3);
            Assert.Equal("bullish", shortRow.PredictedTrend);

            var mediumRow = Assert.Single(rows, r => r.HorizonMonthsMin == 3 && r.HorizonMonthsMax == 12);
            Assert.Equal("neutral", mediumRow.PredictedTrend);

            var longRow = Assert.Single(rows, r => r.HorizonMonthsMin == 12 && r.HorizonMonthsMax == 36);
            Assert.Equal("bearish", longRow.PredictedTrend);

            foreach (var row in rows)
            {
                Assert.Equal("TW", row.Market);
                Assert.Equal("user-1", row.UserId);
                Assert.Equal(123.45m, row.PriceAtPrediction);
                Assert.Null(row.EvaluatedAtUtc);
                Assert.Null(row.ActualTrend);
                Assert.Null(row.WasCorrect);
            }
        }

        [Fact]
        public async Task RecordAsync_NullCurrentPrice_StillWritesRowsWithNullPriceAtPrediction()
        {
            // StockTrendAnalysis.CurrentPrice is itself nullable — persistence must not be blocked
            // on it; such rows just can't be evaluated later (the evaluation query filters them out).
            await _recorder.RecordAsync("2330", "TW", "user-1", BuildAnalysis(currentPrice: null), CancellationToken.None);

            var rows = _dbContext.StockPredictions.Where(p => p.StockCode == "2330").ToList();
            Assert.Equal(3, rows.Count);
            Assert.All(rows, r => Assert.Null(r.PriceAtPrediction));
        }
    }
}
