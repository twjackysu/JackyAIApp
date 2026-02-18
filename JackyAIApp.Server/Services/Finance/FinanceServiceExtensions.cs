using JackyAIApp.Server.Services.Finance.DataProviders;
using JackyAIApp.Server.Services.Finance.Indicators;
using TWStockLib.Services;

namespace JackyAIApp.Server.Services.Finance
{
    /// <summary>
    /// Extension methods for registering finance-related services.
    /// </summary>
    public static class FinanceServiceExtensions
    {
        /// <summary>
        /// Registers all finance analysis services including TWStockLib,
        /// indicator calculators, data providers, and the indicator engine.
        /// </summary>
        public static IServiceCollection AddFinanceAnalysisServices(this IServiceCollection services)
        {
            // Register TWStockLib services
            services.AddStockServices();

            // Register all indicator calculators (Strategy Pattern)
            // Adding a new indicator = add one line here (or use assembly scanning)
            services.AddSingleton<IIndicatorCalculator, MACalculator>();
            services.AddSingleton<IIndicatorCalculator, RSICalculator>();
            services.AddSingleton<IIndicatorCalculator, MACDCalculator>();
            services.AddSingleton<IIndicatorCalculator, KDCalculator>();
            services.AddSingleton<IIndicatorCalculator, VolumeRatioCalculator>();
            services.AddSingleton<IIndicatorCalculator, BollingerBandCalculator>();

            // Register indicator engine
            services.AddSingleton<IIndicatorEngine, IndicatorEngine>();

            // Register data providers
            services.AddScoped<TWStockLibHistoricalProvider>();
            services.AddScoped<IMarketDataProvider>(sp =>
            {
                var inner = sp.GetRequiredService<TWStockLibHistoricalProvider>();
                var cache = sp.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>();
                var logger = sp.GetRequiredService<ILogger<CachedMarketDataProvider>>();
                return new CachedMarketDataProvider(inner, cache, logger);
            });

            return services;
        }
    }
}
