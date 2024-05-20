using JackyAIApp.Server.Common;
using JackyAIApp.Server.Configuration;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.Data.Models;
using JackyAIApp.Server.Request;
using JackyAIApp.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Cosmos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OpenAI.Interfaces;
using Org.BouncyCastle.Bcpg;
using System.Collections.Generic;

namespace JackyAIApp.Server.Controllers
{
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class RepositoryController(ILogger<RepositoryController> logger, IOptionsMonitor<Settings> settings, IMyResponseFactory responseFactory, AzureCosmosDBContext DBContext, IUserService userService) : ControllerBase
    {
        private readonly ILogger<RepositoryController> _logger = logger ?? throw new ArgumentNullException();
        private readonly IOptionsMonitor<Settings> _settings = settings;
        private readonly IMyResponseFactory _responseFactory = responseFactory ?? throw new ArgumentNullException();
        private readonly AzureCosmosDBContext _DBContext = DBContext;
        private readonly IUserService _userService = userService;
        [HttpGet("word")]
        public async Task<IActionResult> GetWords()
        {
            var userId = _userService.GetUserId();
            var list = await _DBContext.PersonalWord.Where(x => x.UserId == userId).Select(x => x.Id).ToListAsync();
            return responseFactory.CreateOKResponse(await _DBContext.Word.Where(x => list.Contains(x.Id)).ToListAsync());
        }
        [HttpGet("word/{id}")]
        public async Task<IActionResult> GetWord(string id)
        {
            var userId = _userService.GetUserId();
            var personalWord = await _DBContext.PersonalWord.SingleOrDefaultAsync(w => w.Id == id);
            if(personalWord == null)
            {
                return _responseFactory.CreateErrorResponse(ErrorCodes.NotFound);
            }
            return responseFactory.CreateOKResponse(await _DBContext.Word.SingleOrDefaultAsync(x => personalWord.WordId == x.Id));
        }
        [HttpPut("word/{wordId}")]
        public async Task<IActionResult> AddWord(string wordId)
        {
            if (wordId == null)
            {
                return _responseFactory.CreateErrorResponse(ErrorCodes.BadRequest);
            }
            var userId = _userService.GetUserId();
            if(userId == null)
            {
                return _responseFactory.CreateErrorResponse(ErrorCodes.Forbidden);
            }
            _logger.LogInformation("user: {userId}, add personal word: {wordId}", userId, wordId);
            var personalWord = new PersonalWord()
            {
                Id = Guid.NewGuid().ToString(),
                PartitionKey = userId,
                CreationDate = DateTime.Now,
                UserId = userId,
                WordId = wordId,
            };

            await _DBContext.PersonalWord.AddAsync(personalWord);
            await _DBContext.SaveChangesAsync();

            return _responseFactory.CreateOKResponse(personalWord);
        }
    }
}
