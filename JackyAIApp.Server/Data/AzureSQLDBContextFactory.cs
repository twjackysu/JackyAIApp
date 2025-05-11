using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace JackyAIApp.Server.Data
{
  public class AzureSQLDBContextFactory : IDesignTimeDbContextFactory<AzureSQLDBContext>
  {
    public AzureSQLDBContext CreateDbContext(string[] args)
    {
      var config = new ConfigurationBuilder()
              .SetBasePath(Directory.GetCurrentDirectory())
              .AddJsonFile("appsettings.json")
              .Build();
      var connectionString = config.GetConnectionString("SQLConnection") ?? "";
      var optionsBuilder = new DbContextOptionsBuilder<AzureSQLDBContext>();
      optionsBuilder.UseSqlServer(connectionString);

      return new AzureSQLDBContext(optionsBuilder.Options);
    }
  }
}
