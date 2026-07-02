using JackyAIApp.Server.Common;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.Services;
using JackyAIApp.Server.Services.Dictionary;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace JackyAIApp.Server.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DictionaryController(
        IMyResponseFactory responseFactory,
        IDictionaryService dictionaryService,
        AzureSQLDBContext dbContext,
        IUserService userService) : ControllerBase
    {
        private readonly IMyResponseFactory _responseFactory = responseFactory;
        private readonly IDictionaryService _dictionaryService = dictionaryService;
        private readonly AzureSQLDBContext _dbContext = dbContext;
        private readonly IUserService _userService = userService;

        // Anonymous lookup is intentional — the dictionary is browsable without login.
        [HttpGet("{word}", Name = "Search word")]
        public async Task<IActionResult> Get(string word)
        {
            var result = await _dictionaryService.GetWordAsync(word);
            return ToActionResult(result);
        }

        // Mutates shared data and can trigger paid OpenAI regeneration on next lookup,
        // so this one endpoint (unlike Get) requires an authenticated admin.
        [Authorize]
        [HttpPut("{word}/invalid", Name = "Make a word invalid")]
        public async Task<IActionResult> Invalid(string word)
        {
            var userId = _userService.GetUserId();
            var isAdmin = !string.IsNullOrEmpty(userId) &&
                await _dbContext.Users.AnyAsync(u => u.Id == userId && u.IsAdmin == true);
            if (!isAdmin)
            {
                return _responseFactory.CreateErrorResponse(ErrorCodes.Forbidden, "Admin access required.");
            }

            var result = await _dictionaryService.InvalidateWordAsync(word);
            return ToActionResult(result);
        }

        private IActionResult ToActionResult(ServiceResult result) =>
            result.IsSuccess
                ? _responseFactory.CreateOKResponse(result.Data)
                : _responseFactory.CreateErrorResponse(result.Error!.Value, result.ErrorMessage!);
    }
}
