using JackyAIApp.Server.Common;
using JackyAIApp.Server.DTO;

namespace JackyAIApp.Server.Services.Finance
{
    public interface IFinanceAnalysisService
    {
        Task<ServiceResult<StockTrendAnalysis>> AnalyzeStockWithAIAsync(string stockCodeOrName, string stockData, string userId, CancellationToken cancellationToken = default);
    }
}