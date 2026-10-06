using JackyAIApp.Server.Common;
using JackyAIApp.Server.DTO;
using JackyAIApp.Server.DTO.Portfolio;
using JackyAIApp.Server.Services;
using JackyAIApp.Server.Services.Portfolio;
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
        IUserService userService,
        ServerTiming timing) : ControllerBase
    {
        private string RequireUserId()
        {
            var id = userService.GetUserId();
            if (string.IsNullOrEmpty(id)) throw new UnauthorizedAccessException();
            return id;
        }

        [HttpGet("summary")]
        public async Task<IActionResult> GetSummary(
            [FromQuery] decimal feeDiscountRate = 0.28m, CancellationToken ct = default)
        {
            var userId = RequireUserId();
            var result = await portfolioService.GetPortfolioSummaryAsync(userId, feeDiscountRate, ct);
            // Where the time went, per stage — readable in the browser's network panel (Timing tab).
            if (timing.ToHeaderValue() is { } stages)
                Response.Headers.Append(ServerTiming.HeaderName, stages);
            return responseFactory.CreateOKResponse(result);
        }

        [HttpGet("holdings")]
        public async Task<IActionResult> GetHoldings(CancellationToken ct)
        {
            var userId = RequireUserId();
            var result = await portfolioService.GetLatestHoldingsAsync(userId, ct);
            return responseFactory.CreateOKResponse(result);
        }

        [HttpPut("holdings")]
        public async Task<IActionResult> SaveHoldings([FromBody] SaveHoldingsRequest req, CancellationToken ct)
        {
            var userId = RequireUserId();
            await portfolioService.SaveHoldingsAsync(userId, req, ct);
            return responseFactory.CreateOKResponse(true);
        }

        [HttpPost("holdings/{symbol}/refresh-price")]
        public async Task<IActionResult> RefreshPositionPrice(
            string symbol, [FromQuery] decimal feeDiscountRate = 0.28m, CancellationToken ct = default)
        {
            var userId = RequireUserId();
            var result = await portfolioService.RefreshPositionPriceAsync(userId, symbol, feeDiscountRate, ct);
            return responseFactory.CreateOKResponse(result);
        }

        [HttpGet("holdings/{snapshotId:int}")]
        public async Task<IActionResult> GetSnapshotHoldings(int snapshotId, CancellationToken ct)
        {
            var userId = RequireUserId();
            var result = await portfolioService.GetSnapshotHoldingsAsync(userId, snapshotId, ct);
            if (result == null) return NotFound();
            return responseFactory.CreateOKResponse(result);
        }

        [HttpDelete("holdings/{snapshotId:int}")]
        public async Task<IActionResult> DeleteSnapshot(int snapshotId, CancellationToken ct)
        {
            var userId = RequireUserId();
            var ok = await portfolioService.DeleteSnapshotAsync(userId, snapshotId, ct);
            if (!ok) return NotFound();
            return responseFactory.CreateOKResponse(true);
        }

        [HttpGet("update-history")]
        public async Task<IActionResult> GetUpdateHistory(CancellationToken ct)
        {
            var userId = RequireUserId();
            var result = await portfolioService.GetUpdateHistoryAsync(userId, ct);
            return responseFactory.CreateOKResponse(result);
        }

        [HttpGet("net-worth-history")]
        public async Task<IActionResult> GetNetWorthHistory(
            [FromQuery] int days = 90, [FromQuery] string groupBy = "assetType",
            [FromQuery] DateOnly? from = null, [FromQuery] DateOnly? to = null, CancellationToken ct = default)
        {
            var userId = RequireUserId();
            // Fail-soft on an unrecognized groupBy value (falls back to AssetType) rather than
            // rejecting the request — this is a display toggle, not a validated business input.
            var parsedGroupBy = Enum.TryParse<NetWorthGroupBy>(groupBy, ignoreCase: true, out var g)
                ? g
                : NetWorthGroupBy.AssetType;
            // Either date selects the explicit-window mode; `days` stays for the charts that still
            // offer preset spans.
            var result = from.HasValue || to.HasValue
                ? await portfolioService.GetNetWorthHistoryAsync(userId, from, to, parsedGroupBy, ct)
                : await portfolioService.GetNetWorthHistoryAsync(userId, days, parsedGroupBy, ct);
            return responseFactory.CreateOKResponse(result);
        }

        [HttpGet("drawdown")]
        public async Task<IActionResult> GetDrawdown([FromQuery] int days = 365, CancellationToken ct = default)
        {
            var userId = RequireUserId();
            var result = await portfolioService.GetDrawdownAnalysisAsync(userId, days, ct);
            return responseFactory.CreateOKResponse(result);
        }

        [HttpGet("benchmark-comparison")]
        public async Task<IActionResult> GetBenchmarkComparison(
            [FromQuery] int days = 365, [FromQuery] string[]? symbols = null, CancellationToken ct = default)
        {
            var userId = RequireUserId();
            var result = await portfolioService.GetBenchmarkComparisonAsync(userId, days, symbols, ct);
            return responseFactory.CreateOKResponse(result);
        }

        [HttpGet("lookup/{market}/{symbol}")]
        public async Task<IActionResult> LookupStock(string market, string symbol, CancellationToken ct)
        {
            var result = await portfolioService.LookupStockAsync(market, symbol, ct);
            if (result == null) return NotFound();
            return responseFactory.CreateOKResponse(result);
        }

        [HttpGet("stocks/search")]
        public async Task<IActionResult> SearchStocks([FromQuery] string market, [FromQuery] string keyword, CancellationToken ct)
        {
            var result = await portfolioService.SearchStocksAsync(market ?? "TW", keyword ?? "", ct);
            return responseFactory.CreateOKResponse(result);
        }

        [HttpGet("funds/search")]
        public async Task<IActionResult> SearchFunds([FromQuery] string keyword, CancellationToken ct)
        {
            var result = await portfolioService.SearchFundsAsync(keyword ?? "", ct);
            return responseFactory.CreateOKResponse(result);
        }

        [HttpGet("holdings/{market}/{symbol}/price-history")]
        public async Task<IActionResult> GetHoldingPriceHistory(string market, string symbol, CancellationToken ct)
        {
            var userId = RequireUserId();
            var result = await portfolioService.GetHoldingPriceHistoryAsync(userId, market, symbol, ct);
            if (result == null) return NotFound();
            return responseFactory.CreateOKResponse(result);
        }

        [HttpGet("holdings/{market}/{symbol}/ai-summary")]
        public async Task<IActionResult> GetAiTechSummary(string market, string symbol, CancellationToken ct)
        {
            var userId = RequireUserId();
            var (result, error, errorCode) = await portfolioService.GetAiTechSummaryAsync(userId, market, symbol, ct);
            if (result == null)
                return responseFactory.CreateErrorResponse(errorCode ?? ErrorCodes.InternalServerError, error ?? "AI 總結失敗");
            return responseFactory.CreateOKResponse(result);
        }
    }
}
