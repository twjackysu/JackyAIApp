using JackyAIApp.Server.Common;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.Data.Models.SQL;
using JackyAIApp.Server.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace JackyAIApp.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AccountController(ILogger<AccountController> logger, AzureSQLDBContext DBContext, IUserService userService, IMyResponseFactory responseFactory) : ControllerBase
    {
        private readonly ILogger<AccountController> _logger = logger;
        private readonly AzureSQLDBContext _DBContext = DBContext;
        private readonly IUserService _userService = userService;
        private readonly IMyResponseFactory _responseFactory = responseFactory;

        [HttpGet("login/{provider}")]
        public IActionResult Login(string provider, string? returnUrl = null)
        {
            // Only accept a same-site relative path — Url.IsLocalUrl rejects absolute/protocol-
            // relative URLs, which is what stops this from becoming an open-redirect vector.
            var safeReturnUrl = !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl)
                ? returnUrl
                : "/";

            if (User.Identity?.IsAuthenticated ?? false)
            {
                // The user is already logged in, redirect them to a client-side route.
                return LocalRedirect(safeReturnUrl);
            }

            var redirectUrl = Url.Action(nameof(LoginCallback));
            var properties = new AuthenticationProperties { RedirectUri = redirectUrl };
            // Items survives the OAuth round-trip (persisted in the correlation cookie) — the
            // callback below reads it back out to know where to send the user after sign-in.
            properties.Items["returnUrl"] = safeReturnUrl;
            return Challenge(properties, provider);
        }

        [HttpGet("login-callback")]
        public async Task<IActionResult> LoginCallback()
        {
            var result = await HttpContext.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            if (!result.Succeeded)
            {
                _logger.LogWarning("Login callback failed: {FailureMessage}", result.Failure?.Message);
                // Redirect to a user-friendly error page instead of raw 401
                return LocalRedirect("~/login-error");
            }

            // Re-validate rather than trust the round-tripped value blindly — belt-and-suspenders
            // against a tampered correlation cookie.
            var returnUrl = result.Properties?.Items.TryGetValue("returnUrl", out var stored) == true
                && !string.IsNullOrEmpty(stored) && Url.IsLocalUrl(stored)
                ? stored
                : "/";

            var userId = _userService.GetUserId();
            var user = await _DBContext.Users.SingleOrDefaultAsync(x => x.Id == userId);
            if (user == null && userId != null)
            {
                user = new User()
                {
                    Id = userId,
                    Name = _userService.GetUserName(),
                    Email = _userService.GetUserEmail(),
                    LastUpdated = DateTime.UtcNow,
                    CreditBalance = ICreditService.DefaultInitialCredits,
                    TotalCreditsUsed = 0
                };
                _DBContext.Users.Add(user);

                // Record initial credit transaction
                var initialTransaction = new CreditTransaction
                {
                    UserId = userId,
                    Amount = (long)ICreditService.DefaultInitialCredits,
                    BalanceAfter = ICreditService.DefaultInitialCredits,
                    TransactionType = CreditTransactionType.Initial,
                    Reason = "new_user_bonus",
                    Description = "Welcome bonus for new users",
                    CreatedAt = DateTime.UtcNow
                };
                _DBContext.CreditTransactions.Add(initialTransaction);

                await _DBContext.SaveChangesAsync();
                _logger.LogInformation("New user created: {UserId} with {Credits} initial credits", userId, ICreditService.DefaultInitialCredits);
            }

            return LocalRedirect(returnUrl);
        }

        [Authorize]
        [HttpPost("logout")]
        public async Task<IActionResult> Logout()
        {
            var userId = _userService.GetUserId();
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            _logger.LogInformation("User logged out: {UserId}", userId);
            return NoContent();
        }

        [HttpGet("info")]
        public async Task<IActionResult> GetUserInfo()
        {
            var userId = _userService.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized(new { error = "Not authenticated" });
            }

            var user = await _DBContext.Users
                .SingleOrDefaultAsync(x => x.Id == userId);

            if (user == null)
            {
                return _responseFactory.CreateErrorResponse(ErrorCodes.Forbidden, "User not found.");
            }

            return _responseFactory.CreateOKResponse(user);
        }
    }
}
