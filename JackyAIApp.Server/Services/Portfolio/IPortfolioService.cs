using JackyAIApp.Server.Common;
using JackyAIApp.Server.DTO.Portfolio;

namespace JackyAIApp.Server.Services.Portfolio
{
    public interface IPortfolioService
    {
        /// <summary>Saves the reader's holdings as the snapshot of their date (replacing one already on it), within
        /// <see cref="HoldingsLimits"/>; beyond them a BadRequest and nothing saved.</summary>
        Task<ServiceResult<bool>> SaveHoldingsAsync(string userId, SaveHoldingsRequest req, CancellationToken ct = default);
        Task<SaveHoldingsRequest> GetLatestHoldingsAsync(string userId, CancellationToken ct = default);
        Task<SaveHoldingsRequest?> GetSnapshotHoldingsAsync(string userId, int snapshotId, CancellationToken ct = default);
        Task<bool> DeleteSnapshotAsync(string userId, int snapshotId, CancellationToken ct = default);
        Task<List<UpdateHistoryItem>> GetUpdateHistoryAsync(string userId, CancellationToken ct = default);
        Task<PortfolioSummaryDto> GetPortfolioSummaryAsync(
            string userId, decimal feeDiscountRate = 0.28m, CancellationToken ct = default);
        Task<PortfolioSummaryDto> RefreshPositionPriceAsync(
            string userId, string symbol, decimal feeDiscountRate = 0.28m, CancellationToken ct = default);
    }
}
