using JackyAIApp.Server.Data.Models.SQL;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JackyAIApp.Server.Data.Configurations
{
    // What hangs off a user's account: connected services and the daily usage ledger.

    public sealed class JiraConfigConfiguration : IEntityTypeConfiguration<JiraConfig>
    {
        public void Configure(EntityTypeBuilder<JiraConfig> builder)
        {
            builder.HasOne(jc => jc.User)
                .WithMany()
                .HasForeignKey(jc => jc.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }

    public sealed class UserConnectorConfiguration : IEntityTypeConfiguration<UserConnector>
    {
        public void Configure(EntityTypeBuilder<UserConnector> builder)
        {
            builder.HasOne(uc => uc.User)
                .WithMany()
                .HasForeignKey(uc => uc.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // Ensure unique constraint for UserId + ProviderName
            builder.HasIndex(uc => new { uc.UserId, uc.ProviderName })
                .IsUnique();
        }
    }

    public sealed class DailyUsageConfiguration : IEntityTypeConfiguration<DailyUsage>
    {
        public void Configure(EntityTypeBuilder<DailyUsage> builder)
        {
            // Daily usage-limit ledger (DECISION-006/007) — composite PK so UsageLimitService can
            // target the exact (UserId, Date) row with a single atomic conditional UPDATE.
            builder.HasKey(du => new { du.UserId, du.Date });

            builder.HasOne(du => du.User)
                .WithMany()
                .HasForeignKey(du => du.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // Stored as bigint (native integer comparison in every relational provider), rather
            // than the decimal(20,0) convention EF used for the now-removed credit columns —
            // UsageLimitService's atomic conditional UPDATE relies on the database
            // engine itself evaluating "Used + @cost <= @limit" correctly, and the SQLite provider
            // (used by this feature's concurrency tests; see UsageLimitServiceTests) stores
            // `decimal` as TEXT, which compares lexicographically rather than numerically under
            // raw SQL. Usage weights are tiny (see UsageCosts), nowhere near overflowing bigint.
            builder.Property(du => du.Used)
                .HasConversion(v => (long)v, v => (ulong)v)
                .HasColumnType("bigint");
        }
    }
}
