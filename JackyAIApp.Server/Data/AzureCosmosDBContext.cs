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
            modelBuilder.HasDefaultContainer("Word");
            modelBuilder.Entity<Word>().ToContainer("Word");
            modelBuilder.HasDefaultContainer("PersonalWord");
            modelBuilder.Entity<PersonalWord>().ToContainer("PersonalWord");
        }
        public virtual DbSet<Word> Word { get; set; }
        public virtual DbSet<PersonalWord> PersonalWord { get; set; }
    }
}
