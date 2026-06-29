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

        [HttpGet("transactions")]
        public async Task<IActionResult> GetTransactions(CancellationToken ct)
        {
            var userId = RequireUserId();
            var result = await portfolioService.GetTransactionsAsync(userId, ct);
            return Ok(responseFactory.CreateOKResponse(result));
        }

        [HttpPost("transactions")]
        public async Task<IActionResult> AddTransaction([FromBody] AddTransactionRequest req, CancellationToken ct)
        {
            var userId = RequireUserId();
            var result = await portfolioService.AddTransactionAsync(userId, req, ct);
            return Ok(responseFactory.CreateOKResponse(result));
        }

        [HttpDelete("transactions/{id:int}")]
        public async Task<IActionResult> DeleteTransaction(int id, CancellationToken ct)
        {
            var userId = RequireUserId();
            await portfolioService.DeleteTransactionAsync(userId, id, ct);
            return Ok(responseFactory.CreateOKResponse(true));
        }

        [HttpGet("net-worth-history")]
        public async Task<IActionResult> GetNetWorthHistory([FromQuery] int days = 90, CancellationToken ct = default)
        {
            var userId = RequireUserId();
            var result = await portfolioService.GetNetWorthHistoryAsync(userId, days, ct);
            return Ok(responseFactory.CreateOKResponse(result));
        }
    }
}
