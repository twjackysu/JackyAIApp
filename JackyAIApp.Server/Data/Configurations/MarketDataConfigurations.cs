using JackyAIApp.Server.Data.Models.SQL;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JackyAIApp.Server.Data.Configurations
{
    // Market data shared across users: stored prices, the total-return index, FX rates, and the
    // stock predictions kept for accuracy tracking.

    public sealed class DailyStockPriceConfiguration : IEntityTypeConfiguration<DailyStockPrice>
    {
        public void Configure(EntityTypeBuilder<DailyStockPrice> builder)
        {
            // Persistent TW stock price cache (shared across users, immutable finalized bars).
            builder.HasIndex(p => new { p.Symbol, p.TradeDate })
                .IsUnique();
            builder.Property(p => p.Open).HasPrecision(18, 4);
            builder.Property(p => p.High).HasPrecision(18, 4);
            builder.Property(p => p.Low).HasPrecision(18, 4);
            builder.Property(p => p.Close).HasPrecision(18, 4);
        }
    }

    public sealed class TaiexTotalReturnDailyConfiguration : IEntityTypeConfiguration<TaiexTotalReturnDaily>
    {
        public void Configure(EntityTypeBuilder<TaiexTotalReturnDaily> builder)
        {
            // 發行量加權股價報酬指數 daily closes for the 回落煞車 signal (shared across users).
            builder.Property(t => t.Close).HasPrecision(18, 4);
        }
    }

    public sealed class FxRateConfiguration : IEntityTypeConfiguration<FxRate>
    {
        public void Configure(EntityTypeBuilder<FxRate> builder)
        {
            // Published FX rates (shared across users, immutable once published).
            builder.HasIndex(r => new { r.Source, r.Currency, r.RateDate })
                .IsUnique();
            // 6dp because that is exactly what 集保 publishes (e.g. 31.797500); rounding to 2dp
            // moves a 1.5M position by a couple of hundred TWD for no reason.
            builder.Property(r => r.BuyRate).HasPrecision(18, 6);
            builder.Property(r => r.SellRate).HasPrecision(18, 6);
            builder.Property(r => r.Currency).HasMaxLength(3);
            builder.Property(r => r.Source).HasMaxLength(16);
        }
    }

    public sealed class StockPredictionConfiguration : IEntityTypeConfiguration<StockPrediction>
    {
        public void Configure(EntityTypeBuilder<StockPrediction> builder)
        {
            // Finance prediction-accuracy tracking (TASK-013) — supports the "which due rows still
            // need evaluating, oldest first" query PredictionEvaluationService runs.
            builder.HasIndex(p => new { p.EvaluatedAtUtc, p.PredictedAtUtc });
            builder.Property(p => p.PriceAtPrediction).HasPrecision(18, 4);
            builder.Property(p => p.ActualPriceAtEvaluation).HasPrecision(18, 4);
        }
    }
}
