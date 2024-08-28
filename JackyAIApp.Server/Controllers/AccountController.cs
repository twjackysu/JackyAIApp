using JackyAIApp.Server.Common;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.Data.Models;
using JackyAIApp.Server.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JackyAIApp.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AccountController(ILogger<AccountController> logger, AzureCosmosDBContext DBContext, IUserService userService, IMyResponseFactory responseFactory) : ControllerBase
    {
        private readonly ILogger<AccountController> _logger = logger ?? throw new ArgumentNullException();
        private readonly AzureCosmosDBContext _DBContext = DBContext;
        private readonly IUserService _userService = userService;
        private readonly IMyResponseFactory _responseFactory = responseFactory ?? throw new ArgumentNullException();
        [HttpGet("login/{provider}")]
        public IActionResult Login(string provider)
        {
            if (User.Identity?.IsAuthenticated ?? false)
            {
                // The user is already logged in, redirect them to a client-side route.
                return LocalRedirect("~/");
            }

            var redirectUrl = Url.Action(nameof(LoginCallback));
            var properties = new AuthenticationProperties { RedirectUri = redirectUrl };
            return Challenge(properties, provider);
        }

        [HttpGet("login-callback")]
        public async Task<IActionResult> LoginCallback()
        {
            var result = await HttpContext.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            if (!result.Succeeded)
            {
                return Unauthorized();
            }
            var userId = _userService.GetUserId();
            await _DBContext.Database.EnsureCreatedAsync();
            var user = _DBContext.User.SingleOrDefault(x => x.Id == userId);
            if (user == null && userId != null)
            {
                user = new User()
                {
                    Id = userId,
                    PartitionKey = userId,
                    Name = _userService.GetUserName(),
                    Email = _userService.GetUserEmail(),
                    LastUpdated = DateTime.Now,
                    CreditBalance = 20,
                    TotalCreditsUsed = 0,
                    WordIds = []
                };
                _DBContext.User.Add(user);
                await _DBContext.SaveChangesAsync();
            }
            return LocalRedirect("~/");
        }

        [HttpPost("logout")]    
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return NoContent();
        }
        [HttpGet("check-auth")]
        public IActionResult CheckAuth()
        {
            var isAuthenticated = User.Identity?.IsAuthenticated ?? false;
            return Ok(new { IsAuthenticated = isAuthenticated });
        }
        [Authorize]
        [HttpGet("info")]
        public IActionResult GetUserInfo()
        {
            var userId = _userService.GetUserId();
            var user = _DBContext.User.SingleOrDefault(x => x.Id == userId);
            if (user == null)
            {
                return _responseFactory.CreateErrorResponse(ErrorCodes.Forbidden, "User not found.");
            }
            return _responseFactory.CreateOKResponse(user);
        }
    }
}
