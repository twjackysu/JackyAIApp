using JackyAIApp.Server.Services.Finance.Builder;
using JackyAIApp.Server.Services.Finance.DataProviders;
using JackyAIApp.Server.Services.Finance.DataProviders.US;
using JackyAIApp.Server.Services.Finance.Indicators;
using JackyAIApp.Server.Services.Finance.Prediction;
using JackyAIApp.Server.Services.Finance.Scoring;
using TWStockLib.Services;

namespace JackyAIApp.Server.Services.Finance
{
    /// <summary>
    /// Extension methods for registering finance-related services.
    /// </summary>
    public static class FinanceServiceExtensions
    {
        /// <summary>
        /// Registers all finance-domain services: TWSE data access, stock code
        /// resolution, AI trend analysis, TWStockLib, indicator calculators,
        /// data providers, and the indicator engine.
        /// </summary>
        public static IServiceCollection AddFinanceAnalysisServices(this IServiceCollection services)
        {
            services.AddScoped<ITWSEOpenAPIService, TWSEOpenAPIService>();
            services.AddScoped<IStockCodeResolver, StockCodeResolver>();
            services.AddScoped<ITWSEDataService, TWSEDataService>();
            services.AddScoped<IFinanceAnalysisService, FinanceAnalysisService>();

            // Register TWStockLib services
            services.AddTwStock();

            // === Register all indicator calculators (Strategy Pattern) ===
            // Technical indicators
            services.AddSingleton<IIndicatorCalculator, MACalculator>();
            services.AddSingleton<IIndicatorCalculator, RSICalculator>();
            services.AddSingleton<IIndicatorCalculator, MACDCalculator>();
            services.AddSingleton<IIndicatorCalculator, KDCalculator>();
            services.AddSingleton<IIndicatorCalculator, VolumeRatioCalculator>();
            services.AddSingleton<IIndicatorCalculator, BollingerBandCalculator>();

            // Chip indicators (TW)
            services.AddSingleton<IIndicatorCalculator, MarginIndicatorCalculator>();
            services.AddSingleton<IIndicatorCalculator, ForeignHoldingCalculator>();
            services.AddSingleton<IIndicatorCalculator, DirectorPledgeCalculator>();

            // Chip indicators (US)
            services.AddSingleton<IIndicatorCalculator, InsiderTradingCalculator>();

            // Fundamental indicators
            services.AddSingleton<IIndicatorCalculator, PERatioCalculator>();
            services.AddSingleton<IIndicatorCalculator, PBRatioCalculator>();
            services.AddSingleton<IIndicatorCalculator, DividendYieldCalculator>();
            services.AddSingleton<IIndicatorCalculator, RevenueGrowthCalculator>();
            services.AddSingleton<IIndicatorCalculator, EPSCalculator>();

            // Register indicator engine
            services.AddSingleton<IIndicatorEngine, IndicatorEngine>();

            // === Register data providers ===
            // Historical price provider (TWStockLib), wrapped by a DB-backed persistence layer
            // (survives Azure App Service free-tier cold starts, which wipe IMemoryCache — see
            // .claude/plans/portfolio-db-price-cache.md), then the existing 4h memory cache, then an
            // outermost today-price overlay (see TwTodayOverlayMarketDataProvider — deliberately
            // outside the 4h cache, not inside it, or today's still-moving price would freeze for
            // up to 4 hours).
            services.AddScoped<TWStockLibHistoricalProvider>();
            services.AddScoped<ITWStockLibHistoricalProvider>(sp => sp.GetRequiredService<TWStockLibHistoricalProvider>());
            services.AddScoped<PersistentTwHistoricalProvider>();
            services.AddScoped<IMarketDataProvider>(sp =>
            {
                var inner = sp.GetRequiredService<PersistentTwHistoricalProvider>();
                var cache = sp.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>();
                var cachedLogger = sp.GetRequiredService<ILogger<CachedMarketDataProvider>>();
                var cached = new CachedMarketDataProvider(inner, cache, cachedLogger);
                var quoteSource = sp.GetRequiredService<ITWStockLibHistoricalProvider>();
                var overlayLogger = sp.GetRequiredService<ILogger<TwTodayOverlayMarketDataProvider>>();
                return new TwTodayOverlayMarketDataProvider(cached, quoteSource, overlayLogger);
            });

            // Chip data provider (TWSE OpenAPI)
            services.AddScoped<TWSEChipDataProvider>();
            services.AddScoped<IChipDataProvider>(sp => sp.GetRequiredService<TWSEChipDataProvider>());

            // Fundamental data provider (TWSE OpenAPI)
            services.AddScoped<IFundamentalDataProvider, TWSEFundamentalDataProvider>();

            // Macro economy provider (TWSE + BOT + CBC)
            services.AddScoped<IMacroEconomyProvider, TWSEMacroEconomyProvider>();

            // Market overview / dashboard provider (Yahoo Finance quotes)
            services.AddScoped<IMarketOverviewProvider, MarketOverviewProvider>();

            // Multi-year ^TWII daily closes (Yahoo) for the "same period, one line per year" chart.
            // Singleton: the cache in front of it is one copy shared by every visitor.
            services.AddSingleton<MarketIndexHistoryProvider>();
            services.AddSingleton<IMarketIndexHistoryProvider>(sp => new CachedMarketIndexHistoryProvider(
                sp.GetRequiredService<MarketIndexHistoryProvider>(),
                sp.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>(),
                sp.GetRequiredService<ILogger<CachedMarketIndexHistoryProvider>>()));

            // Indicator Explorer provider (FRED)
            services.AddScoped<IIndicatorProvider, FredIndicatorProvider>();

            // Market news provider (Finnhub) — used by Market Brief
            services.AddScoped<IFinnhubNewsProvider, FinnhubNewsProvider>();

            // === US market data providers ===
            services.AddScoped<YahooFinanceMarketDataProvider>();
            services.AddScoped<SECFundamentalDataProvider>();
            services.AddScoped<SECInsiderTradingProvider>();

            // Market data provider factory (routes TW/US)
            services.AddScoped<IMarketDataProviderFactory, MarketDataProviderFactory>();

            // === Register scoring system ===
            services.AddSingleton<CategoryWeightConfig>();
            services.AddScoped<IStockScoreService, StockScoreService>();

            // === Register builder ===
            services.AddScoped<StockAnalysisBuilder>();

            // === Finance query orchestration (used by FinanceController) ===
            // Split by concern: per-stock analysis, shared market data, and the AI-backed market
            // reads (MarketInsightService grounds its prompts in MarketDataQueryService's caches).
            services.AddScoped<IStockAnalysisQueryService, StockAnalysisQueryService>();
            services.AddScoped<IMarketDataQueryService, MarketDataQueryService>();
            services.AddScoped<IMarketInsightService, MarketInsightService>();

            // === Prediction-accuracy tracking (TASK-013) ===
            services.AddScoped<IStockPredictionRecorder, StockPredictionRecorder>();
            services.AddScoped<IPredictionEvaluationService, PredictionEvaluationService>();

            return services;
        }
    }
}
