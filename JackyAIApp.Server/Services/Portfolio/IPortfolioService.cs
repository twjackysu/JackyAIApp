using JackyAIApp.Server.DTO.Portfolio;

namespace JackyAIApp.Server.Services.Portfolio
{
    public interface IPortfolioService
    {
        Task<TransactionDto> AddTransactionAsync(string userId, AddTransactionRequest req, CancellationToken ct = default);
        Task<List<TransactionDto>> GetTransactionsAsync(string userId, CancellationToken ct = default);
        Task DeleteTransactionAsync(string userId, int id, CancellationToken ct = default);
        Task<PortfolioSummaryDto> GetPortfolioSummaryAsync(string userId, CancellationToken ct = default);
        Task<List<NetWorthDataPoint>> GetNetWorthHistoryAsync(string userId, int days, CancellationToken ct = default);
        Task TakeSnapshotAsync(string userId, CancellationToken ct = default);
        Task<HoldingPriceHistoryDto?> GetHoldingPriceHistoryAsync(string userId, string market, string symbol, CancellationToken ct = default);
        Task<StockLookupDto?> LookupStockAsync(string market, string symbol, CancellationToken ct = default);
    }
}
