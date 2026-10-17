using JackyAIApp.Server.Common;

namespace JackyAIApp.Server.Services.Finance
{
    /// <summary>
    /// Market-wide data, shared by every visitor from one cached copy — no AI and no usage cost:
    /// the macro-economy overview, the at-a-glance market dashboard, the multi-year TAIEX series,
    /// and the FRED indicator set.
    /// </summary>
    public interface IMarketDataQueryService
    {
        Task<ServiceResult> GetMacroEconomyAsync(CancellationToken cancellationToken);
        Task<ServiceResult> GetMarketOverviewAsync(CancellationToken cancellationToken);
        Task<ServiceResult> GetMarketIndexHistoryAsync(CancellationToken cancellationToken);
        Task<ServiceResult> GetIndicatorsAsync(CancellationToken cancellationToken);
    }
}
