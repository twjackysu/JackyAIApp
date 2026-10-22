using JackyAIApp.Server.Common;
using JackyAIApp.Server.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JackyAIApp.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AccountController(ILogger<AccountController> logger, IUserAccountService accounts, ICurrentUser currentUser, IMyResponseFactory responseFactory) : ControllerBase
    {
        private readonly ILogger<AccountController> _logger = logger;
        private readonly IUserAccountService _accounts = accounts;
        private readonly ICurrentUser _currentUser = currentUser;
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

            await _accounts.EnsureCreatedAsync(HttpContext.RequestAborted);

            return LocalRedirect(returnUrl);
        }

        [Authorize]
        [HttpPost("logout")]
        public async Task<IActionResult> Logout()
        {
            var userId = _currentUser.Id;
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            _logger.LogInformation("User logged out: {UserId}", userId);
            return NoContent();
        }

        [HttpGet("info")]
        public async Task<IActionResult> GetUserInfo(CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(_currentUser.Id))
            {
                return Unauthorized(new { error = "Not authenticated" });
            }

            return _responseFactory.ToActionResult(await _accounts.GetInfoAsync(ct));
        }
    }
}
