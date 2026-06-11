using Betalgo.Ranul.OpenAI.Interfaces;
using Betalgo.Ranul.OpenAI.ObjectModels;
using Betalgo.Ranul.OpenAI.ObjectModels.RequestModels;
using JackyAIApp.Server.Common;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.DTO;
using JackyAIApp.Server.Services.OpenAI;
using JackyAIApp.Server.Services.Prompt;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

namespace JackyAIApp.Server.Services.Exam
{
    public class ExamService(
        ILogger<ExamService> logger,
        AzureSQLDBContext dbContext,
        IUserService userService,
        IOpenAIService openAIService,
        IOpenAIPromptService promptService,
        IPromptLoader promptLoader,
        ICreditService creditService,
        IEnumerable<IWordExamGenerator> generators) : IExamService
    {
        private readonly ILogger<ExamService> _logger = logger;
        private readonly AzureSQLDBContext _dbContext = dbContext;
        private readonly IUserService _userService = userService;
        private readonly IOpenAIService _openAIService = openAIService;
        private readonly IOpenAIPromptService _promptService = promptService;
        private readonly IPromptLoader _promptLoader = promptLoader;
        private readonly ICreditService _creditService = creditService;
        private readonly IReadOnlyDictionary<ExamType, IWordExamGenerator> _generators =
            generators.ToDictionary(g => g.Type);

        private const string NoWordsMessage =
            "You haven't added any unfamiliar words yet. Please use the favorite icon to add unfamiliar words to the Repository. The exam will generate questions based on the words you're unfamiliar with.";

        public async Task<ServiceResult> GenerateWordExamAsync(ExamType type)
        {
            var userId = _userService.GetUserId();
            var user = await _dbContext.Users.SingleOrDefaultAsync(x => x.Id == userId);
            if (user == null)
            {
                return ServiceResult.Fail(ErrorCodes.Forbidden, "User not found.");
            }

            var randomWordId = await GetRandomUserWordIdAsync(userId!);
            if (randomWordId == null)
            {
                return ServiceResult.Fail(ErrorCodes.Forbidden, NoWordsMessage);
            }

            var word = await _dbContext.Words
                .Include(w => w.ClozeTests)
                    .ThenInclude(ct => ct.Options)
                .Include(w => w.TranslationTests)
                .Include(w => w.SentenceTests)
                .SingleOrDefaultAsync(x => x.Id == randomWordId);

            if (word == null)
            {
                return ServiceResult.Fail(ErrorCodes.NotFound, "Word not found.");
            }

            if (!_generators.TryGetValue(type, out var generator))
            {
                return ServiceResult.Fail(ErrorCodes.BadRequest, $"Unsupported exam type: {type}");
            }

            return await generator.GenerateAsync(word);
        }

        public async Task<ServiceResult> GradeTranslationAsync(TranslationTestUserResponse userResponse)
        {
            // Credit gate (conditional on an authenticated user)
            var userId = _userService.GetUserId();
            if (!string.IsNullOrEmpty(userId) &&
                !await _creditService.ConsumeCreditsAsync(userId, CreditCosts.TranslationGrading, "translation_grading", "Translation quality grading"))
            {
                return ServiceResult.Fail(ErrorCodes.Forbidden, $"Insufficient credits. Required: {CreditCosts.TranslationGrading}.");
            }

            if (userResponse == null || string.IsNullOrEmpty(userResponse.Translation) ||
                string.IsNullOrEmpty(userResponse.ExaminationQuestion) || string.IsNullOrEmpty(userResponse.UnfamiliarWords))
            {
                return ServiceResult.Fail(ErrorCodes.BadRequest, "Input cannot be null or empty.");
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
                return ServiceResult.Fail(ErrorCodes.QueryOpenAIFailed,
                    "Query failed, OpenAI could not generate the corresponding quality grading.");
            }

            _logger.LogInformation("Generated translation quality grading for user response");
            return ServiceResult.Ok(grading);
        }

        public async Task<ServiceResult> StartConversationAsync(ConversationStartRequest request)
        {
            var userId = _userService.GetUserId();
            var user = await _dbContext.Users.SingleOrDefaultAsync(x => x.Id == userId);
            if (user == null)
            {
                return ServiceResult.Fail(ErrorCodes.Forbidden, "User not found.");
            }

            if (request == null || string.IsNullOrEmpty(request.Scenario) ||
                string.IsNullOrEmpty(request.UserRole) || string.IsNullOrEmpty(request.AiRole))
            {
                return ServiceResult.Fail(ErrorCodes.BadRequest, "Scenario, UserRole, and AiRole are required.");
            }

            var systemChatMessage = _promptLoader.GetPrompt("Prompt/Exam/ConversationStartSystem.txt");
            if (string.IsNullOrEmpty(systemChatMessage))
            {
                _logger.LogError("Failed to load ConversationStartSystem.txt prompt");
                return ServiceResult.Fail(ErrorCodes.InternalServerError, "Failed to load conversation prompt.");
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
                return ServiceResult.Fail(ErrorCodes.OpenAIResponseUnsuccessful, errorMessage);
            }

            var content = completionResult.Choices.FirstOrDefault()?.Message.Content;
            if (string.IsNullOrEmpty(content))
            {
                return ServiceResult.Fail(ErrorCodes.QueryOpenAIFailed, errorMessage);
            }

            try
            {
                var conversationStart = JsonConvert.DeserializeObject<ConversationStartResponse>(content);
                if (conversationStart == null)
                {
                    return ServiceResult.Fail(ErrorCodes.QueryOpenAIFailed, errorMessage);
                }

                _logger.LogInformation("Generated conversation start for scenario: {Scenario}", request.Scenario);
                return ServiceResult.Ok(conversationStart);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to deserialize conversation start response");
                return ServiceResult.Fail(ErrorCodes.QueryOpenAIFailed, errorMessage);
            }
        }

        public async Task<ServiceResult> RespondToConversationAsync(ConversationResponseRequest request)
        {
            if (request == null || string.IsNullOrEmpty(request.UserMessage) || request.ConversationContext == null)
            {
                return ServiceResult.Fail(ErrorCodes.BadRequest, "Invalid conversation request.");
            }

            var userId = _userService.GetUserId();

            // Credit gate: each conversation turn costs credits
            if (!string.IsNullOrEmpty(userId) &&
                !await _creditService.ConsumeCreditsAsync(userId, CreditCosts.ConversationResponse, "conversation_response", "Conversation turn"))
            {
                return ServiceResult.Fail(ErrorCodes.Forbidden, $"Insufficient credits. Required: {CreditCosts.ConversationResponse}.");
            }

            var user = await _dbContext.Users.SingleOrDefaultAsync(x => x.Id == userId);
            if (user == null)
            {
                return ServiceResult.Fail(ErrorCodes.Forbidden, "User not found.");
            }

            var (response, error) = await _promptService.GetCompletionAsync<ConversationResponseResponse>(
                "Prompt/Exam/ConversationResponseSystem.txt",
                JsonConvert.SerializeObject(request));

            if (response == null)
            {
                _logger.LogError("Failed to generate conversation response: {Error}", error);
                return ServiceResult.Fail(ErrorCodes.QueryOpenAIFailed,
                    "Query failed, OpenAI could not generate the conversation response.");
            }

            _logger.LogInformation("Generated conversation response for turn {Turn}", request.ConversationContext.TurnNumber);
            return ServiceResult.Ok(response);
        }

        public async Task<ServiceResult> TranscribeAudioAsync(Stream audioStream, string fileName)
        {
            var userId = _userService.GetUserId();
            var user = await _dbContext.Users.SingleOrDefaultAsync(x => x.Id == userId);
            if (user == null)
            {
                return ServiceResult.Fail(ErrorCodes.Forbidden, "User not found.");
            }

            if (!await _creditService.ConsumeCreditsAsync(userId!, CreditCosts.AudioTranscription, "audio_transcription", "Whisper audio transcription"))
            {
                return ServiceResult.Fail(ErrorCodes.Forbidden, $"Insufficient credits. Required: {CreditCosts.AudioTranscription}.");
            }

            var (transcription, error) = await _promptService.TranscribeAudioAsync(audioStream, fileName);

            if (transcription == null)
            {
                _logger.LogError("Whisper transcription failed: {Error}", error);
                return ServiceResult.Fail(ErrorCodes.QueryOpenAIFailed, "Failed to transcribe audio.");
            }

            _logger.LogInformation("Whisper transcription successful");
            return ServiceResult.Ok(new WhisperTranscriptionResponse { Text = transcription.Trim() });
        }

        public async Task<ServiceResult> EvaluateSentenceAsync(SentenceTestUserResponse userResponse)
        {
            if (userResponse == null || string.IsNullOrEmpty(userResponse.UserSentence) ||
                string.IsNullOrEmpty(userResponse.Word) || string.IsNullOrEmpty(userResponse.Prompt))
            {
                return ServiceResult.Fail(ErrorCodes.BadRequest, "Input cannot be null or empty.");
            }

            var userId = _userService.GetUserId();
            if (!string.IsNullOrEmpty(userId) &&
                !await _creditService.ConsumeCreditsAsync(userId, CreditCosts.SentenceEvaluation, "sentence_evaluation", "Sentence AI evaluation"))
            {
                return ServiceResult.Fail(ErrorCodes.Forbidden, $"Insufficient credits. Required: {CreditCosts.SentenceEvaluation}.");
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
                return ServiceResult.Fail(ErrorCodes.QueryOpenAIFailed,
                    "Query failed, OpenAI could not generate the sentence evaluation.");
            }

            _logger.LogInformation("Generated sentence evaluation for word: {Word}", userResponse.Word);
            return ServiceResult.Ok(evaluation);
        }

        /// <summary>
        /// Picks a random WordId from the user's saved words at the database level
        /// (COUNT + OFFSET) instead of loading the whole collection into memory.
        /// </summary>
        private async Task<string?> GetRandomUserWordIdAsync(string userId)
        {
            var query = _dbContext.UserWords.Where(uw => uw.UserId == userId);
            var count = await query.CountAsync();
            if (count == 0)
            {
                return null;
            }

            var skip = Random.Shared.Next(count);
            return await query
                .OrderBy(uw => uw.WordId)
                .Skip(skip)
                .Select(uw => uw.WordId)
                .FirstOrDefaultAsync();
        }
    }
}
