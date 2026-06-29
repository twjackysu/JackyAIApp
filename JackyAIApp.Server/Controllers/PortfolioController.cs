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
        IUserService userService) : ControllerBase
    {
        private string RequireUserId()
        {
            var id = userService.GetUserId();
            if (string.IsNullOrEmpty(id)) throw new UnauthorizedAccessException();
            return id;
        }

        [HttpGet("summary")]
        public async Task<IActionResult> GetSummary(CancellationToken ct)
        {
            var userId = RequireUserId();
            var result = await portfolioService.GetPortfolioSummaryAsync(userId, ct);
            return Ok(responseFactory.CreateOKResponse(result));
        }

        [HttpGet("holdings")]
        public async Task<IActionResult> GetHoldings(CancellationToken ct)
        {
            var userId = RequireUserId();
            var result = await portfolioService.GetLatestHoldingsAsync(userId, ct);
            return Ok(responseFactory.CreateOKResponse(result));
        }

        [HttpPut("holdings")]
        public async Task<IActionResult> SaveHoldings([FromBody] SaveHoldingsRequest req, CancellationToken ct)
        {
            var userId = RequireUserId();
            await portfolioService.SaveHoldingsAsync(userId, req, ct);
            return Ok(responseFactory.CreateOKResponse(true));
        }

        [HttpGet("update-history")]
        public async Task<IActionResult> GetUpdateHistory(CancellationToken ct)
        {
            var userId = RequireUserId();
            var result = await portfolioService.GetUpdateHistoryAsync(userId, ct);
            return Ok(responseFactory.CreateOKResponse(result));
        }

        [HttpGet("net-worth-history")]
        public async Task<IActionResult> GetNetWorthHistory([FromQuery] int days = 90, CancellationToken ct = default)
        {
            var userId = RequireUserId();
            var result = await portfolioService.GetNetWorthHistoryAsync(userId, days, ct);
            return Ok(responseFactory.CreateOKResponse(result));
        }

        [HttpGet("lookup/{market}/{symbol}")]
        public async Task<IActionResult> LookupStock(string market, string symbol, CancellationToken ct)
        {
            var result = await portfolioService.LookupStockAsync(market, symbol, ct);
            if (result == null) return NotFound();
            return Ok(responseFactory.CreateOKResponse(result));
        }

        [HttpGet("holdings/{market}/{symbol}/price-history")]
        public async Task<IActionResult> GetHoldingPriceHistory(string market, string symbol, CancellationToken ct)
        {
            var userId = RequireUserId();
            var result = await portfolioService.GetHoldingPriceHistoryAsync(userId, market, symbol, ct);
            if (result == null) return NotFound();
            return Ok(responseFactory.CreateOKResponse(result));
        }
    }
}
