using JackyAIApp.Server.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace JackyAIApp.Server.Data
{
  public class AzureSQLDBContext : DbContext
  {
    public AzureSQLDBContext(DbContextOptions<AzureSQLDBContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
      modelBuilder.Entity<Word>().ToTable("Words");
      modelBuilder.Entity<Word>().HasKey(w => w.Id);

      modelBuilder.Entity<User>().ToTable("Users");
      modelBuilder.Entity<User>().HasKey(u => u.Id);
      base.OnModelCreating(modelBuilder);
    }

    public virtual DbSet<Word> Words { get; set; } = null!;
    public virtual DbSet<User> Users { get; set; } = null!;
  }
}
