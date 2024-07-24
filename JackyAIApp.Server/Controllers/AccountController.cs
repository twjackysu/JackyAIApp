using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using JackyAIApp.Server.Common;
using JackyAIApp.Server.Configuration;
using JackyAIApp.Server.Data;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using OpenAI.Interfaces;
using DotnetSdkUtilities.Factory.ResponseFactory;
using Microsoft.AspNetCore.Http.HttpResults;

namespace JackyAIApp.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AccountController : ControllerBase
    {
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
    }
}
