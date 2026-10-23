using JackyAIApp.Server.Common;
using JackyAIApp.Server.DTO.Finance;
using JackyAIApp.Server.Services.Market;
using Microsoft.Extensions.Caching.Memory;

namespace JackyAIApp.Server.Services.Finance
{
    public class MarketDataQueryService(
        ILogger<MarketDataQueryService> logger,
        IMemoryCache memoryCache,
        IMacroEconomyProvider macroEconomyProvider,
        IMarketOverviewProvider marketOverviewProvider,
        IMarketIndexHistoryProvider marketIndexHistoryProvider,
        IIndicatorProvider indicatorProvider,
        TimeProvider? timeProvider = null) : IMarketDataQueryService
    {
        private readonly ILogger<MarketDataQueryService> _logger = logger;
        private readonly IMemoryCache _memoryCache = memoryCache;
        private readonly IMacroEconomyProvider _macroEconomyProvider = macroEconomyProvider;
        private readonly IMarketOverviewProvider _marketOverviewProvider = marketOverviewProvider;
        private readonly IMarketIndexHistoryProvider _marketIndexHistoryProvider = marketIndexHistoryProvider;
        private readonly IIndicatorProvider _indicatorProvider = indicatorProvider;
        private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

        public async Task<ServiceResult<MacroEconomyResponse>> GetMacroEconomyAsync(CancellationToken cancellationToken)
        {
            var cacheKey = $"MacroEconomy_{_time.GetTaipeiToday():yyyyMMdd}";
            try
            {
                var result = await _memoryCache.GetOrCreateSingleFlightAsync(
                    cacheKey, TimeSpan.FromHours(2),
                    () => _macroEconomyProvider.FetchAsync(CancellationToken.None), cancellationToken);
                return ServiceResult.Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to fetch macro economy data");
                return ServiceResult.Fail(ErrorCodes.InternalServerError, "Failed to fetch macro economy data.");
            }
        }

        public async Task<ServiceResult<MarketOverviewResponse>> GetMarketOverviewAsync(CancellationToken cancellationToken)
        {
            const string cacheKey = "MarketOverview";
            try
            {
                // Short TTL — these are intraday snapshots, but cache enough to shield the upstream
                // (free, unofficial) source from per-request load; single-flight collapses the
                // cold-cache burst into one fetch.
                var result = await _memoryCache.GetOrCreateSingleFlightAsync(
                    cacheKey, TimeSpan.FromMinutes(3),
                    () => _marketOverviewProvider.FetchAsync(CancellationToken.None), cancellationToken);
                return ServiceResult.Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to fetch market overview data");
                return ServiceResult.Fail(ErrorCodes.InternalServerError, "Failed to fetch market overview data.");
            }
        }

        /// <summary>
        /// Multi-year daily TAIEX closes for the "same period, one line per year" chart. Caching,
        /// single-flight and the last-good fallback live in the provider; this only maps "no data"
        /// to an error rather than ever returning an empty or partial series as a success.
        /// </summary>
        public async Task<ServiceResult<MarketIndexHistoryResponse>> GetMarketIndexHistoryAsync(CancellationToken cancellationToken)
        {
            var history = await _marketIndexHistoryProvider.GetAsync(cancellationToken);
            return history is null
                ? ServiceResult.Fail(ErrorCodes.MarketHistoryUnavailable, "Market index history is temporarily unavailable.")
                : ServiceResult.Ok(history);
        }

        public async Task<ServiceResult<IndicatorExplorerResponse>> GetIndicatorsAsync(CancellationToken cancellationToken)
        {
            if (!_indicatorProvider.IsConfigured)
                return ServiceResult.Fail(ErrorCodes.InternalServerError, "Indicator data source is not configured (FRED API key missing).");

            const string cacheKey = "Indicators";
            try
            {
                var result = await _memoryCache.GetOrCreateSingleFlightAsync(
                    cacheKey, TimeSpan.FromHours(6),
                    () => _indicatorProvider.FetchAsync(CancellationToken.None), cancellationToken);
                return ServiceResult.Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to fetch indicators");
                return ServiceResult.Fail(ErrorCodes.InternalServerError, "Failed to fetch indicators.");
            }
        }
    }
}
