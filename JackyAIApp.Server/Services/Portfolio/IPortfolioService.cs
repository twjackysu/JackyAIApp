using JackyAIApp.Server.Common;
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
        Task<PortfolioSummaryDto> GetPortfolioSummaryAsync(
            string userId, decimal feeDiscountRate = 0.28m, CancellationToken ct = default);
        Task<PortfolioSummaryDto> RefreshPositionPriceAsync(
            string userId, string symbol, decimal feeDiscountRate = 0.28m, CancellationToken ct = default);
        Task<List<NetWorthDataPoint>> GetNetWorthHistoryAsync(
            string userId, int days, NetWorthGroupBy groupBy = NetWorthGroupBy.AssetType, CancellationToken ct = default);
        Task<List<NetWorthDataPoint>> GetNetWorthHistoryAsync(
            string userId, DateOnly? from, DateOnly? to, NetWorthGroupBy groupBy = NetWorthGroupBy.AssetType,
            CancellationToken ct = default);
        Task<DrawdownAnalysisDto> GetDrawdownAnalysisAsync(
            string userId, int days, CancellationToken ct = default);
        Task<BenchmarkComparisonDto> GetBenchmarkComparisonAsync(
            string userId, int days, IReadOnlyList<string>? benchmarkSymbols = null, CancellationToken ct = default);
    }
}
