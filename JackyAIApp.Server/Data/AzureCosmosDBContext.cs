using JackyAIApp.Server.Data.Models;
using JackyAIApp.Server.DTO;
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
            modelBuilder.HasDefaultContainer("Word");
            modelBuilder.Entity<Word>().ToContainer("Word");
            modelBuilder.HasDefaultContainer("User");
            modelBuilder.Entity<User>().ToContainer("User");
        }
        public virtual DbSet<Word> Word { get; set; }
        public virtual DbSet<User> User { get; set; }
    }
}
