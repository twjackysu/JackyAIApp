using JackyAIApp.Server.DTO.Portfolio;

namespace JackyAIApp.Server.Services.Portfolio
{
    public interface IPortfolioService
    {
        Task SaveHoldingsAsync(string userId, SaveHoldingsRequest req, CancellationToken ct = default);
        Task<SaveHoldingsRequest> GetLatestHoldingsAsync(string userId, CancellationToken ct = default);
        Task<List<UpdateHistoryItem>> GetUpdateHistoryAsync(string userId, CancellationToken ct = default);
        Task<PortfolioSummaryDto> GetPortfolioSummaryAsync(string userId, CancellationToken ct = default);
        Task<List<NetWorthDataPoint>> GetNetWorthHistoryAsync(string userId, int days, CancellationToken ct = default);
        Task<StockLookupDto?> LookupStockAsync(string market, string symbol, CancellationToken ct = default);
        Task<HoldingPriceHistoryDto?> GetHoldingPriceHistoryAsync(string userId, string market, string symbol, CancellationToken ct = default);
    }
}
