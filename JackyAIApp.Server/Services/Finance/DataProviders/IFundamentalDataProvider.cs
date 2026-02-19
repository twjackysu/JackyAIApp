using JackyAIApp.Server.DTO.Finance;

namespace JackyAIApp.Server.Services.Finance.DataProviders
{
    /// <summary>
    /// Interface for fundamental data providers (P/E, P/B, EPS, revenue).
    /// </summary>
    public interface IFundamentalDataProvider
    {
        /// <summary>Fetch fundamental data for the given stock code.</summary>
        Task<FundamentalData?> FetchAsync(string stockCode, CancellationToken cancellationToken = default);
    }
}
