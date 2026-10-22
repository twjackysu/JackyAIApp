using JackyAIApp.Server.Data.Models.SQL;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JackyAIApp.Server.Data.Configurations
{
    // Each user's completed daily challenges.

    public sealed class DailyChallengeResultConfiguration : IEntityTypeConfiguration<DailyChallengeResult>
    {
        public void Configure(EntityTypeBuilder<DailyChallengeResult> builder)
        {
            builder.HasOne(dc => dc.User)
                .WithMany()
                .HasForeignKey(dc => dc.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // Index for faster queries: one result per user per day
            builder.HasIndex(dc => new { dc.UserId, dc.ChallengeDate })
                .IsUnique();
        }
    }
}
