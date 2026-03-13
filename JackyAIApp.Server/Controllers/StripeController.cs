using JackyAIApp.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JackyAIApp.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class StripeController : ControllerBase
    {
        private readonly IStripeService _stripeService;
        private readonly IUserService _userService;
        private readonly ILogger<StripeController> _logger;

        public StripeController(
            IStripeService stripeService,
            IUserService userService,
            ILogger<StripeController> logger)
        {
            _stripeService = stripeService;
            _userService = userService;
            _logger = logger;
        }

        /// <summary>
        /// Get available credit packs
        /// </summary>
        [HttpGet("packs")]
        public IActionResult GetPacks()
        {
            var packs = StripeService.CreditPacks.Select(p => new
            {
                p.Id,
                p.Name,
                priceInCents = p.PriceInCents,
                p.Credits,
                p.Badge,
            });
            return Ok(packs);
        }

        /// <summary>
        /// Create a Stripe Checkout Session for purchasing credits
        /// </summary>
        [HttpPost("checkout")]
        [Authorize]
        public async Task<IActionResult> CreateCheckout([FromBody] CreateCheckoutRequest request)
        {
            var userId = _userService.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized("User not found");
            }

            try
            {
                var checkoutUrl = await _stripeService.CreateCheckoutSessionAsync(userId, request.PackId);
                return Ok(new { checkoutUrl });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        /// <summary>
        /// Stripe webhook endpoint (called by Stripe, not by frontend)
        /// </summary>
        [HttpPost("webhook")]
        public async Task<IActionResult> Webhook()
        {
            var json = await new StreamReader(HttpContext.Request.Body).ReadToEndAsync();
            var signature = Request.Headers["Stripe-Signature"].FirstOrDefault();

            if (string.IsNullOrEmpty(signature))
            {
                return BadRequest("Missing Stripe-Signature header");
            }

            var success = await _stripeService.HandleWebhookAsync(json, signature);
            return success ? Ok() : BadRequest("Webhook processing failed");
        }
    }

    public class CreateCheckoutRequest
    {
        public string PackId { get; set; } = "";
    }
}
