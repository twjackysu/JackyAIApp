using JackyAIApp.Server.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace JackyAIApp.Server.Data
{
    public class AzureCosmosDBContext : DbContext
    {
        public AzureCosmosDBContext(DbContextOptions<AzureCosmosDBContext> options)
            : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.HasDefaultContainer("WordDefinition");
            modelBuilder.Entity<WordDefinition>().ToContainer("WordDefinition");
        }
        public virtual DbSet<WordDefinition> WordDefinition { get; set; }
    }
}
