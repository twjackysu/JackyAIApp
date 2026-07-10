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
    }

    public class ECPayCreateRequest { public string PackId { get; set; } = ""; }
}
