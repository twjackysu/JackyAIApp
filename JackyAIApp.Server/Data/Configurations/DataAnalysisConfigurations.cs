using JackyAIApp.Server.Data.Models.SQL;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JackyAIApp.Server.Data.Configurations
{
    // Data Analysis: each user's saved data sources, and dashboards with their widgets and shares.

    public sealed class DataSourceConfigConfiguration : IEntityTypeConfiguration<DataSourceConfig>
    {
        public void Configure(EntityTypeBuilder<DataSourceConfig> builder)
        {
            builder.HasOne(ds => ds.User)
                .WithMany(u => u.DataSourceConfigs)
                .HasForeignKey(ds => ds.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // Index for fast per-user listing
            builder.HasIndex(ds => ds.UserId);
        }
    }

    public sealed class DashboardConfiguration : IEntityTypeConfiguration<Dashboard>
    {
        public void Configure(EntityTypeBuilder<Dashboard> builder)
        {
            builder.HasOne(d => d.User)
                .WithMany()
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            builder.HasIndex(d => d.UserId);
            // By name, as the column always held it ("Private", "Public", ...).
            builder.Property(d => d.Visibility)
                .HasConversion<string>();
        }
    }

    public sealed class DashboardWidgetConfiguration : IEntityTypeConfiguration<DashboardWidget>
    {
        public void Configure(EntityTypeBuilder<DashboardWidget> builder)
        {
            builder.HasOne(w => w.Dashboard)
                .WithMany(d => d.Widgets)
                .HasForeignKey(w => w.DashboardId)
                .OnDelete(DeleteBehavior.Cascade);
            builder.HasIndex(w => w.DashboardId);
        }
    }

    public sealed class DashboardShareConfiguration : IEntityTypeConfiguration<DashboardShare>
    {
        public void Configure(EntityTypeBuilder<DashboardShare> builder)
        {
            builder.HasOne(s => s.Dashboard)
                .WithMany(d => d.Shares)
                .HasForeignKey(s => s.DashboardId)
                .OnDelete(DeleteBehavior.Cascade);
            // Grantee user — NoAction to avoid multiple cascade paths (User → Dashboard → Share).
            builder.HasOne(s => s.User)
                .WithMany()
                .HasForeignKey(s => s.UserId)
                .OnDelete(DeleteBehavior.NoAction);
            builder.HasIndex(s => new { s.DashboardId, s.UserId })
                .IsUnique();
        }
    }
}
