using JackyAIApp.Server.Common;
using JackyAIApp.Server.Services.Dictionary;
using Microsoft.AspNetCore.Mvc;

namespace JackyAIApp.Server.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DictionaryController(
        IMyResponseFactory responseFactory,
        IDictionaryService dictionaryService) : ControllerBase
    {
        private readonly IMyResponseFactory _responseFactory = responseFactory;
        private readonly IDictionaryService _dictionaryService = dictionaryService;

        [HttpGet("{word}", Name = "Search word")]
        public async Task<IActionResult> Get(string word)
        {
            var result = await _dictionaryService.GetWordAsync(word);
            return ToActionResult(result);
        }

        [HttpPut("{word}/invalid", Name = "Make a word invalid")]
        public async Task<IActionResult> Invalid(string word)
        {
            var result = await _dictionaryService.InvalidateWordAsync(word);
            return ToActionResult(result);
        }

        private IActionResult ToActionResult(DictionaryResult result) =>
            result.IsSuccess
                ? _responseFactory.CreateOKResponse(result.Word!)
                : _responseFactory.CreateErrorResponse(result.Error!.Value, result.ErrorMessage!);
    }
}
