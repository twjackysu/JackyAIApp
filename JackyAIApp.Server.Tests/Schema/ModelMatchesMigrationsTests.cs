using JackyAIApp.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace JackyAIApp.Server.Tests.Schema
{
    /// <summary>
    /// The model the entities and Data/Configurations build must be the one the last migration
    /// left. Startup runs <c>Database.Migrate()</c>, and EF refuses to migrate while the model has
    /// changes no migration covers — so a forgotten migration would stop the app from starting,
    /// in production. The same check as <c>dotnet ef migrations has-pending-model-changes</c>;
    /// nothing connects to a database.
    /// </summary>
    public class ModelMatchesMigrationsTests
    {
        [Fact]
        public void TheModel_HasNoChangesWithoutAMigration()
        {
            using var db = new AzureSQLDBContext(new DbContextOptionsBuilder<AzureSQLDBContext>()
                .UseSqlServer("Server=.;Database=never-opened")
                .Options);

            // Changed an entity or a configuration? Add a migration (JackyAIApp.Server/CLAUDE.md).
            Assert.False(db.Database.HasPendingModelChanges());
        }
    }
}
