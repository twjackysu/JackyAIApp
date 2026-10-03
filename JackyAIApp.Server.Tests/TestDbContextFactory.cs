using JackyAIApp.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace JackyAIApp.Server.Tests
{
    /// <summary>
    /// An <see cref="IDbContextFactory{TContext}"/> over fixed options, for services that open their
    /// own short-lived contexts. With an InMemory database every context it creates sees the same
    /// data as the test's own context built from the same options.
    ///
    /// Records each context it hands out, so a test can tell whether concurrent work really ran on
    /// separate contexts — the property that keeps it safe against a real database.
    /// </summary>
    public sealed class TestDbContextFactory(DbContextOptions<AzureSQLDBContext> options)
        : IDbContextFactory<AzureSQLDBContext>
    {
        private readonly List<AzureSQLDBContext> _created = [];

        public IReadOnlyList<AzureSQLDBContext> Created
        {
            get { lock (_created) return _created.ToList(); }
        }

        public AzureSQLDBContext CreateDbContext()
        {
            var context = new AzureSQLDBContext(options);
            lock (_created) _created.Add(context);
            return context;
        }
    }
}
