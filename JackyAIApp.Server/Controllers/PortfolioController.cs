using JackyAIApp.Server.Common;
using JackyAIApp.Server.DTO;
using JackyAIApp.Server.DTO.Portfolio;
using JackyAIApp.Server.Services;
using JackyAIApp.Server.Services.Portfolio;
using JackyAIApp.Server.Services.Portfolio.DrawdownBrake;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JackyAIApp.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class PortfolioController(
        IMyResponseFactory responseFactory,
        IPortfolioService portfolioService,
        INetWorthHistoryService netWorthHistory,
        IDrawdownAnalysisService drawdownAnalysis,
        IBenchmarkComparisonService benchmarkComparison,
        IHoldingSearchService holdingSearch,
        IHoldingPriceService holdingPrices,
        IHoldingAiSummaryService holdingAiSummary,
        ICurrentUser currentUser,
        ServerTiming timing,
        IDrawdownBrakeService drawdownBrakeService,
        IDrawdownBrakeAccess drawdownBrakeAccess,
        ITaiexTotalReturnRefresher drawdownBrakeRefresher,
        DrawdownBrakeTelemetry drawdownBrakeTelemetry) : ControllerBase
    {
        [HttpGet("summary")]
        public async Task<IActionResult> GetSummary(
            [FromQuery] decimal feeDiscountRate = 0.28m, CancellationToken ct = default)
        {
            var userId = currentUser.RequireId();
            var result = await portfolioService.GetPortfolioSummaryAsync(userId, feeDiscountRate, ct);
            // Where the time went, per stage — readable in the browser's network panel (Timing tab).
            if (timing.ToHeaderValue() is { } stages)
                Response.Headers.Append(ServerTiming.HeaderName, stages);
            return responseFactory.CreateOKResponse(result);
        }

        [HttpGet("holdings")]
        public async Task<IActionResult> GetHoldings(CancellationToken ct)
        {
            var userId = currentUser.RequireId();
            var result = await portfolioService.GetLatestHoldingsAsync(userId, ct);
            return responseFactory.CreateOKResponse(result);
        }

        [HttpPut("holdings")]
        public async Task<IActionResult> SaveHoldings([FromBody] SaveHoldingsRequest req, CancellationToken ct)
        {
            var userId = currentUser.RequireId();
            await portfolioService.SaveHoldingsAsync(userId, req, ct);
            return responseFactory.CreateOKResponse(true);
        }

        [HttpPost("holdings/{symbol}/refresh-price")]
        public async Task<IActionResult> RefreshPositionPrice(
            string symbol, [FromQuery] decimal feeDiscountRate = 0.28m, CancellationToken ct = default)
        {
            var userId = currentUser.RequireId();
            var result = await portfolioService.RefreshPositionPriceAsync(userId, symbol, feeDiscountRate, ct);
            return responseFactory.CreateOKResponse(result);
        }

        [HttpGet("holdings/{snapshotId:int}")]
        public async Task<IActionResult> GetSnapshotHoldings(int snapshotId, CancellationToken ct)
        {
            var userId = currentUser.RequireId();
            var result = await portfolioService.GetSnapshotHoldingsAsync(userId, snapshotId, ct);
            if (result == null) return NotFound();
            return responseFactory.CreateOKResponse(result);
        }

        [HttpDelete("holdings/{snapshotId:int}")]
        public async Task<IActionResult> DeleteSnapshot(int snapshotId, CancellationToken ct)
        {
            var userId = currentUser.RequireId();
            var ok = await portfolioService.DeleteSnapshotAsync(userId, snapshotId, ct);
            if (!ok) return NotFound();
            return responseFactory.CreateOKResponse(true);
        }

        [HttpGet("update-history")]
        public async Task<IActionResult> GetUpdateHistory(CancellationToken ct)
        {
            var userId = currentUser.RequireId();
            var result = await portfolioService.GetUpdateHistoryAsync(userId, ct);
            return responseFactory.CreateOKResponse(result);
        }

        [HttpGet("net-worth-history")]
        public async Task<IActionResult> GetNetWorthHistory(
            [FromQuery] int days = 90, [FromQuery] string groupBy = "assetType",
            [FromQuery] DateOnly? from = null, [FromQuery] DateOnly? to = null, CancellationToken ct = default)
        {
            var userId = currentUser.RequireId();
            // Fail-soft on an unrecognized groupBy value (falls back to AssetType) rather than
            // rejecting the request — this is a display toggle, not a validated business input.
            var parsedGroupBy = Enum.TryParse<NetWorthGroupBy>(groupBy, ignoreCase: true, out var g)
                ? g
                : NetWorthGroupBy.AssetType;
            // Either date selects the explicit-window mode; `days` stays for the charts that still
            // offer preset spans.
            var result = from.HasValue || to.HasValue
                ? await netWorthHistory.GetNetWorthHistoryAsync(userId, from, to, parsedGroupBy, ct)
                : await netWorthHistory.GetNetWorthHistoryAsync(userId, days, parsedGroupBy, ct);
            return responseFactory.CreateOKResponse(result);
        }

        [HttpGet("drawdown")]
        public async Task<IActionResult> GetDrawdown([FromQuery] int days = 365, CancellationToken ct = default)
        {
            var userId = currentUser.RequireId();
            var result = await drawdownAnalysis.GetDrawdownAnalysisAsync(userId, days, ct);
            return responseFactory.CreateOKResponse(result);
        }

        /// <summary>
        /// The 回落煞車 market signal (Issue #431). Shared market data — nothing here depends on the
        /// caller's holdings — and free to serve (no AI call), so no usage cost. Limited to the
        /// audience in <c>Features:DrawdownBrake:Audience</c> until the Boss opens it up.
        /// </summary>
        [HttpGet("drawdown-brake")]
        public async Task<IActionResult> GetDrawdownBrake(CancellationToken ct)
        {
            var userId = currentUser.RequireId();
            if (!await drawdownBrakeAccess.IsEnabledForUserAsync(userId, ct))
            {
                drawdownBrakeTelemetry.RecordView(userId, DrawdownBrakeViewOutcome.Forbidden, cacheHit: null);
                return responseFactory.CreateErrorResponse(ErrorCodes.DrawdownBrakeNotEnabled, "This feature is not available.");
            }

            var lookup = await drawdownBrakeService.GetWithCacheStatusAsync(ct);
            drawdownBrakeTelemetry.RecordView(userId, DrawdownBrakeTelemetry.OutcomeOf(lookup.Signal), lookup.CacheHit);
            return responseFactory.CreateOKResponse(lookup.Signal);
        }

        /// <summary>
        /// Asks for the newest index days now rather than at the next hourly check. The cooldown is
        /// site-wide (see <see cref="TaiexTotalReturnRefreshService"/>), so a press right after
        /// anyone else's is <c>Throttled</c> — still HTTP 200, since 429 here means a usage limit —
        /// and carries the latest signal all the same. Same audience as the GET; no AI call, so no
        /// usage cost.
        /// </summary>
        [HttpPost("drawdown-brake/refresh")]
        public async Task<IActionResult> RefreshDrawdownBrake(CancellationToken ct)
        {
            var userId = currentUser.RequireId();
            if (!await drawdownBrakeAccess.IsEnabledForUserAsync(userId, ct))
                return responseFactory.CreateErrorResponse(ErrorCodes.DrawdownBrakeNotEnabled, "This feature is not available.");

            var outcome = await drawdownBrakeRefresher.TryRefreshNowAsync(ct);
            drawdownBrakeTelemetry.RecordRefresh(outcome.Result);
            return responseFactory.CreateOKResponse(new DrawdownBrakeRefreshDto
            {
                Result = outcome.Result,
                RetryAfterSeconds = outcome.RetryAfterSeconds,
                // Rebuilt if the refresh stored anything; otherwise the shared copy. With a newer
                // intraday estimate if one is wanted now and MIS has not been asked in the last
                // 10 seconds — whatever the official refresh answered, and whether or not MIS did.
                Signal = await drawdownBrakeService.GetAfterEstimateRefreshAsync(ct),
            });
        }

        [HttpGet("benchmark-comparison")]
        public async Task<IActionResult> GetBenchmarkComparison(
            [FromQuery] int days = 365, [FromQuery] string[]? symbols = null, CancellationToken ct = default)
        {
            var userId = currentUser.RequireId();
            var result = await benchmarkComparison.GetBenchmarkComparisonAsync(userId, days, symbols, ct);
            return responseFactory.CreateOKResponse(result);
        }

        [HttpGet("lookup/{market}/{symbol}")]
        public async Task<IActionResult> LookupStock(string market, string symbol, CancellationToken ct)
        {
            var result = await holdingPrices.LookupStockAsync(market, symbol, ct);
            if (result == null) return NotFound();
            return responseFactory.CreateOKResponse(result);
        }

        [HttpGet("stocks/search")]
        public async Task<IActionResult> SearchStocks([FromQuery] string market, [FromQuery] string keyword, CancellationToken ct)
        {
            var result = await holdingSearch.SearchStocksAsync(market ?? "TW", keyword ?? "", ct);
            return responseFactory.CreateOKResponse(result);
        }

        [HttpGet("funds/search")]
        public async Task<IActionResult> SearchFunds([FromQuery] string keyword, CancellationToken ct)
        {
            var result = await holdingSearch.SearchFundsAsync(keyword ?? "", ct);
            return responseFactory.CreateOKResponse(result);
        }

        [HttpGet("holdings/{market}/{symbol}/price-history")]
        public async Task<IActionResult> GetHoldingPriceHistory(string market, string symbol, CancellationToken ct)
        {
            var userId = currentUser.RequireId();
            var result = await holdingPrices.GetHoldingPriceHistoryAsync(userId, market, symbol, ct);
            if (result == null) return NotFound();
            return responseFactory.CreateOKResponse(result);
        }

        [HttpGet("holdings/{market}/{symbol}/ai-summary")]
        [UsageLimit(UsageFeature.PortfolioAiSummary)]
        public async Task<IActionResult> GetAiTechSummary(string market, string symbol, CancellationToken ct)
        {
            var userId = currentUser.RequireId();
            return responseFactory.ToActionResult(await holdingAiSummary.GetAiTechSummaryAsync(userId, market, symbol, ct));
        }
    }
}
