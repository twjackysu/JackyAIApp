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

        /// <summary>Gets the at-a-glance market dashboard (US/TW indices, FX, crypto, gold, yields).</summary>
        [HttpGet("market-overview")]
        public async Task<IActionResult> GetMarketOverview(CancellationToken cancellationToken = default) =>
            ToActionResult(await _financeQueryService.GetMarketOverviewAsync(cancellationToken));

        /// <summary>Gets the AI-synthesised "今日市場重點" brief (a few takeaways from the day's market data).</summary>
        [HttpGet("market-brief")]
        public async Task<IActionResult> GetMarketBrief(CancellationToken cancellationToken = default) =>
            ToActionResult(await _financeQueryService.GetMarketBriefAsync(cancellationToken));

        /// <summary>Gets key macro indicators (FRED) with recent history for the Indicator Explorer.</summary>
        [HttpGet("indicators")]
        public async Task<IActionResult> GetIndicators(CancellationToken cancellationToken = default) =>
            ToActionResult(await _financeQueryService.GetIndicatorsAsync(cancellationToken));

        /// <summary>Gets an AI reading for a single indicator (lazy; charges no credits).</summary>
        [HttpGet("indicators/{id}/insight")]
        public async Task<IActionResult> GetIndicatorInsight(string id, CancellationToken cancellationToken = default) =>
            ToActionResult(await _financeQueryService.GetIndicatorInsightAsync(id, cancellationToken));

        private IActionResult ToActionResult(ServiceResult result) =>
            result.IsSuccess
                ? _responseFactory.CreateOKResponse(result.Data)
                : _responseFactory.CreateErrorResponse(result.Error!.Value, result.ErrorMessage!);
    }
}
