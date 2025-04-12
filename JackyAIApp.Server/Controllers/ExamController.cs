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

        /// <summary>
        /// Generates a cloze test for the user based on their unfamiliar words.
        /// </summary>
        /// <returns>An IActionResult containing the cloze test or an error response.</returns>
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
                Model = Models.Gpt_4o_mini,
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

        /// <summary>
        /// Generates a translation test for the user based on their unfamiliar words.
        /// </summary>
        /// <returns>An IActionResult containing the translation test or an error response.</returns>
        [HttpGet("translation")]
        public async Task<IActionResult> GetTranslationTest()
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
            if (word == null)
            {
                return _responseFactory.CreateErrorResponse(ErrorCodes.Forbidden, "You haven't added any unfamiliar words yet. Please use the favorite icon to add unfamiliar words to the Repository. The exam will generate questions based on the words you're unfamiliar with.");
            }
            if (word.TranslationTests != null && word.TranslationTests.Count > 3)
            {
                // Check if there are enough test questions for the vocabulary; if there are more than three, no additional questions will be generated.
                var randomTestIndex = random.Next(word.TranslationTests.Count);
                var randomTest = word.TranslationTests[randomTestIndex];
                return responseFactory.CreateOKResponse(new TranslationTestResponse() {
                    Word = word.Word,
                    Chinese = randomTest.Chinese,
                    English = randomTest.English,
                });
            }

            string systemChatMessage = System.IO.File.ReadAllText("Prompt/Exam/TranslationSystem.txt");
            var completionResult = await _openAIService.ChatCompletion.CreateCompletion(new ChatCompletionCreateRequest
            {
                Messages =
                [
                    ChatMessage.FromSystem(systemChatMessage),
                    ChatMessage.FromUser("appetizer"),
                    ChatMessage.FromAssistant(JsonConvert.SerializeObject(new TranslationTest()
                    {
                        English = "Soup is often a good choice for an appetizer.",
                        Chinese = "湯經常是開胃菜的好選擇。"
                    })),
                    ChatMessage.FromUser(word.Word)
                ],
                Model = Models.Gpt_4o_mini,
            });
            var errorMessage = "Query failed, OpenAI could not generate the corresponding translation test.";
            if (completionResult.Successful)
            {
                _logger.LogInformation("Generate translation test: {lowerWord}, result: {json}", word.Word, JsonConvert.SerializeObject(completionResult, Formatting.Indented));
                var content = completionResult.Choices.FirstOrDefault()?.Message.Content;
                if (string.IsNullOrEmpty(content))
                {
                    return responseFactory.CreateErrorResponse(ErrorCodes.QueryOpenAIFailed, errorMessage);
                }
                TranslationTest? translationTest = null;
                try
                {
                    translationTest = JsonConvert.DeserializeObject<TranslationTest>(content);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to deserialize the content: {content}", content);
                    return responseFactory.CreateErrorResponse(ErrorCodes.QueryOpenAIFailed, errorMessage);
                }
                if (translationTest == null)
                {
                    return responseFactory.CreateErrorResponse(ErrorCodes.QueryOpenAIFailed, errorMessage);
                }
                if (word.TranslationTests == null || !word.TranslationTests.Any(x => x.Chinese == translationTest.Chinese))
                {
                    word.TranslationTests ??= [];
                    word.TranslationTests.Add(translationTest);
                    await _DBContext.SaveChangesAsync();
                    _logger.LogInformation("translationTest: {translationTestJson} added to DB.", JsonConvert.SerializeObject(translationTest));
                }
                return responseFactory.CreateOKResponse(new TranslationTestResponse()
                {
                    Word = word.Word,
                    Chinese = translationTest.Chinese,
                    English = translationTest.English,
                });
            }
            return responseFactory.CreateErrorResponse(ErrorCodes.OpenAIResponseUnsuccessful, errorMessage);
        }
        /// <summary>
        /// Generates a translation quality grading.
        /// </summary>
        /// <returns>An IActionResult containing the translation quality grading or an error response.</returns>
        [HttpPost("translation/quality_grading")]
        public async Task<IActionResult> GetTranslationQualityGrading([FromBody]TranslationTestUserResponse userResponse)
        {
            if(userResponse == null || string.IsNullOrEmpty(userResponse.Translation) || string.IsNullOrEmpty(userResponse.Translation) || string.IsNullOrEmpty(userResponse.Translation))
            {
                return _responseFactory.CreateErrorResponse(ErrorCodes.BadRequest, "input cannot be null or empty.");
            }

            string systemChatMessage = System.IO.File.ReadAllText("Prompt/Exam/TranslationQualityGradingSystem.txt");
            var completionResult = await _openAIService.ChatCompletion.CreateCompletion(new ChatCompletionCreateRequest
            {
                Messages =
                [
                    ChatMessage.FromSystem(systemChatMessage),
                    ChatMessage.FromUser(JsonConvert.SerializeObject(new TranslationTestUserResponse()
                    {
                        UnfamiliarWords = "attentively",
                        ExaminationQuestion = "他專注地閱讀著一本有趣的書。",
                        Translation = "He was reading an interesting book attentively."
                    })),
                    ChatMessage.FromAssistant(JsonConvert.SerializeObject(new TranslationQualityGradingAssistantResponse()
                    {
                        TranslationQualityGrading = "A級 原因：這個翻譯完整保留了原文的所有語意，語法和拼寫完全正確，語句流暢且符合英文的表達習慣。"
                    })),
                    ChatMessage.FromUser(JsonConvert.SerializeObject(userResponse))
                ],
                Model = Models.Gpt_4o_mini,
            });
            var errorMessage = "Query failed, OpenAI could not generate the corresponding quality grading.";
            if (completionResult.Successful)
            {
                _logger.LogInformation("Generate translation quality grading, userResponse: {lowerWord}, result: {json}", JsonConvert.SerializeObject(userResponse, Formatting.Indented), JsonConvert.SerializeObject(completionResult, Formatting.Indented));
                var content = completionResult.Choices.FirstOrDefault()?.Message.Content;
                if (string.IsNullOrEmpty(content))
                {
                    return responseFactory.CreateErrorResponse(ErrorCodes.QueryOpenAIFailed, errorMessage);
                }
                TranslationQualityGradingAssistantResponse? translationQualityGrading = null;
                try
                {
                    translationQualityGrading = JsonConvert.DeserializeObject<TranslationQualityGradingAssistantResponse>(content);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to deserialize the content: {content}", content);
                    return responseFactory.CreateErrorResponse(ErrorCodes.QueryOpenAIFailed, errorMessage);
                }
                if (translationQualityGrading == null)
                {
                    return responseFactory.CreateErrorResponse(ErrorCodes.QueryOpenAIFailed, errorMessage);
                }
                return responseFactory.CreateOKResponse(translationQualityGrading);
            }
            return responseFactory.CreateErrorResponse(ErrorCodes.OpenAIResponseUnsuccessful, errorMessage);
        }
    }
}
