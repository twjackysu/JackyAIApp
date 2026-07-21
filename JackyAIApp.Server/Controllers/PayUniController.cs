using JackyAIApp.Server.Configuration;
using JackyAIApp.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace JackyAIApp.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class PayUniController : ControllerBase
    {
        private readonly IPayUniService _payUniService;
        private readonly IUserService _userService;
        private readonly PayUniOptions _options;

        public PayUniController(IPayUniService payUniService, IUserService userService, IOptions<PayUniOptions> options)
        {
            _payUniService = payUniService;
            _userService = userService;
            _options = options.Value;
        }

        [HttpPost("subscription/create")]
        [Authorize]
        public async Task<IActionResult> CreateSubscription()
        {
            var userId = _userService.GetUserId();
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var formData = await _payUniService.CreateSubscriptionPaymentAsync(userId);
            return Ok(formData);
        }

        /// <summary>PayUni's NotifyURL — authoritative server-to-server payment result. PayUni's
        /// own reference plugin doesn't expect any particular ack body, just a 200 response.</summary>
        [HttpPost("subscription/notify")]
        public async Task<IActionResult> SubscriptionNotify()
        {
            await _payUniService.HandleNotifyCallbackAsync(Request.Form);
            return Ok();
        }

        /// <summary>PayUni's ReturnURL — the browser is redirected here (POST) after checkout;
        /// verify+grant then bounce to the credits page, matching ECPay's ClientBackURL UX.</summary>
        [HttpPost("subscription/return")]
        public async Task<IActionResult> SubscriptionReturn()
        {
            var success = await _payUniService.HandleReturnCallbackAsync(Request.Form);
            var status = success ? "success" : "failed";
            return Redirect($"{_options.AppBaseUrl}/credits?status={status}&provider=payuni&type=subscription");
        }

        [HttpPost("subscription/cancel")]
        [Authorize]
        public async Task<IActionResult> CancelSubscription()
        {
            var userId = _userService.GetUserId();
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var success = await _payUniService.CancelSubscriptionAsync(userId);
            if (!success) return BadRequest("Cancellation failed.");
            return Ok(new { success = true });
        }

        [HttpGet("subscription/status")]
        [Authorize]
        public async Task<IActionResult> GetSubscriptionStatus()
        {
            var userId = _userService.GetUserId();
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var status = await _payUniService.GetSubscriptionStatusAsync(userId);
            return Ok(status);
        }
    }
}
