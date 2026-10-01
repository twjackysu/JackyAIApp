using JackyAIApp.Server.Common;
using JackyAIApp.Server.Services.Repository;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JackyAIApp.Server.Controllers
{
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class RepositoryController(
        IMyResponseFactory responseFactory,
        IRepositoryService repositoryService) : ControllerBase
    {
        private readonly IMyResponseFactory _responseFactory = responseFactory;
        private readonly IRepositoryService _repositoryService = repositoryService;

        [HttpGet("word")]
        public async Task<IActionResult> GetWords(int pageNumber = 1, int pageSize = 10) =>
            _responseFactory.ToActionResult(await _repositoryService.GetWordsAsync(pageNumber, pageSize));

        [HttpGet("word/{wordId}")]
        public async Task<IActionResult> GetWords(string wordId) =>
            _responseFactory.ToActionResult(await _repositoryService.GetWordAsync(wordId));

        [HttpPut("word/{wordId}")]
        public async Task<IActionResult> AddWord(string wordId) =>
            _responseFactory.ToActionResult(await _repositoryService.AddWordAsync(wordId));

        [HttpDelete("word/{wordId}")]
        public async Task<IActionResult> DeleteWord(string wordId) =>
            _responseFactory.ToActionResult(await _repositoryService.DeleteWordAsync(wordId));

        /// <summary>Lists the built-in starter word packs (metadata only).</summary>
        [HttpGet("packs")]
        public IActionResult GetWordPacks() =>
            _responseFactory.ToActionResult(_repositoryService.GetWordPacks());

        /// <summary>Adds a starter word pack to the current user's repository. Free (no usage charge).</summary>
        [HttpPost("packs/{packId}")]
        public async Task<IActionResult> AddWordPack(string packId) =>
            _responseFactory.ToActionResult(await _repositoryService.AddWordPackAsync(packId));
    }
}
