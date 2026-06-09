using JackyAIApp.Server.Common;
using JackyAIApp.Server.DTO;
using JackyAIApp.Server.DTO.Finance;
using JackyAIApp.Server.Services.Finance;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JackyAIApp.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class FinanceController(
        IMyResponseFactory responseFactory,
        IFinanceQueryService financeQueryService) : ControllerBase
    {
        private readonly IMyResponseFactory _responseFactory = responseFactory;
        private readonly IFinanceQueryService _financeQueryService = financeQueryService;

        /// <summary>Gets the daily important financial information from Taiwan Stock Exchange.</summary>
        [Authorize]
        [HttpGet("dailyimportantinfo")]
        public async Task<IActionResult> GetDailyImportantInfo(CancellationToken cancellationToken = default) =>
            ToActionResult(await _financeQueryService.GetDailyImportantInfoAsync(cancellationToken));

        /// <summary>Analyzes a specific stock using TWSE APIs and OpenAI.</summary>
        [Authorize]
        [HttpPost("analyze-stock")]
        [ConsumeCredits(CreditCosts.StockAnalysis, "stock_analysis", "AI stock analysis")]
        public async Task<IActionResult> AnalyzeStock([FromBody] StockSearchRequest request, CancellationToken cancellationToken = default) =>
            ToActionResult(await _financeQueryService.AnalyzeStockAsync(request, cancellationToken));

        /// <summary>Gets technical indicators (MA, RSI, MACD, KD, Volume, Bollinger Bands) for a stock.</summary>
        [HttpGet("technical-indicators/{stockCode}")]
        public async Task<IActionResult> GetTechnicalIndicators(string stockCode, CancellationToken cancellationToken = default) =>
            ToActionResult(await _financeQueryService.GetTechnicalIndicatorsAsync(stockCode, cancellationToken));

        /// <summary>Gets chip analysis (籌碼面) indicators for a stock.</summary>
        [HttpGet("chip-analysis/{stockCode}")]
        public async Task<IActionResult> GetChipAnalysis(string stockCode, CancellationToken cancellationToken = default) =>
            ToActionResult(await _financeQueryService.GetChipAnalysisAsync(stockCode, cancellationToken));

        /// <summary>Gets a comprehensive weighted stock score (technical + chip + fundamental).</summary>
        [HttpGet("score/{stockCode}")]
        public async Task<IActionResult> GetStockScore(string stockCode, CancellationToken cancellationToken = default) =>
            ToActionResult(await _financeQueryService.GetStockScoreAsync(stockCode, cancellationToken));

        /// <summary>Comprehensive stock analysis with configurable indicator selection (TW + US).</summary>
        [HttpPost("comprehensive-analysis")]
        public async Task<IActionResult> GetComprehensiveAnalysis(
            [FromBody] StockAnalysisRequest request,
            CancellationToken cancellationToken = default) =>
            ToActionResult(await _financeQueryService.GetComprehensiveAnalysisAsync(request, cancellationToken));

        /// <summary>Gets macro economy overview data (market index, sectors, margin, FX, bank rates).</summary>
        [HttpGet("macro-economy")]
        public async Task<IActionResult> GetMacroEconomy(CancellationToken cancellationToken = default) =>
            ToActionResult(await _financeQueryService.GetMacroEconomyAsync(cancellationToken));

        private IActionResult ToActionResult(ServiceResult result) =>
            result.IsSuccess
                ? _responseFactory.CreateOKResponse(result.Data)
                : _responseFactory.CreateErrorResponse(result.Error!.Value, result.ErrorMessage!);
    }
}
