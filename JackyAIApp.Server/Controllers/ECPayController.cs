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
            // Return TWD pricing for ECPay
            var packs = new[]
            {
                new { id = "starter",   name = "Starter Pack",    priceTWD = 150,  credits = 500UL,  badge = (string?)"" },
                new { id = "popular",   name = "Popular Pack",    priceTWD = 300,  credits = 1100UL, badge = (string?)"10% Bonus" },
                new { id = "bestvalue", name = "Best Value Pack", priceTWD = 600,  credits = 2400UL, badge = (string?)"20% Bonus" },
            };
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
    }

    public class ECPayCreateRequest { public string PackId { get; set; } = ""; }
}
