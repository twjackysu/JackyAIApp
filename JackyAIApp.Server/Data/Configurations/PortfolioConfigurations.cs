using JackyAIApp.Server.Data.Models.Portfolio;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JackyAIApp.Server.Data.Configurations
{
    // Portfolio: each user's dated holdings snapshots and their positions, and the AI classification
    // of the symbols held.

    public sealed class HoldingSnapshotConfiguration : IEntityTypeConfiguration<HoldingSnapshot>
    {
        public void Configure(EntityTypeBuilder<HoldingSnapshot> builder)
        {
            builder.HasOne(hs => hs.User)
                .WithMany()
                .HasForeignKey(hs => hs.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasIndex(hs => new { hs.UserId, hs.EffectiveDate })
                .IsUnique();

            // Was decimal(18,2), which silently rounded every stored rate to the cent and moved
            // historical valuations by a few hundred TWD on a seven-figure position.
            builder.Property(s => s.UsdTwdRate).HasPrecision(18, 6);
        }
    }

    public sealed class HoldingPositionConfiguration : IEntityTypeConfiguration<HoldingPosition>
    {
        public void Configure(EntityTypeBuilder<HoldingPosition> builder)
        {
            builder.HasOne(hp => hp.Snapshot)
                .WithMany(hs => hs.Positions)
                .HasForeignKey(hp => hp.SnapshotId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Property(hp => hp.Quantity).HasPrecision(18, 6);
            builder.Property(hp => hp.UnitPrice).HasPrecision(18, 6);
            builder.Property(hp => hp.CostBasis).HasPrecision(18, 4);
        }
    }

    public sealed class StockClassificationConfiguration : IEntityTypeConfiguration<StockClassification>
    {
        public void Configure(EntityTypeBuilder<StockClassification> builder)
        {
            builder.HasKey(c => c.Symbol);
        }
    }
}
