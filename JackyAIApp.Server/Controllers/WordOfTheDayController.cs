using JackyAIApp.Server.Common;
using JackyAIApp.Server.Services.WordOfTheDay;
using Microsoft.AspNetCore.Mvc;

namespace JackyAIApp.Server.Controllers
{
    [ApiController]
    [Route("api/dictionary/[controller]")]
    public class WordOfTheDayController(
        IMyResponseFactory responseFactory,
        IWordOfTheDayService wordOfTheDayService) : ControllerBase
    {
        private readonly IMyResponseFactory _responseFactory = responseFactory;
        private readonly IWordOfTheDayService _wordOfTheDayService = wordOfTheDayService;

        [HttpGet(Name = "Get word of the day")]
        public async Task<IActionResult> Get()
        {
            var result = await _wordOfTheDayService.GetWordOfTheDayAsync();
            return result.IsSuccess
                ? _responseFactory.CreateOKResponse(result.Data)
                : _responseFactory.CreateErrorResponse(result.Error!.Value, result.ErrorMessage!);
        }
    }
}
