﻿using JackyAIApp.Server.Common;
using JackyAIApp.Server.Configuration;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.Data.Models;
using JackyAIApp.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace JackyAIApp.Server.Controllers
{
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class RepositoryController(ILogger<RepositoryController> logger, IOptionsMonitor<Settings> settings, IMyResponseFactory responseFactory, AzureCosmosDBContext DBContext, IUserService userService, IMemoryCache memoryCache) : ControllerBase
    {
        private readonly ILogger<RepositoryController> _logger = logger ?? throw new ArgumentNullException();
        private readonly IOptionsMonitor<Settings> _settings = settings;
        private readonly IMyResponseFactory _responseFactory = responseFactory ?? throw new ArgumentNullException();
        private readonly AzureCosmosDBContext _DBContext = DBContext;
        private readonly IUserService _userService = userService;
        private readonly IMemoryCache _memoryCache = memoryCache;
        [HttpGet("word")]
        public async Task<IActionResult> GetWords()
        {
            var userId = _userService.GetUserId();
            var cacheKey = $"GetWords_{userId}";
            if(!_memoryCache.TryGetValue(cacheKey, out List<Word>? result))
            {
                var user = await _DBContext.User.SingleOrDefaultAsync(x => x.Id == userId);
                if (user == null)
                {
                    return _responseFactory.CreateErrorResponse(ErrorCodes.Forbidden, "User not found.");
                }
                var list = user.WordIds;
                result = await _DBContext.Word.Where(x => list.Contains(x.Id)).ToListAsync();
                _memoryCache.Set(cacheKey, result, TimeSpan.FromDays(1));
            }
            return responseFactory.CreateOKResponse(result);
        }
        [HttpPut("word/{wordId}")]
        public async Task<IActionResult> AddWord(string wordId)
        {
            if (string.IsNullOrEmpty(wordId))
            {
                return _responseFactory.CreateErrorResponse(ErrorCodes.BadRequest, "The word ID is invalid.");
            }

            var userId = _userService.GetUserId();
            var cacheKey = $"GetWords_{userId}";

            var user = await _DBContext.User.SingleOrDefaultAsync(x => x.Id == userId);
            if (user == null)
            {
                return _responseFactory.CreateErrorResponse(ErrorCodes.Forbidden, "User not found.");
            }

            if (user.WordIds.Contains(wordId))
            {
                return _responseFactory.CreateErrorResponse(ErrorCodes.BadRequest, "The word has already been added.");
            }

            _logger.LogInformation("User: {userId}, Adding personal word: {wordId}", userId, wordId);

            user.WordIds.Add(wordId);
            user.LastUpdated = DateTime.UtcNow;

            await _DBContext.SaveChangesAsync();

            _memoryCache.Remove(cacheKey);

            return _responseFactory.CreateOKResponse(user);
        }
        [HttpDelete("word/{wordId}")]
        public async Task<IActionResult> DeleteWord(string wordId)
        {
            if (string.IsNullOrEmpty(wordId))
            {
                return _responseFactory.CreateErrorResponse(ErrorCodes.BadRequest, "The word ID is invalid.");
            }

            var userId = _userService.GetUserId();
            var cacheKey = $"GetWords_{userId}";

            var user = await _DBContext.User.SingleOrDefaultAsync(x => x.Id == userId);
            if (user == null)
            {
                return _responseFactory.CreateErrorResponse(ErrorCodes.Forbidden, "User not found.");
            }

            _logger.LogInformation("User: {userId}, Deleting personal word: {wordId}", userId, wordId);

            var isSuccess = user.WordIds.Remove(wordId);
            user.LastUpdated = DateTime.UtcNow;

            if (!isSuccess)
            {
                return _responseFactory.CreateErrorResponse(ErrorCodes.NotFound, "The word ID was not found.");
            }

            await _DBContext.SaveChangesAsync();

            _memoryCache.Remove(cacheKey);

            return _responseFactory.CreateOKResponse();
        }
    }
}
