﻿using Betalgo.Ranul.OpenAI.Interfaces;
using Betalgo.Ranul.OpenAI.ObjectModels;
using Betalgo.Ranul.OpenAI.ObjectModels.RequestModels;
using DotnetSdkUtilities.Services;
using JackyAIApp.Server.Common;
using JackyAIApp.Server.Configuration;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.DTO;
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
    public class ExamController(ILogger<ExamController> logger, IOptionsMonitor<Settings> settings, IMyResponseFactory responseFactory, AzureSQLDBContext DBContext, IUserService userService, IExtendedMemoryCache memoryCache, IOpenAIService openAIService) : ControllerBase
    {
        private readonly ILogger<ExamController> _logger = logger ?? throw new ArgumentNullException();
        private readonly IOptionsMonitor<Settings> _settings = settings;
        private readonly IMyResponseFactory _responseFactory = responseFactory ?? throw new ArgumentNullException();
        private readonly AzureSQLDBContext _DBContext = DBContext;
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
                // Check if there are enough test questions for the vocabulary; if there are more than three, no additional questions will be generated.
                var randomTestIndex = random.Next(word.ClozeTests.Count);
                var randomTest = word.ClozeTests.ElementAt(randomTestIndex);
                return _responseFactory.CreateOKResponse(ConvertClozeTestToDto(randomTest));
            }

            string systemChatMessage = System.IO.File.ReadAllText("Prompt/Exam/ClozeSystem.txt");
            var completionResult = await _openAIService.ChatCompletion.CreateCompletion(new ChatCompletionCreateRequest
            {
                Messages =
                [
                    ChatMessage.FromSystem(systemChatMessage),
                    ChatMessage.FromUser("coast"),
                    ChatMessage.FromAssistant(JsonConvert.SerializeObject(new DTO.ClozeTest()
                    {
                        Question = "The lighthouse was built on the __________ to help guide ships safely to shore.",
                        Answer = "coast",
                        Options = ["coast", "cost", "cast", "post"]
                    })),
                    ChatMessage.FromUser(word.WordText)
                ],
                Model = Models.Gpt_4o_mini,
            });
            var errorMessage = "Query failed, OpenAI could not generate the corresponding cloze test.";
            if (completionResult.Successful)
            {
                _logger.LogInformation("Generate cloze test: {lowerWord}, result: {json}", word.WordText, JsonConvert.SerializeObject(completionResult, Formatting.Indented));
                var content = completionResult.Choices.FirstOrDefault()?.Message.Content;
                if (string.IsNullOrEmpty(content))
                {
                    return _responseFactory.CreateErrorResponse(ErrorCodes.QueryOpenAIFailed, errorMessage);
                }
                Data.Models.SQL.ClozeTest? clozeTest = null;
                try
                {
                    var clozeTestDTO = JsonConvert.DeserializeObject<DTO.ClozeTest>(content);

                    // Convert options to list if needed
                    if (clozeTestDTO != null && clozeTestDTO.Options != null)
                    {
                        // Randomize the order of options
                        var optionsList = clozeTestDTO.Options.OrderBy(x => random.NextDouble()).ToList();

                        // Create a new cloze test to save to the database
                        string clozeTestId = Guid.NewGuid().ToString();
                        var newClozeTest = new Data.Models.SQL.ClozeTest
                        {
                            Id = clozeTestId,
                            Question = clozeTestDTO.Question,
                            Answer = clozeTestDTO.Answer,
                            WordId = word.Id,
                            Word = word
                        };

                        // Add options
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

                        clozeTest = newClozeTest;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to deserialize the content: {content}", content);
                    return _responseFactory.CreateErrorResponse(ErrorCodes.QueryOpenAIFailed, errorMessage);
                }

                if (clozeTest == null)
                {
                    return _responseFactory.CreateErrorResponse(ErrorCodes.QueryOpenAIFailed, errorMessage);
                }

                // Check if a similar cloze test already exists
                bool similarTestExists = false;
                if (word.ClozeTests != null)
                {
                    similarTestExists = word.ClozeTests.Any(x => x.Question == clozeTest.Question);
                }

                if (!similarTestExists)
                {
                    if (word.ClozeTests == null)
                    {
                        word.ClozeTests = new List<Data.Models.SQL.ClozeTest>();
                    }
                    word.ClozeTests.Add(clozeTest);
                    await _DBContext.SaveChangesAsync();
                    _logger.LogInformation("clozeTest: {clozeTestJson} added to DB.", JsonConvert.SerializeObject(ConvertClozeTestToDto(clozeTest)));
                }
                return _responseFactory.CreateOKResponse(ConvertClozeTestToDto(clozeTest));
            }
            return _responseFactory.CreateErrorResponse(ErrorCodes.OpenAIResponseUnsuccessful, errorMessage);
        }

        /// <summary>
        /// Generates a translation test for the user based on their unfamiliar words.
        /// </summary>
        /// <returns>An IActionResult containing the translation test or an error response.</returns>
        [HttpGet("translation")]
        public async Task<IActionResult> GetTranslationTest()
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
                .Include(w => w.TranslationTests)
                .SingleOrDefaultAsync(x => x.Id == randomWordId);

            if (word == null)
            {
                return _responseFactory.CreateErrorResponse(ErrorCodes.NotFound, "Word not found.");
            }

            if (word.TranslationTests != null && word.TranslationTests.Count > 3)
            {
                // Check if there are enough test questions for the vocabulary; if there are more than three, no additional questions will be generated.
                var randomTestIndex = random.Next(word.TranslationTests.Count);
                var randomTest = word.TranslationTests.ElementAt(randomTestIndex);
                return _responseFactory.CreateOKResponse(ConvertTranslationTestToDto(randomTest, word.WordText));
            }

            string systemChatMessage = System.IO.File.ReadAllText("Prompt/Exam/TranslationSystem.txt");
            var completionResult = await _openAIService.ChatCompletion.CreateCompletion(new ChatCompletionCreateRequest
            {
                Messages =
                [
                    ChatMessage.FromSystem(systemChatMessage),
                    ChatMessage.FromUser("appetizer"),
                    ChatMessage.FromAssistant(JsonConvert.SerializeObject(new DTO.TranslationTest()
                    {
                        English = "Soup is often a good choice for an appetizer.",
                        Chinese = "湯經常是開胃菜的好選擇。"
                    })),
                    ChatMessage.FromUser(word.WordText)
                ],
                Model = Models.Gpt_4o_mini,
            });
            var errorMessage = "Query failed, OpenAI could not generate the corresponding translation test.";
            if (completionResult.Successful)
            {
                _logger.LogInformation("Generate translation test: {lowerWord}, result: {json}", word.WordText, JsonConvert.SerializeObject(completionResult, Formatting.Indented));
                var content = completionResult.Choices.FirstOrDefault()?.Message.Content;
                if (string.IsNullOrEmpty(content))
                {
                    return _responseFactory.CreateErrorResponse(ErrorCodes.QueryOpenAIFailed, errorMessage);
                }
                Data.Models.SQL.TranslationTest? translationTest = null;
                try
                {
                    var testFromJson = JsonConvert.DeserializeObject<DTO.TranslationTest>(content);

                    if (testFromJson != null)
                    {
                        translationTest = new Data.Models.SQL.TranslationTest
                        {
                            Id = Guid.NewGuid().ToString(),
                            English = testFromJson.English,
                            Chinese = testFromJson.Chinese,
                            WordId = word.Id,
                            Word = word
                        };
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to deserialize the content: {content}", content);
                    return _responseFactory.CreateErrorResponse(ErrorCodes.QueryOpenAIFailed, errorMessage);
                }

                if (translationTest == null)
                {
                    return _responseFactory.CreateErrorResponse(ErrorCodes.QueryOpenAIFailed, errorMessage);
                }

                bool similarTestExists = false;
                if (word.TranslationTests != null)
                {
                    similarTestExists = word.TranslationTests.Any(x => x.Chinese == translationTest.Chinese);
                }

                if (!similarTestExists)
                {
                    if (word.TranslationTests == null)
                    {
                        word.TranslationTests = new List<Data.Models.SQL.TranslationTest>();
                    }
                    word.TranslationTests.Add(translationTest);
                    await _DBContext.SaveChangesAsync();
                    _logger.LogInformation("translationTest: {translationTestJson} added to DB.", JsonConvert.SerializeObject(ConvertTranslationTestToDto(translationTest, word.WordText)));
                }

                return _responseFactory.CreateOKResponse(ConvertTranslationTestToDto(translationTest, word.WordText));
            }
            return _responseFactory.CreateErrorResponse(ErrorCodes.OpenAIResponseUnsuccessful, errorMessage);
        }

        /// <summary>
        /// Generates a translation quality grading.
        /// </summary>
        /// <returns>An IActionResult containing the translation quality grading or an error response.</returns>
        [HttpPost("translation/quality_grading")]
        public async Task<IActionResult> GetTranslationQualityGrading([FromBody] TranslationTestUserResponse userResponse)
        {
            if (userResponse == null || string.IsNullOrEmpty(userResponse.Translation) || string.IsNullOrEmpty(userResponse.ExaminationQuestion) || string.IsNullOrEmpty(userResponse.UnfamiliarWords))
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
                    return _responseFactory.CreateErrorResponse(ErrorCodes.QueryOpenAIFailed, errorMessage);
                }
                TranslationQualityGradingAssistantResponse? translationQualityGrading = null;
                try
                {
                    translationQualityGrading = JsonConvert.DeserializeObject<TranslationQualityGradingAssistantResponse>(content);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to deserialize the content: {content}", content);
                    return _responseFactory.CreateErrorResponse(ErrorCodes.QueryOpenAIFailed, errorMessage);
                }
                if (translationQualityGrading == null)
                {
                    return _responseFactory.CreateErrorResponse(ErrorCodes.QueryOpenAIFailed, errorMessage);
                }
                return _responseFactory.CreateOKResponse(translationQualityGrading);
            }
            return _responseFactory.CreateErrorResponse(ErrorCodes.OpenAIResponseUnsuccessful, errorMessage);
        }

        /// <summary>
        /// Starts a new conversation test by generating a scenario and first message.
        /// </summary>
        /// <returns>An IActionResult containing the conversation start response or an error response.</returns>
        [HttpPost("conversation/start")]
        public async Task<IActionResult> StartConversationTest([FromBody] ConversationStartRequest request)
        {
            var userId = _userService.GetUserId();
            var user = await _DBContext.Users.SingleOrDefaultAsync(x => x.Id == userId);
            if (user == null)
            {
                return _responseFactory.CreateErrorResponse(ErrorCodes.Forbidden, "User not found.");
            }

            if (request == null || string.IsNullOrEmpty(request.Scenario) || string.IsNullOrEmpty(request.UserRole) || string.IsNullOrEmpty(request.AiRole))
            {
                return _responseFactory.CreateErrorResponse(ErrorCodes.BadRequest, "Scenario, UserRole, and AiRole are required.");
            }

            string systemChatMessage = System.IO.File.ReadAllText("Prompt/Exam/ConversationStartSystem.txt");
            
            var completionResult = await _openAIService.ChatCompletion.CreateCompletion(new ChatCompletionCreateRequest
            {
                Messages =
                [
                    ChatMessage.FromSystem(systemChatMessage),
                    ChatMessage.FromUser(JsonConvert.SerializeObject(request))
                ],
                Model = Models.Gpt_4o_mini,
                Temperature = 0.8f, // Increase creativity for varied responses
            });

            var errorMessage = "Query failed, OpenAI could not generate the conversation scenario.";
            if (completionResult.Successful)
            {
                _logger.LogInformation("Generate conversation start: {request}, result: {json}", JsonConvert.SerializeObject(request), JsonConvert.SerializeObject(completionResult, Formatting.Indented));
                var content = completionResult.Choices.FirstOrDefault()?.Message.Content;
                if (string.IsNullOrEmpty(content))
                {
                    return _responseFactory.CreateErrorResponse(ErrorCodes.QueryOpenAIFailed, errorMessage);
                }

                ConversationStartResponse? conversationStart = null;
                try
                {
                    conversationStart = JsonConvert.DeserializeObject<ConversationStartResponse>(content);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to deserialize the content: {content}", content);
                    return _responseFactory.CreateErrorResponse(ErrorCodes.QueryOpenAIFailed, errorMessage);
                }

                if (conversationStart == null)
                {
                    return _responseFactory.CreateErrorResponse(ErrorCodes.QueryOpenAIFailed, errorMessage);
                }

                return _responseFactory.CreateOKResponse(conversationStart);
            }
            return _responseFactory.CreateErrorResponse(ErrorCodes.OpenAIResponseUnsuccessful, errorMessage);
        }

        /// <summary>
        /// Processes user's response in a conversation and generates AI response with corrections if needed.
        /// </summary>
        /// <returns>An IActionResult containing the AI response and correction feedback.</returns>
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

            string systemChatMessage = System.IO.File.ReadAllText("Prompt/Exam/ConversationResponseSystem.txt");
            var completionResult = await _openAIService.ChatCompletion.CreateCompletion(new ChatCompletionCreateRequest
            {
                Messages =
                [
                    ChatMessage.FromSystem(systemChatMessage),
                    ChatMessage.FromUser(JsonConvert.SerializeObject(request))
                ],
                Model = Models.Gpt_4o_mini,
            });

            var errorMessage = "Query failed, OpenAI could not generate the conversation response.";
            if (completionResult.Successful)
            {
                _logger.LogInformation("Generate conversation response: {request}, result: {json}", JsonConvert.SerializeObject(request), JsonConvert.SerializeObject(completionResult, Formatting.Indented));
                var content = completionResult.Choices.FirstOrDefault()?.Message.Content;
                if (string.IsNullOrEmpty(content))
                {
                    return _responseFactory.CreateErrorResponse(ErrorCodes.QueryOpenAIFailed, errorMessage);
                }

                ConversationResponseResponse? conversationResponse = null;
                try
                {
                    conversationResponse = JsonConvert.DeserializeObject<ConversationResponseResponse>(content);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to deserialize the content: {content}", content);
                    return _responseFactory.CreateErrorResponse(ErrorCodes.QueryOpenAIFailed, errorMessage);
                }

                if (conversationResponse == null)
                {
                    return _responseFactory.CreateErrorResponse(ErrorCodes.QueryOpenAIFailed, errorMessage);
                }

                return _responseFactory.CreateOKResponse(conversationResponse);
            }
            return _responseFactory.CreateErrorResponse(ErrorCodes.OpenAIResponseUnsuccessful, errorMessage);
        }

        /// <summary>
        /// Transcribes audio to text using OpenAI Whisper.
        /// </summary>
        /// <returns>An IActionResult containing the transcription or an error response.</returns>
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

            try
            {
                using var stream = audioFile.OpenReadStream();
                using var memoryStream = new MemoryStream();
                await stream.CopyToAsync(memoryStream);
                var audioBytes = memoryStream.ToArray();

                var audioResult = await _openAIService.Audio.CreateTranscription(new Betalgo.Ranul.OpenAI.ObjectModels.RequestModels.AudioCreateTranscriptionRequest
                {
                    File = audioBytes,
                    FileName = audioFile.FileName,
                    Model = Betalgo.Ranul.OpenAI.ObjectModels.Models.WhisperV1,
                    Language = "en" // English language for better accuracy
                });

                if (audioResult.Successful && !string.IsNullOrEmpty(audioResult.Text))
                {
                    _logger.LogInformation("Whisper transcription successful: {text}", audioResult.Text);
                    return _responseFactory.CreateOKResponse(new WhisperTranscriptionResponse
                    {
                        Text = audioResult.Text.Trim()
                    });
                }
                else
                {
                    _logger.LogError("Whisper transcription failed: {error}", audioResult.Error?.Message);
                    return _responseFactory.CreateErrorResponse(ErrorCodes.QueryOpenAIFailed, "Failed to transcribe audio.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during Whisper transcription");
                return _responseFactory.CreateErrorResponse(ErrorCodes.QueryOpenAIFailed, "Error processing audio file.");
            }
        }

        /// <summary>
        /// Generates a sentence formation test for the user based on their unfamiliar words.
        /// </summary>
        /// <returns>An IActionResult containing the sentence test or an error response.</returns>
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

            string systemChatMessage = System.IO.File.ReadAllText("Prompt/Exam/SentenceSystem.txt");
            var completionResult = await _openAIService.ChatCompletion.CreateCompletion(new ChatCompletionCreateRequest
            {
                Messages =
                [
                    ChatMessage.FromSystem(systemChatMessage),
                    ChatMessage.FromUser("adventure"),
                    ChatMessage.FromAssistant(JsonConvert.SerializeObject(new DTO.SentenceTest()
                    {
                        Prompt = "Create a sentence using the word 'adventure' in the context of travel.",
                        SampleAnswer = "Last summer, I went on an amazing adventure to explore the mountains.",
                        Context = "Travel and exploration",
                        DifficultyLevel = 3,
                        GrammarPattern = "Past tense narrative"
                    })),
                    ChatMessage.FromUser(word.WordText)
                ],
                Model = Models.Gpt_4o_mini,
            });

            var errorMessage = "Query failed, OpenAI could not generate the corresponding sentence test.";
            if (completionResult.Successful)
            {
                _logger.LogInformation("Generate sentence test: {word}, result: {json}", word.WordText, JsonConvert.SerializeObject(completionResult, Formatting.Indented));
                var content = completionResult.Choices.FirstOrDefault()?.Message.Content;
                if (string.IsNullOrEmpty(content))
                {
                    return _responseFactory.CreateErrorResponse(ErrorCodes.QueryOpenAIFailed, errorMessage);
                }

                Data.Models.SQL.SentenceTest? sentenceTest = null;
                try
                {
                    var testFromJson = JsonConvert.DeserializeObject<DTO.SentenceTest>(content);

                    if (testFromJson != null)
                    {
                        sentenceTest = new Data.Models.SQL.SentenceTest
                        {
                            Id = Guid.NewGuid().ToString(),
                            Prompt = testFromJson.Prompt,
                            SampleAnswer = testFromJson.SampleAnswer,
                            Context = testFromJson.Context,
                            DifficultyLevel = testFromJson.DifficultyLevel,
                            GrammarPattern = testFromJson.GrammarPattern,
                            WordId = word.Id,
                            Word = word
                        };
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to deserialize the content: {content}", content);
                    return _responseFactory.CreateErrorResponse(ErrorCodes.QueryOpenAIFailed, errorMessage);
                }

                if (sentenceTest == null)
                {
                    return _responseFactory.CreateErrorResponse(ErrorCodes.QueryOpenAIFailed, errorMessage);
                }

                bool similarTestExists = false;
                if (word.SentenceTests != null)
                {
                    similarTestExists = word.SentenceTests.Any(x => x.Prompt == sentenceTest.Prompt);
                }

                if (!similarTestExists)
                {
                    if (word.SentenceTests == null)
                    {
                        word.SentenceTests = new List<Data.Models.SQL.SentenceTest>();
                    }
                    word.SentenceTests.Add(sentenceTest);
                    await _DBContext.SaveChangesAsync();
                    _logger.LogInformation("sentenceTest: {sentenceTestJson} added to DB.", JsonConvert.SerializeObject(ConvertSentenceTestToDto(sentenceTest, word.WordText)));
                }

                return _responseFactory.CreateOKResponse(ConvertSentenceTestToDto(sentenceTest, word.WordText));
            }
            return _responseFactory.CreateErrorResponse(ErrorCodes.OpenAIResponseUnsuccessful, errorMessage);
        }

        /// <summary>
        /// Evaluates a user's sentence formation response and provides detailed feedback.
        /// </summary>
        /// <returns>An IActionResult containing the sentence evaluation or an error response.</returns>
        [HttpPost("sentence/evaluate")]
        public async Task<IActionResult> EvaluateSentence([FromBody] SentenceTestUserResponse userResponse)
        {
            if (userResponse == null || string.IsNullOrEmpty(userResponse.UserSentence) || string.IsNullOrEmpty(userResponse.Word) || string.IsNullOrEmpty(userResponse.Prompt))
            {
                return _responseFactory.CreateErrorResponse(ErrorCodes.BadRequest, "Input cannot be null or empty.");
            }

            string systemChatMessage = System.IO.File.ReadAllText("Prompt/Exam/SentenceEvaluationSystem.txt");
            var completionResult = await _openAIService.ChatCompletion.CreateCompletion(new ChatCompletionCreateRequest
            {
                Messages =
                [
                    ChatMessage.FromSystem(systemChatMessage),
                    ChatMessage.FromUser(JsonConvert.SerializeObject(new SentenceTestUserResponse()
                    {
                        Word = "adventure",
                        Prompt = "Create a sentence using the word 'adventure' in the context of travel.",
                        Context = "Travel and exploration",
                        UserSentence = "My adventure to Japan was incredible and full of surprises.",
                        DifficultyLevel = 3,
                        GrammarPattern = "Past tense narrative"
                    })),
                    ChatMessage.FromAssistant(JsonConvert.SerializeObject(new SentenceTestGradingResponse()
                    {
                        Score = 85,
                        GrammarFeedback = "語法正確，時態使用恰當。",
                        UsageFeedback = "單字 'adventure' 使用正確，完全符合旅遊情境。",
                        CreativityFeedback = "句子表達生動，用詞豐富。",
                        OverallFeedback = "整體表現優秀，句子結構完整，意思清楚。",
                        Suggestions = ["可以考慮添加更多細節描述", "嘗試使用更多形容詞來豐富句子"]
                    })),
                    ChatMessage.FromUser(JsonConvert.SerializeObject(userResponse))
                ],
                Model = Models.Gpt_4o_mini,
            });

            var errorMessage = "Query failed, OpenAI could not generate the sentence evaluation.";
            if (completionResult.Successful)
            {
                _logger.LogInformation("Generate sentence evaluation, userResponse: {userResponse}, result: {json}", JsonConvert.SerializeObject(userResponse, Formatting.Indented), JsonConvert.SerializeObject(completionResult, Formatting.Indented));
                var content = completionResult.Choices.FirstOrDefault()?.Message.Content;
                if (string.IsNullOrEmpty(content))
                {
                    return _responseFactory.CreateErrorResponse(ErrorCodes.QueryOpenAIFailed, errorMessage);
                }

                SentenceTestGradingResponse? sentenceEvaluation = null;
                try
                {
                    sentenceEvaluation = JsonConvert.DeserializeObject<SentenceTestGradingResponse>(content);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to deserialize the content: {content}", content);
                    return _responseFactory.CreateErrorResponse(ErrorCodes.QueryOpenAIFailed, errorMessage);
                }

                if (sentenceEvaluation == null)
                {
                    return _responseFactory.CreateErrorResponse(ErrorCodes.QueryOpenAIFailed, errorMessage);
                }

                return _responseFactory.CreateOKResponse(sentenceEvaluation);
            }
            return _responseFactory.CreateErrorResponse(ErrorCodes.OpenAIResponseUnsuccessful, errorMessage);
        }

        /// <summary>
        /// Converts SQL ClozeTest entity to DTO to avoid circular reference issues
        /// </summary>
        private ClozeTest ConvertClozeTestToDto(Data.Models.SQL.ClozeTest sqlClozeTest)
        {
            return new ClozeTest
            {
                Question = sqlClozeTest.Question,
                Answer = sqlClozeTest.Answer,
                Options = sqlClozeTest.Options.Select(o => o.OptionText).ToList()
            };
        }

        /// <summary>
        /// Converts SQL TranslationTest entity to DTO to avoid circular reference issues
        /// </summary>
        private TranslationTestResponse ConvertTranslationTestToDto(Data.Models.SQL.TranslationTest sqlTranslationTest, string wordText)
        {
            return new TranslationTestResponse
            {
                Word = wordText,
                Chinese = sqlTranslationTest.Chinese,
                English = sqlTranslationTest.English
            };
        }

        /// <summary>
        /// Converts SQL SentenceTest entity to DTO to avoid circular reference issues
        /// </summary>
        private SentenceTestResponse ConvertSentenceTestToDto(Data.Models.SQL.SentenceTest sqlSentenceTest, string wordText)
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
