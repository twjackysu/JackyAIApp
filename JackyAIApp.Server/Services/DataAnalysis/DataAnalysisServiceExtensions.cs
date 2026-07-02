using DataAnalysisLibrary.Connectors.MsSql;
using DataAnalysisLibrary.Core.Engine;

namespace JackyAIApp.Server.Services.DataAnalysis
{
    /// <summary>
    /// Extension methods for registering DataAnalysis-domain services.
    /// </summary>
    public static class DataAnalysisServiceExtensions
    {
        /// <summary>
        /// Registers config/session services plus the DataAnalysisEngine — a singleton since
        /// the engine itself is stateless; per-query connections are opened as needed.
        /// </summary>
        public static IServiceCollection AddDataAnalysisServices(this IServiceCollection services, string sqlConnectionString)
        {
            services.AddScoped<IDataSourceConfigService, DataSourceConfigService>();
            services.AddScoped<ISessionDataStore, SessionDataStore>();

            services.AddSingleton(_ =>
                new DataAnalysisEngineBuilder()
                    .AddSource(new MsSqlDataSourceFactory(new MsSqlConfig
                    {
                        SourceKey = "mssql",
                        ConnectionString = sqlConnectionString
                    }))
                    .Build());

            return services;
        }
    }
}
