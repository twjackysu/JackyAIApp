using JackyAIApp.Server.Common;
using JackyAIApp.Server.DTO;
using JackyAIApp.Server.DTO.Finance;

namespace JackyAIApp.Server.Services.Finance
{
    /// <summary>
    /// Per-stock analysis flows (cache → external data → indicator engine / AI). Covers the AI
    /// stock summary, the single technical / chip / score endpoints, the comprehensive weighted
    /// analysis, and the trendline chart's price bars. Controllers stay thin and map the ServiceResult.
    /// </summary>
    public interface IStockAnalysisQueryService
    {
        Task<ServiceResult<StockTrendAnalysis>> AnalyzeStockAsync(StockSearchRequest request, CancellationToken cancellationToken);
        Task<ServiceResult<TechnicalAnalysisResponse>> GetTechnicalIndicatorsAsync(string stockCode, CancellationToken cancellationToken);
        Task<ServiceResult<ChipAnalysisResponse>> GetChipAnalysisAsync(string stockCode, CancellationToken cancellationToken);
        Task<ServiceResult<StockScoreResponse>> GetStockScoreAsync(string stockCode, CancellationToken cancellationToken);
        Task<ServiceResult<StockAnalysisResult>> GetComprehensiveAnalysisAsync(StockAnalysisRequest request, CancellationToken cancellationToken);

        /// <summary>Daily OHLC bars for the trendline chart: a stock's cached recent history, or about six years of the TAIEX for code <c>TAIEX</c>.</summary>
        Task<ServiceResult<PriceBarsResponse>> GetPriceBarsAsync(string code, string? market, CancellationToken cancellationToken);
    }
}
