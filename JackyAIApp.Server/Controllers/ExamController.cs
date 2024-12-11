using Betalgo.Ranul.OpenAI.Interfaces;
using Betalgo.Ranul.OpenAI.ObjectModels;
using Betalgo.Ranul.OpenAI.ObjectModels.RequestModels;
using DotnetSdkUtilities.Services;
using JackyAIApp.Server.Common;
using JackyAIApp.Server.Configuration;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.Data.Models;
using JackyAIApp.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace JackyAIApp.Server.Controllers
{
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class ExamController(ILogger<ExamController> logger, IOptionsMonitor<Settings> settings, IMyResponseFactory responseFactory, AzureCosmosDBContext DBContext, IUserService userService, IExtendedMemoryCache memoryCache, IOpenAIService openAIService) : ControllerBase
    {
        private readonly ILogger<ExamController> _logger = logger ?? throw new ArgumentNullException();
        private readonly IOptionsMonitor<Settings> _settings = settings;
        private readonly IMyResponseFactory _responseFactory = responseFactory ?? throw new ArgumentNullException();
        private readonly AzureCosmosDBContext _DBContext = DBContext;
        private readonly IUserService _userService = userService;
        private readonly IExtendedMemoryCache _memoryCache = memoryCache;
        private readonly IOpenAIService _openAIService = openAIService;

        [HttpGet("cloze")]
        public async Task<IActionResult> GetClozeTest()
        {
            var userId = _userService.GetUserId();
            var user = await _DBContext.User.SingleOrDefaultAsync(x => x.Id == userId);
            if (user == null)
            {
                return _responseFactory.CreateErrorResponse(ErrorCodes.Forbidden, "User not found.");
            }
            var list = user.WordIds;
            var random = new Random();
            var randomIndex = random.Next(user.WordIds.Count);

            string randomWordId = user.WordIds[randomIndex];

            var word = await _DBContext.Word.SingleOrDefaultAsync(x => x.Id == randomWordId);
            if(word == null)
            {
                return _responseFactory.CreateErrorResponse(ErrorCodes.Forbidden, "You haven't added any unfamiliar words yet. Please use the favorite icon to add unfamiliar words to the Repository. The exam will generate questions based on the words you're unfamiliar with.");
            }
            if (word.ClozeTests != null && word.ClozeTests.Count > 3)
            {
                // Check if there are enough test questions for the vocabulary; if there are more than three, no additional questions will be generated.
                var randomTestIndex = random.Next(word.ClozeTests.Count);
                var randomTest = word.ClozeTests[randomTestIndex];
                return responseFactory.CreateOKResponse(randomTest);
            }

            string systemChatMessage = System.IO.File.ReadAllText("Prompt/Exam/ClozeSystem.txt");
            var completionResult = await _openAIService.ChatCompletion.CreateCompletion(new ChatCompletionCreateRequest
            {
                Messages =
                [
                    ChatMessage.FromSystem(systemChatMessage),
                    ChatMessage.FromUser("coast"),
                    ChatMessage.FromAssistant(JsonConvert.SerializeObject(new ClozeTest()
                    {
                        Question = "The lighthouse was built on the __________ to help guide ships safely to shore.",
                        Options = ["coast", "cost", "cast", "post"],
                        Answer = "coast"
                    })),
                    ChatMessage.FromUser(word.Word)
                ],
                Model = Models.Gpt_4_turbo,
            });
            var errorMessage = "Query failed, OpenAI could not generate the corresponding cloze test.";
            if (completionResult.Successful)
            {
                _logger.LogInformation("Generate cloze test: {lowerWord}, result: {json}", word.Word, JsonConvert.SerializeObject(completionResult, Formatting.Indented));
                var content = completionResult.Choices.FirstOrDefault()?.Message.Content;
                if (string.IsNullOrEmpty(content))
                {
                    return responseFactory.CreateErrorResponse(ErrorCodes.QueryOpenAIFailed, errorMessage);
                }
                ClozeTest? clozeTest = null;
                try
                {
                    clozeTest = JsonConvert.DeserializeObject<ClozeTest>(content);
                    // Randomize the order of options
                    if (clozeTest != null && clozeTest.Options != null)
                    {
                        clozeTest.Options = [.. clozeTest.Options.OrderBy(x => random.NextDouble())];
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to deserialize the content: {content}", content);
                    return responseFactory.CreateErrorResponse(ErrorCodes.QueryOpenAIFailed, errorMessage);
                }
                if (clozeTest == null)
                {
                    return responseFactory.CreateErrorResponse(ErrorCodes.QueryOpenAIFailed, errorMessage);
                }
                if(word.ClozeTests == null || !word.ClozeTests.Any(x => x.Question == clozeTest.Question))
                {
                    word.ClozeTests ??= [];
                    word.ClozeTests.Add(clozeTest);
                    await _DBContext.SaveChangesAsync();
                    _logger.LogInformation("clozeTest: {clozeTestJson} added to DB.", JsonConvert.SerializeObject(clozeTest));
                }
                return responseFactory.CreateOKResponse(clozeTest);
            }
            return responseFactory.CreateErrorResponse(ErrorCodes.OpenAIResponseUnsuccessful, errorMessage);
        }
    }
}
