using JackyAIApp.Server.Common;
using JackyAIApp.Server.Data.Models.Portfolio;

namespace JackyAIApp.Server.Services.Portfolio
{
    /// <summary>One holding's full AI classification: exactly one closed-enum category, exactly
    /// one free-form sub-sector label, and 0-4 free-form theme tags. Tags are NOT mutually
    /// exclusive across holdings (AMZN is 電商 AND 雲端 at once) — they must never drive a
    /// stacked/summed chart, display and non-stacked views only.</summary>
    public sealed record StockCategoryInfo(
        PortfolioCategory Category, string? SubCategory, IReadOnlyList<string> Tags)
    {
        public static StockCategoryInfo Direct(PortfolioCategory category) => new(category, null, []);
    }

    /// <summary>
    /// Resolves a holding's fixed-vocabulary PortfolioCategory (for the net-worth-history
    /// "view by 產業" chart), lazily classified via AI once per symbol and persisted forever —
    /// same DB-cache pattern as PersistentTwHistoricalProvider's market-type resolution. See
    /// .claude/plans/portfolio-industry-classification.md.
    /// </summary>
    public interface IStockCategoryService
    {
        Task<StockCategoryInfo> GetCategoryAsync(
            AssetType assetType, string symbol, string name, string market,
            CancellationToken ct = default);
    }
}
