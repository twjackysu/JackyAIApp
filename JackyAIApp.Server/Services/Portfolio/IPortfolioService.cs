using JackyAIApp.Server.DTO.Portfolio;

namespace JackyAIApp.Server.Services.Portfolio
{
    public interface IPortfolioService
    {
        Task SaveHoldingsAsync(string userId, SaveHoldingsRequest req, CancellationToken ct = default);
        Task<SaveHoldingsRequest> GetLatestHoldingsAsync(string userId, CancellationToken ct = default);
        Task<SaveHoldingsRequest?> GetSnapshotHoldingsAsync(string userId, int snapshotId, CancellationToken ct = default);
        Task<bool> DeleteSnapshotAsync(string userId, int snapshotId, CancellationToken ct = default);
        Task<List<UpdateHistoryItem>> GetUpdateHistoryAsync(string userId, CancellationToken ct = default);
        Task<PortfolioSummaryDto> GetPortfolioSummaryAsync(string userId, CancellationToken ct = default);
        Task<List<NetWorthDataPoint>> GetNetWorthHistoryAsync(string userId, int days, CancellationToken ct = default);
        Task<StockLookupDto?> LookupStockAsync(string market, string symbol, CancellationToken ct = default);
        Task<List<StockSearchItem>> SearchStocksAsync(string market, string keyword, CancellationToken ct = default);
        Task<List<FundSearchItem>> SearchFundsAsync(string keyword, CancellationToken ct = default);
        Task<HoldingPriceHistoryDto?> GetHoldingPriceHistoryAsync(string userId, string market, string symbol, CancellationToken ct = default);
        Task<(PortfolioAiSummaryDto? Result, string? Error)> GetAiTechSummaryAsync(string userId, string market, string symbol, CancellationToken ct = default);
    }
}
