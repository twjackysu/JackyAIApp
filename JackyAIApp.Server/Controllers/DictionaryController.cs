using JackyAIApp.Server.Common;
using JackyAIApp.Server.Services.Dictionary;
using Microsoft.AspNetCore.Authorization;
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

        // Anonymous lookup is intentional — the dictionary is browsable without login.
        [HttpGet("{word}", Name = "Search word")]
        public async Task<IActionResult> Get(string word)
        {
            var result = await _dictionaryService.GetWordAsync(word);
            return _responseFactory.ToActionResult(result);
        }

        // Anonymous, no OpenAI call — pure Hunspell lookup, safe/cheap to expose publicly.
        [HttpGet("{word}/suggestions", Name = "Suggest spelling")]
        public IActionResult Suggest(string word) => _responseFactory.ToActionResult(_dictionaryService.SuggestWords(word));

        // Mutates shared data and can trigger paid OpenAI regeneration on next lookup,
        // so this one endpoint (unlike Get) requires an authenticated admin.
        [Authorize]
        [AdminOnly]
        [HttpPut("{word}/invalid", Name = "Make a word invalid")]
        public async Task<IActionResult> Invalid(string word)
        {
            var result = await _dictionaryService.InvalidateWordAsync(word);
            return _responseFactory.ToActionResult(result);
        }
    }
}
