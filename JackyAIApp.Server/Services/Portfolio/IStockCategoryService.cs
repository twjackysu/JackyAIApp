using JackyAIApp.Server.Common;
using JackyAIApp.Server.Data.Models.Portfolio;

namespace JackyAIApp.Server.Services.Portfolio
{
    /// <summary>
    /// Resolves a holding's fixed-vocabulary PortfolioCategory (for the net-worth-history
    /// "view by 產業" chart), lazily classified via AI once per symbol and persisted forever —
    /// same DB-cache pattern as PersistentTwHistoricalProvider's market-type resolution. See
    /// .claude/plans/portfolio-industry-classification.md.
    /// </summary>
    public interface IStockCategoryService
    {
        Task<(PortfolioCategory Category, string? SubCategory)> GetCategoryAsync(
            AssetType assetType, string symbol, string name, string market,
            CancellationToken ct = default);
    }
}
