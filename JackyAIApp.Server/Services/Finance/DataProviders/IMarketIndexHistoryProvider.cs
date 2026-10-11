using JackyAIApp.Server.DTO.Finance;

namespace JackyAIApp.Server.Services.Finance.DataProviders
{
    /// <summary>
    /// Multi-year daily closes of the TAIEX price index (<c>^TWII</c>). Shared market data — no
    /// per-user input — so the cached implementation serves every visitor from one fetch.
    /// </summary>
    public interface IMarketIndexHistoryProvider
    {
        /// <summary>
        /// The history, or null when it cannot be had: the source is unreachable, or what it
        /// returned failed the sanity checks. Never a partial or repaired series.
        /// </summary>
        Task<MarketIndexHistoryResponse?> GetAsync(CancellationToken cancellationToken = default);
    }
}
