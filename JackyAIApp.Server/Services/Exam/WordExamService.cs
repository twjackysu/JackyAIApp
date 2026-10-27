using JackyAIApp.Server.Common;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.DTO;
using JackyAIApp.Server.Services.OpenAI;
using Microsoft.EntityFrameworkCore;

namespace JackyAIApp.Server.Services.Exam
{
    /// <summary>
    /// The vocabulary-based exams — cloze, translation, sentence formation — and the AI grading of
    /// a translation or a sentence. A question is about one of the user's words
    /// (<see cref="IExamWordPicker"/>) and comes from that type's <see cref="IWordExamGenerator{TQuestion}"/>.
    /// </summary>
    public interface IWordExamService
    {
        /// <summary>A cloze question about one of the current user's words (generated, or a stored one).</summary>
        Task<ServiceResult<ClozeTestResponse>> GenerateClozeAsync();

        /// <summary>A translation question about one of the current user's words (generated, or a stored one).</summary>
        Task<ServiceResult<TranslationTestResponse>> GenerateTranslationAsync();

        /// <summary>A sentence-formation question about one of the current user's words (generated, or a stored one).</summary>
        Task<ServiceResult<SentenceTestResponse>> GenerateSentenceAsync();

        Task<ServiceResult<TranslationQualityGradingAssistantResponse>> GradeTranslationAsync(TranslationTestUserResponse userResponse);

        Task<ServiceResult<SentenceTestGradingResponse>> EvaluateSentenceAsync(SentenceTestUserResponse userResponse);
    }

    public class WordExamService(
        ILogger<WordExamService> logger,
        AzureSQLDBContext dbContext,
        ICurrentUser currentUser,
        IOpenAIPromptService promptService,
        IWordExamGenerator<ClozeTestResponse> cloze,
        IWordExamGenerator<TranslationTestResponse> translation,
        IWordExamGenerator<SentenceTestResponse> sentence,
        IExamWordPicker wordPicker,
        IUserAccountService accounts) : IWordExamService
    {
        private readonly ILogger<WordExamService> _logger = logger;
        private readonly AzureSQLDBContext _dbContext = dbContext;
        private readonly ICurrentUser _currentUser = currentUser;
        private readonly IOpenAIPromptService _promptService = promptService;
        private readonly IExamWordPicker _wordPicker = wordPicker;
        private readonly IUserAccountService _accounts = accounts;

        public Task<ServiceResult<ClozeTestResponse>> GenerateClozeAsync() => GenerateAsync(cloze);

        public Task<ServiceResult<TranslationTestResponse>> GenerateTranslationAsync() => GenerateAsync(translation);

        public Task<ServiceResult<SentenceTestResponse>> GenerateSentenceAsync() => GenerateAsync(sentence);

        /// <summary>Picks one of the user's words (<see cref="IExamWordPicker"/>) and asks
        /// <paramref name="generator"/> for a question about it.</summary>
        private async Task<ServiceResult<TQuestion>> GenerateAsync<TQuestion>(IWordExamGenerator<TQuestion> generator)
        {
            var userId = _currentUser.RequireId();
            if (!await _accounts.ExistsAsync())
            {
                return UserAccountFailures.Missing;
            }

            var randomWordId = await _wordPicker.PickWordIdAsync(userId);
            if (randomWordId == null)
            {
                return ExamWordPicker.NoWords;
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

            return await generator.GenerateAsync(word);
        }

        public async Task<ServiceResult<TranslationQualityGradingAssistantResponse>> GradeTranslationAsync(TranslationTestUserResponse userResponse)
        {
            if (userResponse == null || string.IsNullOrEmpty(userResponse.Translation) ||
                string.IsNullOrEmpty(userResponse.ExaminationQuestion) || string.IsNullOrEmpty(userResponse.UnfamiliarWords))
            {
                return ServiceResult.Fail(ErrorCodes.BadRequest, "Input cannot be null or empty.");
            }

            if (ExamInputLimits.TooLong(userResponse, ExamInputLimits.MaxShortAnswerChars) is { } tooLong)
                return tooLong;

            var example = (
                User: AiJson.Serialize(new TranslationTestUserResponse
                {
                    UnfamiliarWords = "attentively",
                    ExaminationQuestion = "他專注地閱讀著一本有趣的書。",
                    Translation = "He was reading an interesting book attentively."
                }),
                Assistant: AiJson.Serialize(new TranslationQualityGradingAssistantResponse
                {
                    Grade = "A",
                    Reason = "這個翻譯完整保留了原文的所有語意，語法和拼寫完全正確，語句流暢且符合英文的表達習慣。",
                    CorrectedTranslation = "He was reading an interesting book attentively.",
                    WordUsed = true
                })
            );

            var (grading, error) = await _promptService.GetCompletionAsync<TranslationQualityGradingAssistantResponse>(
                "Prompt/Exam/TranslationQualityGradingSystem.txt",
                AiJson.Serialize(userResponse),
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

        public async Task<ServiceResult<SentenceTestGradingResponse>> EvaluateSentenceAsync(SentenceTestUserResponse userResponse)
        {
            if (userResponse == null || string.IsNullOrEmpty(userResponse.UserSentence) ||
                string.IsNullOrEmpty(userResponse.Word) || string.IsNullOrEmpty(userResponse.Prompt))
            {
                return ServiceResult.Fail(ErrorCodes.BadRequest, "Input cannot be null or empty.");
            }

            if (ExamInputLimits.TooLong(userResponse, ExamInputLimits.MaxShortAnswerChars) is { } tooLong)
                return tooLong;

            var example = (
                User: AiJson.Serialize(new SentenceTestUserResponse
                {
                    Word = "adventure",
                    Prompt = "Create a sentence using the word 'adventure' in the context of travel.",
                    Context = "Travel and exploration",
                    UserSentence = "My adventure to Japan was incredible and full of surprises.",
                    DifficultyLevel = 3,
                    GrammarPattern = "Past tense narrative"
                }),
                Assistant: AiJson.Serialize(new SentenceTestGradingResponse
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
                AiJson.Serialize(userResponse),
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
    }
}
