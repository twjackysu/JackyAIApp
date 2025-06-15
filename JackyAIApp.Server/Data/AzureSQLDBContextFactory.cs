using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace JackyAIApp.Server.Data
{
    public class AzureSQLDBContextFactory : IDesignTimeDbContextFactory<AzureSQLDBContext>
    {
        public AzureSQLDBContext CreateDbContext(string[] args)
        {
            // Load configuration from appsettings.json for design-time tools like migrations
            IConfigurationRoot configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json")
                .AddJsonFile("appsettings.Development.json", optional: true)
                .Build();

            var optionsBuilder = new DbContextOptionsBuilder<AzureSQLDBContext>();
            
            // Get connection string from configuration
            string connectionString = configuration.GetConnectionString("SQLConnection") 
                ?? throw new InvalidOperationException("Connection string 'SQLConnection' not found.");
                
            optionsBuilder.UseSqlServer(connectionString);

            return new AzureSQLDBContext(optionsBuilder.Options);
        }
    }
}
