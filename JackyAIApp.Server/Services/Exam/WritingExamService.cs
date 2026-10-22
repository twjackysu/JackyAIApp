using JackyAIApp.Server.Common;
using JackyAIApp.Server.DTO;
using JackyAIApp.Server.Services.OpenAI;

namespace JackyAIApp.Server.Services.Exam
{
    /// <summary>Open-ended writing practice: a topic with some of the user's own words, and its AI grading.</summary>
    public interface IWritingExamService
    {
        /// <summary>
        /// Serves an open-ended writing prompt: a topic from a fixed rotation plus up to 5 of the
        /// calling user's own saved words (empty if the user has none — open-topic writing, not an
        /// error). Both plain DB queries, no AI call, so unlike every other exam-type generation
        /// endpoint this is deliberately not usage-limit-gated.
        /// </summary>
        Task<ServiceResult<WritingPromptResponse>> GeneratePromptAsync();

        /// <summary>
        /// Grades an open-ended writing submission against its topic/required words — same one-shot
        /// grading shape as <see cref="IWordExamService.GradeTranslationAsync"/>/<see cref="IWordExamService.EvaluateSentenceAsync"/>.
        /// </summary>
        Task<ServiceResult<WritingGradingResponse>> GradeAsync(WritingSubmissionRequest request);
    }

    public class WritingExamService(
        ILogger<WritingExamService> logger,
        ICurrentUser currentUser,
        IOpenAIPromptService promptService,
        IExamWordPicker wordPicker,
        IUserAccountService accounts) : IWritingExamService
    {
        private readonly ILogger<WritingExamService> _logger = logger;
        private readonly ICurrentUser _currentUser = currentUser;
        private readonly IOpenAIPromptService _promptService = promptService;
        private readonly IExamWordPicker _wordPicker = wordPicker;
        private readonly IUserAccountService _accounts = accounts;

        private static readonly string[] WritingTopics =
        [
            "a typical day in your life",
            "a memorable experience you had",
            "a skill you want to learn and why",
            "your opinion on something you care about",
        ];

        private const string WritingFeature = "exam.writing_evaluation";

        /// <summary>
        /// Serves an open-ended writing prompt: a topic from a fixed rotation plus up to 5 of the
        /// calling user's own saved words. Both plain DB queries — no AI call, so this is
        /// deliberately not usage-limit-gated (unlike every other GET api/exam/* generation endpoint).
        /// </summary>
        public async Task<ServiceResult<WritingPromptResponse>> GeneratePromptAsync()
        {
            var userId = _currentUser.RequireId();
            if (!await _accounts.ExistsAsync())
            {
                return UserAccountFailures.Missing;
            }

            var topic = WritingTopics[Random.Shared.Next(WritingTopics.Length)];
            var words = await _wordPicker.PickSavedWordsAsync(userId, 5);

            return ServiceResult.Ok(new WritingPromptResponse
            {
                Topic = topic,
                RequiredWords = words.Select(w => new WritingPromptWordDto
                {
                    WordId = w.WordId,
                    WordText = w.Word.WordText,
                }).ToList(),
            });
        }

        public async Task<ServiceResult<WritingGradingResponse>> GradeAsync(WritingSubmissionRequest request)
        {
            if (request == null || string.IsNullOrEmpty(request.Topic) || string.IsNullOrEmpty(request.Submission))
            {
                return ServiceResult.Fail(ErrorCodes.BadRequest, "Input cannot be null or empty.");
            }

            var example = (
                User: AiJson.Serialize(new WritingSubmissionRequest
                {
                    Topic = "a memorable experience you had",
                    RequiredWords = new List<string> { "adventure", "journey", "unexpected" },
                    Submission = "Last summer I had an unexpected adventure when my flight got cancelled. " +
                        "It turned into a journey I will never forget, full of new friends and unplanned stops."
                }),
                Assistant: AiJson.Serialize(new WritingGradingResponse
                {
                    Score = 88,
                    GrammarFeedback = "文法大致正確，時態一致，僅有少數用詞可以更道地。",
                    ContentFeedback = "內容切題，段落結構清楚，有開頭、轉折與結尾。",
                    VocabularyFeedback = "三個指定單字都自然地融入句子中，語意恰當。",
                    WordsUsed = new List<WritingWordUsageDto>
                    {
                        new() { Word = "adventure", Used = true },
                        new() { Word = "journey", Used = true },
                        new() { Word = "unexpected", Used = true },
                    },
                    Suggestions = new List<string> { "可以多加一些具體細節，例如地點或時間，讓故事更生動。" }
                })
            );

            var (grading, error) = await _promptService.GetCompletionAsync<WritingGradingResponse>(
                "Prompt/Exam/WritingEvaluationSystem.txt",
                AiJson.Serialize(request),
                model: NewModels.GPT_6_LUNA, examples: new[] { example },
                feature: WritingFeature);

            if (grading == null)
            {
                _logger.LogError("Failed to generate writing evaluation: {Error}", error);
                return ServiceResult.Fail(ErrorCodes.QueryOpenAIFailed,
                    "Query failed, OpenAI could not generate the writing evaluation.");
            }

            _logger.LogInformation("Generated writing evaluation for topic: {Topic}", request.Topic);
            return ServiceResult.Ok(grading);
        }
    }
}
