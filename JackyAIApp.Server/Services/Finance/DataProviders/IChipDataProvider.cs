using JackyAIApp.Server.DTO.Finance;

namespace JackyAIApp.Server.Services.Finance.DataProviders
{
    /// <summary>
    /// Interface for chip (籌碼) data providers.
    /// Separated from IMarketDataProvider to allow distinct DI registration and testability.
    /// </summary>
    public interface IChipDataProvider
    {
        /// <summary>Fetch chip data for the given stock code.</summary>
        Task<MarketData> FetchAsync(string stockCode, CancellationToken cancellationToken = default);
    }
}
