using JackyAIApp.Server.Data.Models.SQL;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JackyAIApp.Server.Data.Configurations
{
    // The connectors a user lets the AI use (module Connectors).

    public sealed class ConnectorInstallationConfiguration : IEntityTypeConfiguration<ConnectorInstallation>
    {
        public void Configure(EntityTypeBuilder<ConnectorInstallation> builder)
        {
            // A catalog entry is added once per user; a server of their own may be added many times.
            builder.HasIndex(c => new { c.UserId, c.DefinitionKey })
                .IsUnique()
                .HasFilter("[DefinitionKey] <> 'custom'");

            builder.HasIndex(c => c.UserId);
        }
    }

    public sealed class SiteAccessTokenConfiguration : IEntityTypeConfiguration<SiteAccessToken>
    {
        public void Configure(EntityTypeBuilder<SiteAccessToken> builder)
        {
            // A request's token is looked up by its hash.
            builder.HasIndex(t => t.TokenHash).IsUnique();

            builder.HasIndex(t => t.UserId);
        }
    }
}
