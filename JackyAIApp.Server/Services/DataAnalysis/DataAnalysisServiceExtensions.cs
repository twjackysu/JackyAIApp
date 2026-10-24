using DataAnalysisLibrary.Connectors.MsSql;
using DataAnalysisLibrary.Core.Engine;
using JackyAIApp.Server.Common;
using JackyAIApp.Server.Services.Assistant;
using JackyAIApp.Server.Services.DataAnalysis.Assistant;

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
            // Shared source resolution (default vs custom, ownership, saved-settings validation),
            // used by the query endpoints, the NL question and dashboard widgets.
            services.AddScoped<IDataSourceResolver, DataSourceResolver>();
            services.AddScoped<IDashboardService, DashboardService>();
            services.AddScoped<IDataSourceStore, DataSourceStore>();
            // The NL-query planner (LLM pipeline behind summarize/nl), extracted from the controller.
            services.AddScoped<INlQueryPlanner, NlQueryPlanner>();
            // The engine queries and the NL question behind DataAnalysisController.
            services.AddScoped<EngineSources>();
            services.AddScoped<IDataAnalysisQueryService, DataAnalysisQueryService>();
            services.AddScoped<INlSummarizeService, NlSummarizeService>();
            // The page's AI assistant (the shared chat engine's "data-analysis" profile).
            services.AddScoped<IChatProfile, DataAnalysisChatProfile>();

            // Session uploads live in this process's memory, so the store and its byte budget are
            // process-wide singletons; the admission filter reads the same budget before a body is read.
            services.AddOptions<SessionUploadOptions>().BindConfiguration(SessionUploadOptions.SectionName);
            services.AddSingleton<SessionUploadBudget>();
            services.AddSingleton<ISessionDataStore, SessionDataStore>();
            services.AddSingleton<SessionUploadAdmissionFilter>();

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
