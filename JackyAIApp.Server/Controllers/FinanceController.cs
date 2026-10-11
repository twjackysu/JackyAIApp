using JackyAIApp.Server.Common;
using JackyAIApp.Server.DTO;
using JackyAIApp.Server.DTO.Finance;
using JackyAIApp.Server.Services.Finance;
using JackyAIApp.Server.Services.Finance.Prediction;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JackyAIApp.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class FinanceController(
        IMyResponseFactory responseFactory,
        IFinanceQueryService financeQueryService,
        IServiceScopeFactory serviceScopeFactory,
        ILogger<FinanceController> logger) : ControllerBase
    {
        private readonly IMyResponseFactory _responseFactory = responseFactory;
        private readonly IFinanceQueryService _financeQueryService = financeQueryService;
        private readonly IServiceScopeFactory _serviceScopeFactory = serviceScopeFactory;
        private readonly ILogger<FinanceController> _logger = logger;

        /// <summary>Analyzes a specific stock using TWSE APIs and OpenAI.</summary>
        [Authorize]
        [HttpPost("analyze-stock")]
        [UsageLimit(UsageCosts.StockAnalysis, "stock_analysis")]
        public async Task<IActionResult> AnalyzeStock([FromBody] StockSearchRequest request, CancellationToken cancellationToken = default) =>
            _responseFactory.ToActionResult(await _financeQueryService.AnalyzeStockAsync(request, cancellationToken));

        /// <summary>Gets technical indicators (MA, RSI, MACD, KD, Volume, Bollinger Bands) for a stock.</summary>
        [HttpGet("technical-indicators/{stockCode}")]
        public async Task<IActionResult> GetTechnicalIndicators(string stockCode, CancellationToken cancellationToken = default) =>
            _responseFactory.ToActionResult(await _financeQueryService.GetTechnicalIndicatorsAsync(stockCode, cancellationToken));

        /// <summary>Gets chip analysis (籌碼面) indicators for a stock.</summary>
        [HttpGet("chip-analysis/{stockCode}")]
        public async Task<IActionResult> GetChipAnalysis(string stockCode, CancellationToken cancellationToken = default) =>
            _responseFactory.ToActionResult(await _financeQueryService.GetChipAnalysisAsync(stockCode, cancellationToken));

        /// <summary>Gets a comprehensive weighted stock score (technical + chip + fundamental).</summary>
        [HttpGet("score/{stockCode}")]
        public async Task<IActionResult> GetStockScore(string stockCode, CancellationToken cancellationToken = default) =>
            _responseFactory.ToActionResult(await _financeQueryService.GetStockScoreAsync(stockCode, cancellationToken));

        /// <summary>Comprehensive stock analysis with configurable indicator selection (TW + US).</summary>
        [HttpPost("comprehensive-analysis")]
        public async Task<IActionResult> GetComprehensiveAnalysis(
            [FromBody] StockAnalysisRequest request,
            CancellationToken cancellationToken = default) =>
            _responseFactory.ToActionResult(await _financeQueryService.GetComprehensiveAnalysisAsync(request, cancellationToken));

        /// <summary>Gets macro economy overview data (market index, sectors, margin, FX, bank rates).</summary>
        [HttpGet("macro-economy")]
        public async Task<IActionResult> GetMacroEconomy(CancellationToken cancellationToken = default) =>
            _responseFactory.ToActionResult(await _financeQueryService.GetMacroEconomyAsync(cancellationToken));

        /// <summary>Gets the at-a-glance market dashboard (US/TW indices, FX, crypto, gold, yields).</summary>
        [HttpGet("market-overview")]
        public async Task<IActionResult> GetMarketOverview(CancellationToken cancellationToken = default) =>
            _responseFactory.ToActionResult(await _financeQueryService.GetMarketOverviewAsync(cancellationToken));

        /// <summary>
        /// Gets about six years of daily TAIEX (<c>^TWII</c>, price index, dividends not included)
        /// closes, oldest first. Anonymous like <c>market-overview</c>, shared by every visitor from
        /// one cached copy, no AI and no usage cost. Takes no parameters on purpose — a symbol
        /// argument would turn this into a Yahoo proxy.
        /// </summary>
        [HttpGet("market-index-history")]
        public async Task<IActionResult> GetMarketIndexHistory(CancellationToken cancellationToken = default) =>
            _responseFactory.ToActionResult(await _financeQueryService.GetMarketIndexHistoryAsync(cancellationToken));

        /// <summary>
        /// Gets the AI-synthesised "今日市場AI摘要" daily digest (global macro + Taiwan focus),
        /// grounded in market data, global news, and TW major announcements. Charged
        /// <see cref="UsageCosts.MarketDigest"/> once per user per day (handled in-service so
        /// cached same-day reads are free).
        /// </summary>
        [Authorize]
        [HttpGet("market-digest")]
        public async Task<IActionResult> GetMarketDigest(CancellationToken cancellationToken = default)
        {
            var result = _responseFactory.ToActionResult(await _financeQueryService.GetMarketDigestAsync(cancellationToken));

            // Prediction-accuracy tracking (TASK-013): opportunistically grade a small batch of due
            // predictions, piggybacking on this already-frequent, already-authenticated endpoint
            // instead of a dedicated scheduled job (see ARCHITECTURE.md). Fire-and-forget, started
            // only after the digest response above is already built — must never add latency to,
            // or fail, the response the caller is waiting on. Uses its own DI scope (not the
            // request's) since this keeps running after the request scope is disposed.
            _ = Task.Run(async () =>
            {
                try
                {
                    using var scope = _serviceScopeFactory.CreateScope();
                    var evaluationService = scope.ServiceProvider.GetRequiredService<IPredictionEvaluationService>();
                    await evaluationService.EvaluateDuePredictionsAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Background stock-prediction evaluation failed");
                }
            });

            return result;
        }

        /// <summary>Gets key macro indicators (FRED) with recent history for the Indicator Explorer.</summary>
        [HttpGet("indicators")]
        public async Task<IActionResult> GetIndicators(CancellationToken cancellationToken = default) =>
            _responseFactory.ToActionResult(await _financeQueryService.GetIndicatorsAsync(cancellationToken));

        /// <summary>Gets an AI reading for a single indicator (lazy; charges no usage).</summary>
        [HttpGet("indicators/{id}/insight")]
        public async Task<IActionResult> GetIndicatorInsight(string id, CancellationToken cancellationToken = default) =>
            _responseFactory.ToActionResult(await _financeQueryService.GetIndicatorInsightAsync(id, cancellationToken));
    }
}
