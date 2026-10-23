using DataAnalysisLibrary.Core.Engine;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.Services;
using JackyAIApp.Server.Services.DataAnalysis;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace JackyAIApp.Server.Tests.TestHelpers
{
    /// <summary>
    /// Builds <see cref="DashboardService"/> for tests, so what it is made of is written in this one file.
    /// Set only what the test exercises.
    /// </summary>
    public sealed class DashboardServiceBuilder
    {
        public required AzureSQLDBContext Db { get; init; }

        /// <summary>Who is looking.</summary>
        public required ICurrentUser Viewer { get; init; }

        /// <summary>The engine of the built-in default source; null when no widget query may run at all.</summary>
        public DataAnalysisEngine? DefaultEngine { get; init; }

        /// <summary>Builds a custom source's engine; needed only for a custom source.</summary>
        public IDataSourceConfigService? ConfigService { get; init; }

        public DashboardService Build()
        {
            IDataSourceResolver resolver = DefaultEngine is null
                ? new Mock<IDataSourceResolver>(MockBehavior.Strict).Object
                : new DataSourceResolver(DefaultEngine, Db, ConfigService!, NullLogger<DataSourceResolver>.Instance);
            var engines = new EngineSources(resolver, Viewer, new MemoryCache(new MemoryCacheOptions()), NullLogger<EngineSources>.Instance);
            return new DashboardService(NullLogger<DashboardService>.Instance, Db, engines, Viewer);
        }
    }
}
