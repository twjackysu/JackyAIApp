using JackyAIApp.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JackyAIApp.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class PayPalController : ControllerBase
    {
        private readonly IPayPalService _payPalService;
        private readonly IUserService _userService;

        public PayPalController(IPayPalService payPalService, IUserService userService)
        {
            _payPalService = payPalService;
            _userService = userService;
        }

        [HttpGet("packs")]
        public IActionResult GetPacks()
        {
            var packs = StripeService.CreditPacks.Select(p => new
            {
                p.Id, p.Name, priceInCents = p.PriceInCents, p.Credits, p.Badge,
            });
            return Ok(packs);
        }

        [HttpPost("create-order")]
        [Authorize]
        public async Task<IActionResult> CreateOrder([FromBody] CreateOrderRequest request)
        {
            var userId = _userService.GetUserId();
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            try
            {
                var approveUrl = await _payPalService.CreateOrderAsync(userId, request.PackId);
                return Ok(new { approveUrl });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpPost("capture-order")]
        [Authorize]
        public async Task<IActionResult> CaptureOrder([FromBody] CaptureOrderRequest request)
        {
            var success = await _payPalService.CaptureOrderAsync(request.OrderId);
            return success ? Ok(new { message = "Credits added" }) : BadRequest("Capture failed");
        }
    }

    public class CreateOrderRequest { public string PackId { get; set; } = ""; }
    public class CaptureOrderRequest { public string OrderId { get; set; } = ""; }
}
