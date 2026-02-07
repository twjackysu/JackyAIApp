using DotnetSdkUtilities.Factory.ResponseFactory;
using JackyAIApp.Server.Common;
using JackyAIApp.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JackyAIApp.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class CreditController : ControllerBase
    {
        private readonly ICreditService _creditService;
        private readonly IUserService _userService;
        private readonly IMyResponseFactory _responseFactory;
        private readonly ILogger<CreditController> _logger;

        public CreditController(
            ICreditService creditService,
            IUserService userService,
            IMyResponseFactory responseFactory,
            ILogger<CreditController> logger)
        {
            _creditService = creditService;
            _userService = userService;
            _responseFactory = responseFactory;
            _logger = logger;
        }

        /// <summary>
        /// Get current credit balance
        /// </summary>
        [HttpGet("balance")]
        public async Task<IActionResult> GetBalance()
        {
            var userId = _userService.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return _responseFactory.CreateErrorResponse(ErrorCodes.Forbidden, "User not found.");
            }

            var balance = await _creditService.GetBalanceAsync(userId);
            return _responseFactory.CreateOKResponse(new { balance });
        }

        /// <summary>
        /// Get credit transaction history
        /// </summary>
        [HttpGet("history")]
        public async Task<IActionResult> GetHistory([FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 20)
        {
            var userId = _userService.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return _responseFactory.CreateErrorResponse(ErrorCodes.Forbidden, "User not found.");
            }

            if (pageNumber < 1) pageNumber = 1;
            if (pageSize < 1) pageSize = 20;
            if (pageSize > 100) pageSize = 100;

            var transactions = await _creditService.GetTransactionHistoryAsync(userId, pageNumber, pageSize);
            var totalCount = await _creditService.GetTransactionCountAsync(userId);

            return _responseFactory.CreateOKResponse(new
            {
                transactions = transactions.Select(t => new
                {
                    t.Id,
                    t.Amount,
                    t.BalanceAfter,
                    t.TransactionType,
                    t.Reason,
                    t.Description,
                    t.CreatedAt
                }),
                pagination = new
                {
                    pageNumber,
                    pageSize,
                    totalCount,
                    totalPages = (int)Math.Ceiling((double)totalCount / pageSize)
                }
            });
        }

        /// <summary>
        /// Check if user has sufficient credits for an action
        /// </summary>
        [HttpGet("check")]
        public async Task<IActionResult> CheckCredits([FromQuery] ulong required)
        {
            var userId = _userService.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return _responseFactory.CreateErrorResponse(ErrorCodes.Forbidden, "User not found.");
            }

            var hasSufficient = await _creditService.HasSufficientCreditsAsync(userId, required);
            var balance = await _creditService.GetBalanceAsync(userId);

            return _responseFactory.CreateOKResponse(new
            {
                hasSufficient,
                balance,
                required
            });
        }
    }
}
