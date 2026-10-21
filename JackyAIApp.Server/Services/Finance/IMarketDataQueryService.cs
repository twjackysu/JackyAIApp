using JackyAIApp.Server.Common;
using JackyAIApp.Server.DTO.Finance;

namespace JackyAIApp.Server.Services.Finance
{
    /// <summary>
    /// Market-wide data, shared by every visitor from one cached copy — no AI and no usage cost:
    /// the macro-economy overview, the at-a-glance market dashboard, the multi-year TAIEX series,
    /// and the FRED indicator set.
    /// </summary>
    public interface IMarketDataQueryService
    {
        Task<ServiceResult<MacroEconomyResponse>> GetMacroEconomyAsync(CancellationToken cancellationToken);
        Task<ServiceResult<MarketOverviewResponse>> GetMarketOverviewAsync(CancellationToken cancellationToken);
        Task<ServiceResult<MarketIndexHistoryResponse>> GetMarketIndexHistoryAsync(CancellationToken cancellationToken);
        Task<ServiceResult<IndicatorExplorerResponse>> GetIndicatorsAsync(CancellationToken cancellationToken);
    }
}
