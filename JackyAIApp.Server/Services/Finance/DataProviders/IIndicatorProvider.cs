using JackyAIApp.Server.DTO.Finance;

namespace JackyAIApp.Server.Services.Finance.DataProviders
{
    /// <summary>
    /// Provides key macro/market indicators (value + history) for the Indicator Explorer.
    /// </summary>
    public interface IIndicatorProvider
    {
        /// <summary>Whether the upstream data source is configured (e.g., API key present).</summary>
        bool IsConfigured { get; }

        Task<IndicatorExplorerResponse> FetchAsync(CancellationToken cancellationToken = default);
    }
}
