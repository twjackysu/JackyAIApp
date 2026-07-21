using JackyAIApp.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JackyAIApp.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ECPayController : ControllerBase
    {
        private readonly IECPayService _ecPayService;
        private readonly IUserService _userService;

        public ECPayController(IECPayService ecPayService, IUserService userService)
        {
            _ecPayService = ecPayService;
            _userService = userService;
        }

        [HttpGet("packs")]
        public IActionResult GetPacks()
        {
            var packs = CreditPacks.All.Select(p => new
            {
                id = p.Id,
                name = p.Name,
                priceTWD = p.PriceTWD,
                credits = p.Credits,
                badge = p.Badge,
            });
            return Ok(packs);
        }

        [HttpPost("create-payment")]
        [Authorize]
        public async Task<IActionResult> CreatePayment([FromBody] ECPayCreateRequest request)
        {
            var userId = _userService.GetUserId();
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            try
            {
                var formData = await _ecPayService.CreatePaymentAsync(userId, request.PackId);
                return Ok(formData);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        /// <summary>
        /// ECPay server-side callback (POST form data from ECPay)
        /// </summary>
        [HttpPost("callback")]
        public async Task<IActionResult> Callback()
        {
            var success = await _ecPayService.HandleCallbackAsync(Request.Form);
            // ECPay expects "1|OK" on success
            return Content(success ? "1|OK" : "0|Fail");
        }

        // ── Subscription (信用卡定期定額) ──────────────────────────────────────

        [HttpPost("subscription/create")]
        [Authorize]
        public async Task<IActionResult> CreateSubscription()
        {
            var userId = _userService.GetUserId();
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var formData = await _ecPayService.CreateSubscriptionPaymentAsync(userId);
            return Ok(formData);
        }

        /// <summary>ECPay ReturnURL — first charge of a new subscription.</summary>
        [HttpPost("subscription/callback")]
        public async Task<IActionResult> SubscriptionCallback()
        {
            var success = await _ecPayService.HandleSubscriptionFirstChargeCallbackAsync(Request.Form);
            return Content(success ? "1|OK" : "0|Fail");
        }

        /// <summary>ECPay PeriodReturnURL — every charge from the 2nd period onward.</summary>
        [HttpPost("subscription/period-callback")]
        public async Task<IActionResult> SubscriptionPeriodCallback()
        {
            var success = await _ecPayService.HandleSubscriptionPeriodCallbackAsync(Request.Form);
            return Content(success ? "1|OK" : "0|Fail");
        }

        [HttpPost("subscription/cancel")]
        [Authorize]
        public async Task<IActionResult> CancelSubscription()
        {
            var userId = _userService.GetUserId();
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var success = await _ecPayService.CancelSubscriptionAsync(userId);
            if (!success) return BadRequest("Cancellation failed.");
            return Ok(new { success = true });
        }

        [HttpGet("subscription/status")]
        [Authorize]
        public async Task<IActionResult> GetSubscriptionStatus()
        {
            var userId = _userService.GetUserId();
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var status = await _ecPayService.GetSubscriptionStatusAsync(userId);
            return Ok(status);
        }
    }

    public class ECPayCreateRequest { public string PackId { get; set; } = ""; }
}
