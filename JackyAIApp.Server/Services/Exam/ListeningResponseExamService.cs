using JackyAIApp.Server.Common;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.DTO;
using JackyAIApp.Server.Services.OpenAI;
using Microsoft.EntityFrameworkCore;
using SQL = JackyAIApp.Server.Data.Models.SQL;

namespace JackyAIApp.Server.Services.Exam
{
    /// <summary>
    /// TOEIC Part 2 style "question/statement + 3 spoken responses" questions. No image, so —
    /// unlike Part 1 — generated live on demand and persisted for reuse: while the user's unseen
    /// pool is small, always generates a fresh one via AI; once it has grown, mostly reuses a
    /// random existing entry with an occasional AI-generated top-up.
    /// </summary>
    public interface IListeningResponseExamService
    {
        Task<ServiceResult<ListeningResponseTestResponse>> GenerateAsync();

        /// <summary>Looks up a question's spoken prompt text, for the unbilled audio playback endpoint.</summary>
        Task<ServiceResult<string>> GetPromptTextAsync(string testId);

        /// <summary>Looks up a response option's spoken text, for the unbilled audio playback endpoint.</summary>
        Task<ServiceResult<string>> GetOptionTextAsync(string optionId);
    }

    public class ListeningResponseExamService(
        ILogger<ListeningResponseExamService> logger,
        AzureSQLDBContext dbContext,
        IOpenAIPromptService promptService,
        IExamBankPicker bankPicker) : IListeningResponseExamService
    {
        private readonly ILogger<ListeningResponseExamService> _logger = logger;
        private readonly AzureSQLDBContext _dbContext = dbContext;
        private readonly IOpenAIPromptService _promptService = promptService;
        private readonly IExamBankPicker _bankPicker = bankPicker;

        // Gate generation on the user's *unseen* pool, not the raw bank size — otherwise a user who
        // burns through a small-but-growing bank faster than it fills would start seeing repeats.
        // While unseen < threshold, always generate a fresh Part 2 question; once there's enough
        // unseen backlog, mostly reuse a random one (cheap) with an occasional AI top-up.
        private const int ListeningResponseBankGrowthThreshold = 20;
        private const string ListeningResponseFeature = "exam.listening_response";

        private static readonly (string User, string Assistant) ListeningResponseFewShotExample = (
            User: "scheduling",
            Assistant: AiJson.Serialize(new ListeningResponseGenerationResult
            {
                Prompt = "When is the quarterly report due?",
                Options =
                [
                    new ListeningResponseOptionGen { Text = "By the end of this Friday.", IsCorrect = true, Explanation = "正確。直接回答了「何時」，給出明確的截止時間。" },
                    new ListeningResponseOptionGen { Text = "In the third-floor conference room.", IsCorrect = false, Explanation = "錯誤。回答的是地點，答非所問（問題問的是時間）。" },
                    new ListeningResponseOptionGen { Text = "Yes, I reported it already.", IsCorrect = false, Explanation = "錯誤。像是回應「report」這個字的聯想，並未回答何時截止。" },
                ],
            })
        );

        public async Task<ServiceResult<ListeningResponseTestResponse>> GenerateAsync()
        {
            var pool = await _bankPicker.OpenAsync(
                _dbContext.ListeningResponseTests, q => q.Include(t => t.Options), a => a.ListeningResponseTestId);
            var shouldGenerate = pool.UnseenCount < ListeningResponseBankGrowthThreshold || Random.Shared.Next(5) == 0;

            if (!shouldGenerate && pool.UnseenCount > 0)
            {
                var existing = await pool.PickAsync();
                return ServiceResult.Ok(ToListeningResponseDto(existing));
            }

            var scenario = ListeningResponseScenarios[Random.Shared.Next(ListeningResponseScenarios.Length)];
            var (dto, error) = await _promptService.GetCompletionAsync<ListeningResponseGenerationResult>(
                "Prompt/Exam/ListeningResponseSystem.txt", scenario,
                model: NewModels.GPT_6_LUNA, examples: new[] { ListeningResponseFewShotExample },
                feature: ListeningResponseFeature);

            if (dto == null)
            {
                _logger.LogError("Failed to generate listening response question: {Error}", error);

                // Fall back to an existing one rather than a hard failure, if the bank has any.
                if (pool.TotalCount > 0)
                {
                    var fallback = await pool.PickAsync();
                    return ServiceResult.Ok(ToListeningResponseDto(fallback));
                }

                return ServiceResult.Fail(ErrorCodes.QueryOpenAIFailed,
                    "Query failed, OpenAI could not generate the question.");
            }
            if (dto.Options.Count != 3 || dto.Options.Count(o => o.IsCorrect) != 1)
            {
                _logger.LogError("Listening response generation returned malformed options ({Count} options, {CorrectCount} correct)",
                    dto.Options.Count, dto.Options.Count(o => o.IsCorrect));
                return ServiceResult.Fail(ErrorCodes.QueryOpenAIFailed,
                    "Generated question had an invalid option shape (expected 3 options, 1 correct).");
            }

            var testId = Guid.NewGuid().ToString();
            var newTest = new SQL.ListeningResponseTest
            {
                Id = testId,
                Prompt = dto.Prompt,
                CreatedAt = DateTime.UtcNow,
                Options = dto.Options.Select(o => new SQL.ListeningResponseOption
                {
                    Id = Guid.NewGuid().ToString(),
                    Text = o.Text,
                    IsCorrect = o.IsCorrect,
                    Explanation = o.Explanation,
                    ListeningResponseTestId = testId,
                }).ToList(),
            };

            _dbContext.ListeningResponseTests.Add(newTest);
            await _dbContext.SaveChangesAsync();

            return ServiceResult.Ok(ToListeningResponseDto(newTest));
        }

        private static readonly string[] ListeningResponseScenarios =
        [
            "scheduling", "office supplies", "travel arrangements", "meeting logistics",
            "deliveries and shipping", "IT support", "restaurant reservations", "job interviews",
            "client requests", "building maintenance",
        ];

        private static ListeningResponseTestResponse ToListeningResponseDto(SQL.ListeningResponseTest t) => new()
        {
            Id = t.Id,
            Prompt = t.Prompt,
            Options = t.Options.Select(o => new ListeningResponseOptionDto
            {
                Id = o.Id,
                Text = o.Text,
                IsCorrect = o.IsCorrect,
                Explanation = o.Explanation,
            }).ToList(),
        };

        public async Task<ServiceResult<string>> GetPromptTextAsync(string testId)
        {
            var test = await _dbContext.ListeningResponseTests.SingleOrDefaultAsync(t => t.Id == testId);
            return test == null
                ? ServiceResult.Fail(ErrorCodes.NotFound, "Listening response question not found.")
                : ServiceResult.Ok(test.Prompt);
        }

        public async Task<ServiceResult<string>> GetOptionTextAsync(string optionId)
        {
            var option = await _dbContext.ListeningResponseOptions.SingleOrDefaultAsync(o => o.Id == optionId);
            return option == null
                ? ServiceResult.Fail(ErrorCodes.NotFound, "Listening response option not found.")
                : ServiceResult.Ok(option.Text);
        }
    }
}
