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
            ToActionResult(await _repositoryService.GetWordsAsync(pageNumber, pageSize));

        [HttpGet("word/{wordId}")]
        public async Task<IActionResult> GetWords(string wordId) =>
            ToActionResult(await _repositoryService.GetWordAsync(wordId));

        [HttpPut("word/{wordId}")]
        public async Task<IActionResult> AddWord(string wordId) =>
            ToActionResult(await _repositoryService.AddWordAsync(wordId));

        [HttpDelete("word/{wordId}")]
        public async Task<IActionResult> DeleteWord(string wordId) =>
            ToActionResult(await _repositoryService.DeleteWordAsync(wordId));

        private IActionResult ToActionResult(ServiceResult result) =>
            result.IsSuccess
                ? _responseFactory.CreateOKResponse(result.Data)
                : _responseFactory.CreateErrorResponse(result.Error!.Value, result.ErrorMessage!);
    }
}
