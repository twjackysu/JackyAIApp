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

        /// <summary>
        /// Enrich fundamental data with derived ratios using market price.
        /// Default implementation is a no-op (e.g., TWSE already returns ratios).
        /// Providers that return raw values (e.g., SEC EDGAR) should override this
        /// to compute P/E, P/B, Dividend Yield, etc.
        /// </summary>
        void EnrichWithMarketPrice(FundamentalData data, decimal latestPrice) { }
    }
}
