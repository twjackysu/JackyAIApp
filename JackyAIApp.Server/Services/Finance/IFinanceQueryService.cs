using JackyAIApp.Server.Common;
using JackyAIApp.Server.DTO;
using JackyAIApp.Server.DTO.Finance;

namespace JackyAIApp.Server.Services.Finance
{
    /// <summary>
    /// Orchestrates the Finance read/analysis flows (cache → external API → AI /
    /// indicator engine). Controllers stay thin and just map the ServiceResult.
    /// </summary>
    public interface IFinanceQueryService
    {
        Task<ServiceResult> GetDailyImportantInfoAsync(CancellationToken cancellationToken);
        Task<ServiceResult> AnalyzeStockAsync(StockSearchRequest request, CancellationToken cancellationToken);
        Task<ServiceResult> GetTechnicalIndicatorsAsync(string stockCode, CancellationToken cancellationToken);
        Task<ServiceResult> GetChipAnalysisAsync(string stockCode, CancellationToken cancellationToken);
        Task<ServiceResult> GetStockScoreAsync(string stockCode, CancellationToken cancellationToken);
        Task<ServiceResult> GetComprehensiveAnalysisAsync(StockAnalysisRequest request, CancellationToken cancellationToken);
        Task<ServiceResult> GetMacroEconomyAsync(CancellationToken cancellationToken);
        Task<ServiceResult> GetMarketOverviewAsync(CancellationToken cancellationToken);
    }
}
