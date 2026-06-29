using JackyAIApp.Server.DTO;

namespace JackyAIApp.Server.Services.Finance
{
    public interface IFinanceAnalysisService
    {
        Task<(StockTrendAnalysis? analysis, string? errorDetail)> AnalyzeStockWithAIAsync(string stockCodeOrName, string stockData, string userId, CancellationToken cancellationToken = default);
    }
}