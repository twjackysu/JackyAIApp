using JackyAIApp.Server.DTO.Finance;

namespace JackyAIApp.Server.Services.Finance.DataProviders
{
    /// <summary>
    /// Provides the at-a-glance market dashboard (indices / FX / crypto / rates).
    /// </summary>
    public interface IMarketOverviewProvider
    {
        Task<MarketOverviewResponse> FetchAsync(CancellationToken cancellationToken = default);
    }
}
