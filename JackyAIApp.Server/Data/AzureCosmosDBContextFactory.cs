using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using System.Configuration;

namespace JackyAIApp.Server.Data
{
    // Migrations are designed around relational databases and hence IMigrator is not available.
    // ref: https://github.com/dotnet/efcore/issues/13200#issuecomment-418870904
    public class AzureCosmosDBContextFactory : IDesignTimeDbContextFactory<AzureCosmosDBContext>
    {
        public AzureCosmosDBContext CreateDbContext(string[] args)
        {
            var config = new ConfigurationBuilder()
                    .SetBasePath(Directory.GetCurrentDirectory())
                    .AddJsonFile("appsettings.json")
                    .Build();
            var connectionString = config.GetConnectionString("AzureCosmosDBConnection") ?? "";
            var databaseName = config.GetValue<string>("Settings:AzureCosmosDatabaseName") ?? "";
            var optionsBuilder = new DbContextOptionsBuilder<AzureCosmosDBContext>();
            optionsBuilder.UseCosmos(connectionString, databaseName);

            return new AzureCosmosDBContext(optionsBuilder.Options);
        }
    }
}
