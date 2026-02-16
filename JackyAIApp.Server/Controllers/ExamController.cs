using Betalgo.Ranul.OpenAI.Interfaces;
using Betalgo.Ranul.OpenAI.ObjectModels;
using Betalgo.Ranul.OpenAI.ObjectModels.RequestModels;
using DotnetSdkUtilities.Services;
using JackyAIApp.Server.Common;
using JackyAIApp.Server.Configuration;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.DTO;
using JackyAIApp.Server.Services;
using JackyAIApp.Server.Services.OpenAI;
using JackyAIApp.Server.Services.Prompt;
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
    public class ExamController : ControllerBase
    {
        private readonly ILogger<ExamController> _logger;
        private readonly IMyResponseFactory _responseFactory;
        private readonly AzureSQLDBContext _DBContext;
        private readonly IUserService _userService;
        private readonly IOpenAIService _openAIService;
        private readonly IOpenAIPromptService _promptService;
        private readonly IPromptLoader _promptLoader;

        public ExamController(
            ILogger<ExamController> logger,
            IMyResponseFactory responseFactory,
            AzureSQLDBContext DBContext,
            IUserService userService,
            IOpenAIService openAIService,
            IOpenAIPromptService promptService,
            IPromptLoader promptLoader)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _responseFactory = responseFactory ?? throw new ArgumentNullException(nameof(responseFactory));
            _DBContext = DBContext ?? throw new ArgumentNullException(nameof(DBContext));
            _userService = userService ?? throw new ArgumentNullException(nameof(userService));
            _openAIService = openAIService ?? throw new ArgumentNullException(nameof(openAIService));
            _promptService = promptService ?? throw new ArgumentNullException(nameof(promptService));
            _promptLoader = promptLoader ?? throw new ArgumentNullException(nameof(promptLoader));
        }

        /// <summary>
        /// Generates a cloze test for the user based on their unfamiliar words.
        /// </summary>
        /// <returns>An IActionResult containing the cloze test or an error response.</returns>
        [HttpGet("cloze")]
        public async Task<IActionResult> GetClozeTest()
        {
            var userId = _userService.GetUserId();
            var user = await _DBContext.Users.SingleOrDefaultAsync(x => x.Id == userId);
            if (user == null)
            {
                return _responseFactory.CreateErrorResponse(ErrorCodes.Forbidden, "User not found.");
            }

            // Get a random word from the user's collection
            var userWords = await _DBContext.UserWords
                .Where(uw => uw.UserId == userId)
                .ToListAsync();

            if (userWords.Count == 0)
            {
                return _responseFactory.CreateErrorResponse(ErrorCodes.Forbidden, "You haven't added any unfamiliar words yet. Please use the favorite icon to add unfamiliar words to the Repository. The exam will generate questions based on the words you're unfamiliar with.");
            }

            var random = new Random();
            var randomIndex = random.Next(userWords.Count);
            string randomWordId = userWords[randomIndex].WordId;

            var word = await _DBContext.Words
                .Include(w => w.ClozeTests)
                    .ThenInclude(ct => ct.Options)
                .SingleOrDefaultAsync(x => x.Id == randomWordId);

            if (word == null)
            {
                return _responseFactory.CreateErrorResponse(ErrorCodes.NotFound, "Word not found.");
            }

            if (word.ClozeTests != null && word.ClozeTests.Count > 3)
            {
                var randomTestIndex = random.Next(word.ClozeTests.Count);
                var randomTest = word.ClozeTests.ElementAt(randomTestIndex);
                return _responseFactory.CreateOKResponse(ConvertClozeTestToDto(randomTest));
            }

            // Use OpenAIPromptService with few-shot example
            var example = (
                User: "coast",
                Assistant: JsonConvert.SerializeObject(new DTO.ClozeTest
                {
                    Question = "The lighthouse was built on the __________ to help guide ships safely to shore.",
                    Answer = "coast",
                    Options = new List<string> { "coast", "cost", "cast", "post" }
                })
            );

            var (clozeTestDTO, error) = await _promptService.GetCompletionAsync<DTO.ClozeTest>(
                "Prompt/Exam/ClozeSystem.txt",
                word.WordText,
                examples: new[] { example });

            var errorMessage = "Query failed, OpenAI could not generate the corresponding cloze test.";
            if (clozeTestDTO == null)
            {
                _logger.LogError("Failed to generate cloze test for word {Word}: {Error}", word.WordText, error);
                return _responseFactory.CreateErrorResponse(ErrorCodes.QueryOpenAIFailed, errorMessage);
            }

            _logger.LogInformation("Generated cloze test for word: {Word}", word.WordText);

            // Randomize options order
            var optionsList = clozeTestDTO.Options.OrderBy(_ => random.NextDouble()).ToList();

            // Create SQL entity
            var clozeTestId = Guid.NewGuid().ToString();
            var newClozeTest = new Data.Models.SQL.ClozeTest
            {
                Id = clozeTestId,
                Question = clozeTestDTO.Question,
                Answer = clozeTestDTO.Answer,
                WordId = word.Id,
                Word = word
            };

            foreach (var option in optionsList)
            {
                newClozeTest.Options.Add(new Data.Models.SQL.ClozeTestOption
                {
                    Id = Guid.NewGuid().ToString(),
                    OptionText = option,
                    ClozeTestId = clozeTestId,
                    ClozeTest = newClozeTest
                });
            }

            // Save to DB if not duplicate
            bool similarTestExists = word.ClozeTests?.Any(x => x.Question == newClozeTest.Question) ?? false;
            if (!similarTestExists)
            {
                word.ClozeTests ??= new List<Data.Models.SQL.ClozeTest>();
                word.ClozeTests.Add(newClozeTest);
                await _DBContext.SaveChangesAsync();
                _logger.LogInformation("ClozeTest added to DB for word: {Word}", word.WordText);
            }

            return _responseFactory.CreateOKResponse(ConvertClozeTestToDto(newClozeTest));
        }

        /// <summary>
        /// Generates a translation test for the user based on their unfamiliar words.
        /// </summary>
        [HttpGet("translation")]
        public async Task<IActionResult> GetTranslationTest()
        {
            var userId = _userService.GetUserId();
            var user = await _DBContext.Users.SingleOrDefaultAsync(x => x.Id == userId);
            if (user == null)
            {
                return _responseFactory.CreateErrorResponse(ErrorCodes.Forbidden, "User not found.");
            }

            var userWords = await _DBContext.UserWords
                .Where(uw => uw.UserId == userId)
                .ToListAsync();

            if (userWords.Count == 0)
            {
                return _responseFactory.CreateErrorResponse(ErrorCodes.Forbidden, "You haven't added any unfamiliar words yet. Please use the favorite icon to add unfamiliar words to the Repository. The exam will generate questions based on the words you're unfamiliar with.");
            }

            var random = new Random();
            var randomIndex = random.Next(userWords.Count);
            string randomWordId = userWords[randomIndex].WordId;

            var word = await _DBContext.Words
                .Include(w => w.TranslationTests)
                .SingleOrDefaultAsync(x => x.Id == randomWordId);

            if (word == null)
            {
                return _responseFactory.CreateErrorResponse(ErrorCodes.NotFound, "Word not found.");
            }

            if (word.TranslationTests != null && word.TranslationTests.Count > 3)
            {
                var randomTestIndex = random.Next(word.TranslationTests.Count);
                var randomTest = word.TranslationTests.ElementAt(randomTestIndex);
                return _responseFactory.CreateOKResponse(ConvertTranslationTestToDto(randomTest, word.WordText));
            }

            var example = (
                User: "appetizer",
                Assistant: JsonConvert.SerializeObject(new DTO.TranslationTest
                {
                    English = "Soup is often a good choice for an appetizer.",
                    Chinese = "湯經常是開胃菜的好選擇。"
                })
            );

            var (translationTestDTO, error) = await _promptService.GetCompletionAsync<DTO.TranslationTest>(
                "Prompt/Exam/TranslationSystem.txt",
                word.WordText,
                examples: new[] { example });

            var errorMessage = "Query failed, OpenAI could not generate the corresponding translation test.";
            if (translationTestDTO == null)
            {
                _logger.LogError("Failed to generate translation test for word {Word}: {Error}", word.WordText, error);
                return _responseFactory.CreateErrorResponse(ErrorCodes.QueryOpenAIFailed, errorMessage);
            }

            _logger.LogInformation("Generated translation test for word: {Word}", word.WordText);

            var translationTest = new Data.Models.SQL.TranslationTest
            {
                Id = Guid.NewGuid().ToString(),
                English = translationTestDTO.English,
                Chinese = translationTestDTO.Chinese,
                WordId = word.Id,
                Word = word
            };

            bool similarTestExists = word.TranslationTests?.Any(x => x.Chinese == translationTest.Chinese) ?? false;
            if (!similarTestExists)
            {
                word.TranslationTests ??= new List<Data.Models.SQL.TranslationTest>();
                word.TranslationTests.Add(translationTest);
                await _DBContext.SaveChangesAsync();
                _logger.LogInformation("TranslationTest added to DB for word: {Word}", word.WordText);
            }

            return _responseFactory.CreateOKResponse(ConvertTranslationTestToDto(translationTest, word.WordText));
        }

        /// <summary>
        /// Generates a translation quality grading.
        /// </summary>
        [HttpPost("translation/quality_grading")]
        public async Task<IActionResult> GetTranslationQualityGrading([FromBody] TranslationTestUserResponse userResponse)
        {
            if (userResponse == null || string.IsNullOrEmpty(userResponse.Translation) || 
                string.IsNullOrEmpty(userResponse.ExaminationQuestion) || string.IsNullOrEmpty(userResponse.UnfamiliarWords))
            {
                return _responseFactory.CreateErrorResponse(ErrorCodes.BadRequest, "Input cannot be null or empty.");
            }

            var example = (
                User: JsonConvert.SerializeObject(new TranslationTestUserResponse
                {
                    UnfamiliarWords = "attentively",
                    ExaminationQuestion = "他專注地閱讀著一本有趣的書。",
                    Translation = "He was reading an interesting book attentively."
                }),
                Assistant: JsonConvert.SerializeObject(new TranslationQualityGradingAssistantResponse
                {
                    TranslationQualityGrading = "A級 原因：這個翻譯完整保留了原文的所有語意，語法和拼寫完全正確，語句流暢且符合英文的表達習慣。"
                })
            );

            var (grading, error) = await _promptService.GetCompletionAsync<TranslationQualityGradingAssistantResponse>(
                "Prompt/Exam/TranslationQualityGradingSystem.txt",
                JsonConvert.SerializeObject(userResponse),
                examples: new[] { example });

            if (grading == null)
            {
                _logger.LogError("Failed to generate translation quality grading: {Error}", error);
                return _responseFactory.CreateErrorResponse(ErrorCodes.QueryOpenAIFailed, 
                    "Query failed, OpenAI could not generate the corresponding quality grading.");
            }

            _logger.LogInformation("Generated translation quality grading for user response");
            return _responseFactory.CreateOKResponse(grading);
        }

        /// <summary>
        /// Starts a new conversation test by generating a scenario and first message.
        /// </summary>
        [HttpPost("conversation/start")]
        public async Task<IActionResult> StartConversationTest([FromBody] ConversationStartRequest request)
        {
            var userId = _userService.GetUserId();
            var user = await _DBContext.Users.SingleOrDefaultAsync(x => x.Id == userId);
            if (user == null)
            {
                return _responseFactory.CreateErrorResponse(ErrorCodes.Forbidden, "User not found.");
            }

            if (request == null || string.IsNullOrEmpty(request.Scenario) || 
                string.IsNullOrEmpty(request.UserRole) || string.IsNullOrEmpty(request.AiRole))
            {
                return _responseFactory.CreateErrorResponse(ErrorCodes.BadRequest, "Scenario, UserRole, and AiRole are required.");
            }

            var systemChatMessage = _promptLoader.GetPrompt("Prompt/Exam/ConversationStartSystem.txt");
            if (string.IsNullOrEmpty(systemChatMessage))
            {
                _logger.LogError("Failed to load ConversationStartSystem.txt prompt");
                return _responseFactory.CreateErrorResponse(ErrorCodes.InternalServerError, "Failed to load conversation prompt.");
            }
            
            var completionResult = await _openAIService.ChatCompletion.CreateCompletion(new ChatCompletionCreateRequest
            {
                Messages =
                [
                    ChatMessage.FromSystem(systemChatMessage),
                    ChatMessage.FromUser(JsonConvert.SerializeObject(request))
                ],
                Model = Models.Gpt_4o_mini,
                Temperature = 0.8f,
            });

            var errorMessage = "Query failed, OpenAI could not generate the conversation scenario.";
            if (!completionResult.Successful)
            {
                return _responseFactory.CreateErrorResponse(ErrorCodes.OpenAIResponseUnsuccessful, errorMessage);
            }

            var content = completionResult.Choices.FirstOrDefault()?.Message.Content;
            if (string.IsNullOrEmpty(content))
            {
                return _responseFactory.CreateErrorResponse(ErrorCodes.QueryOpenAIFailed, errorMessage);
            }

            try
            {
                var conversationStart = JsonConvert.DeserializeObject<ConversationStartResponse>(content);
                if (conversationStart == null)
                {
                    return _responseFactory.CreateErrorResponse(ErrorCodes.QueryOpenAIFailed, errorMessage);
                }

                _logger.LogInformation("Generated conversation start for scenario: {Scenario}", request.Scenario);
                return _responseFactory.CreateOKResponse(conversationStart);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to deserialize conversation start response");
                return _responseFactory.CreateErrorResponse(ErrorCodes.QueryOpenAIFailed, errorMessage);
            }
        }

        /// <summary>
        /// Processes user's response in a conversation and generates AI response with corrections if needed.
        /// </summary>
        [HttpPost("conversation/respond")]
        public async Task<IActionResult> RespondToConversation([FromBody] ConversationResponseRequest request)
        {
            if (request == null || string.IsNullOrEmpty(request.UserMessage) || request.ConversationContext == null)
            {
                return _responseFactory.CreateErrorResponse(ErrorCodes.BadRequest, "Invalid conversation request.");
            }

            var userId = _userService.GetUserId();
            var user = await _DBContext.Users.SingleOrDefaultAsync(x => x.Id == userId);
            if (user == null)
            {
                return _responseFactory.CreateErrorResponse(ErrorCodes.Forbidden, "User not found.");
            }

            var (response, error) = await _promptService.GetCompletionAsync<ConversationResponseResponse>(
                "Prompt/Exam/ConversationResponseSystem.txt",
                JsonConvert.SerializeObject(request));

            if (response == null)
            {
                _logger.LogError("Failed to generate conversation response: {Error}", error);
                return _responseFactory.CreateErrorResponse(ErrorCodes.QueryOpenAIFailed,
                    "Query failed, OpenAI could not generate the conversation response.");
            }

            _logger.LogInformation("Generated conversation response for turn {Turn}", request.ConversationContext.TurnNumber);
            return _responseFactory.CreateOKResponse(response);
        }

        /// <summary>
        /// Transcribes audio to text using OpenAI Whisper.
        /// </summary>
        [HttpPost("whisper/transcribe")]
        public async Task<IActionResult> TranscribeAudio(IFormFile audioFile)
        {
            if (audioFile == null || audioFile.Length == 0)
            {
                return _responseFactory.CreateErrorResponse(ErrorCodes.BadRequest, "No audio file provided.");
            }

            var userId = _userService.GetUserId();
            var user = await _DBContext.Users.SingleOrDefaultAsync(x => x.Id == userId);
            if (user == null)
            {
                return _responseFactory.CreateErrorResponse(ErrorCodes.Forbidden, "User not found.");
            }

            using var stream = audioFile.OpenReadStream();
            var (transcription, error) = await _promptService.TranscribeAudioAsync(stream, audioFile.FileName);

            if (transcription == null)
            {
                _logger.LogError("Whisper transcription failed: {Error}", error);
                return _responseFactory.CreateErrorResponse(ErrorCodes.QueryOpenAIFailed, "Failed to transcribe audio.");
            }

            _logger.LogInformation("Whisper transcription successful");
            return _responseFactory.CreateOKResponse(new WhisperTranscriptionResponse
            {
                Text = transcription.Trim()
            });
        }

        /// <summary>
        /// Generates a sentence formation test for the user based on their unfamiliar words.
        /// </summary>
        [HttpGet("sentence")]
        public async Task<IActionResult> GetSentenceTest()
        {
            var userId = _userService.GetUserId();
            var user = await _DBContext.Users.SingleOrDefaultAsync(x => x.Id == userId);
            if (user == null)
            {
                return _responseFactory.CreateErrorResponse(ErrorCodes.Forbidden, "User not found.");
            }

            var userWords = await _DBContext.UserWords
                .Where(uw => uw.UserId == userId)
                .ToListAsync();

            if (userWords.Count == 0)
            {
                return _responseFactory.CreateErrorResponse(ErrorCodes.Forbidden, "You haven't added any unfamiliar words yet. Please use the favorite icon to add unfamiliar words to the Repository. The exam will generate questions based on the words you're unfamiliar with.");
            }

            var random = new Random();
            var randomIndex = random.Next(userWords.Count);
            string randomWordId = userWords[randomIndex].WordId;

            var word = await _DBContext.Words
                .Include(w => w.SentenceTests)
                .SingleOrDefaultAsync(x => x.Id == randomWordId);

            if (word == null)
            {
                return _responseFactory.CreateErrorResponse(ErrorCodes.NotFound, "Word not found.");
            }

            if (word.SentenceTests != null && word.SentenceTests.Count > 3)
            {
                var randomTestIndex = random.Next(word.SentenceTests.Count);
                var randomTest = word.SentenceTests.ElementAt(randomTestIndex);
                return _responseFactory.CreateOKResponse(ConvertSentenceTestToDto(randomTest, word.WordText));
            }

            var example = (
                User: "adventure",
                Assistant: JsonConvert.SerializeObject(new DTO.SentenceTest
                {
                    Prompt = "Create a sentence using the word 'adventure' in the context of travel.",
                    SampleAnswer = "Last summer, I went on an amazing adventure to explore the mountains.",
                    Context = "Travel and exploration",
                    DifficultyLevel = 3,
                    GrammarPattern = "Past tense narrative"
                })
            );

            var (sentenceTestDTO, error) = await _promptService.GetCompletionAsync<DTO.SentenceTest>(
                "Prompt/Exam/SentenceSystem.txt",
                word.WordText,
                examples: new[] { example });

            var errorMessage = "Query failed, OpenAI could not generate the corresponding sentence test.";
            if (sentenceTestDTO == null)
            {
                _logger.LogError("Failed to generate sentence test for word {Word}: {Error}", word.WordText, error);
                return _responseFactory.CreateErrorResponse(ErrorCodes.QueryOpenAIFailed, errorMessage);
            }

            _logger.LogInformation("Generated sentence test for word: {Word}", word.WordText);

            var sentenceTest = new Data.Models.SQL.SentenceTest
            {
                Id = Guid.NewGuid().ToString(),
                Prompt = sentenceTestDTO.Prompt,
                SampleAnswer = sentenceTestDTO.SampleAnswer,
                Context = sentenceTestDTO.Context,
                DifficultyLevel = sentenceTestDTO.DifficultyLevel,
                GrammarPattern = sentenceTestDTO.GrammarPattern,
                WordId = word.Id,
                Word = word
            };

            bool similarTestExists = word.SentenceTests?.Any(x => x.Prompt == sentenceTest.Prompt) ?? false;
            if (!similarTestExists)
            {
                word.SentenceTests ??= new List<Data.Models.SQL.SentenceTest>();
                word.SentenceTests.Add(sentenceTest);
                await _DBContext.SaveChangesAsync();
                _logger.LogInformation("SentenceTest added to DB for word: {Word}", word.WordText);
            }

            return _responseFactory.CreateOKResponse(ConvertSentenceTestToDto(sentenceTest, word.WordText));
        }

        /// <summary>
        /// Evaluates a user's sentence formation response and provides detailed feedback.
        /// </summary>
        [HttpPost("sentence/evaluate")]
        public async Task<IActionResult> EvaluateSentence([FromBody] SentenceTestUserResponse userResponse)
        {
            if (userResponse == null || string.IsNullOrEmpty(userResponse.UserSentence) || 
                string.IsNullOrEmpty(userResponse.Word) || string.IsNullOrEmpty(userResponse.Prompt))
            {
                return _responseFactory.CreateErrorResponse(ErrorCodes.BadRequest, "Input cannot be null or empty.");
            }

            var example = (
                User: JsonConvert.SerializeObject(new SentenceTestUserResponse
                {
                    Word = "adventure",
                    Prompt = "Create a sentence using the word 'adventure' in the context of travel.",
                    Context = "Travel and exploration",
                    UserSentence = "My adventure to Japan was incredible and full of surprises.",
                    DifficultyLevel = 3,
                    GrammarPattern = "Past tense narrative"
                }),
                Assistant: JsonConvert.SerializeObject(new SentenceTestGradingResponse
                {
                    Score = 85,
                    GrammarFeedback = "語法正確，時態使用恰當。",
                    UsageFeedback = "單字 'adventure' 使用正確，完全符合旅遊情境。",
                    CreativityFeedback = "句子表達生動，用詞豐富。",
                    OverallFeedback = "整體表現優秀，句子結構完整，意思清楚。",
                    Suggestions = new List<string> { "可以考慮添加更多細節描述", "嘗試使用更多形容詞來豐富句子" }
                })
            );

            var (evaluation, error) = await _promptService.GetCompletionAsync<SentenceTestGradingResponse>(
                "Prompt/Exam/SentenceEvaluationSystem.txt",
                JsonConvert.SerializeObject(userResponse),
                examples: new[] { example });

            if (evaluation == null)
            {
                _logger.LogError("Failed to generate sentence evaluation: {Error}", error);
                return _responseFactory.CreateErrorResponse(ErrorCodes.QueryOpenAIFailed,
                    "Query failed, OpenAI could not generate the sentence evaluation.");
            }

            _logger.LogInformation("Generated sentence evaluation for word: {Word}", userResponse.Word);
            return _responseFactory.CreateOKResponse(evaluation);
        }

        private static ClozeTest ConvertClozeTestToDto(Data.Models.SQL.ClozeTest sqlClozeTest)
        {
            return new ClozeTest
            {
                Question = sqlClozeTest.Question,
                Answer = sqlClozeTest.Answer,
                Options = sqlClozeTest.Options.Select(o => o.OptionText).ToList()
            };
        }

        private static TranslationTestResponse ConvertTranslationTestToDto(Data.Models.SQL.TranslationTest sqlTranslationTest, string wordText)
        {
            return new TranslationTestResponse
            {
                Word = wordText,
                Chinese = sqlTranslationTest.Chinese,
                English = sqlTranslationTest.English
            };
        }

        private static SentenceTestResponse ConvertSentenceTestToDto(Data.Models.SQL.SentenceTest sqlSentenceTest, string wordText)
        {
            return new SentenceTestResponse
            {
                Word = wordText,
                Prompt = sqlSentenceTest.Prompt,
                SampleAnswer = sqlSentenceTest.SampleAnswer,
                Context = sqlSentenceTest.Context,
                DifficultyLevel = sqlSentenceTest.DifficultyLevel,
                GrammarPattern = sqlSentenceTest.GrammarPattern
            };
        }
    }
}
