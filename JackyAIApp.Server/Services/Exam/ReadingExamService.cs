using JackyAIApp.Server.Common;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.DTO;
using JackyAIApp.Server.Services.OpenAI;
using Microsoft.EntityFrameworkCore;
using SQL = JackyAIApp.Server.Data.Models.SQL;

namespace JackyAIApp.Server.Services.Exam
{
    /// <summary>
    /// Passage-level reading comprehension exercises (a short passage + 3-4 MCQ comprehension
    /// questions). Not word-scoped, shared bank — generated live on demand and persisted for reuse,
    /// same growth-threshold pattern as <see cref="IListeningResponseExamService"/>. A generated
    /// passage is only kept once it passes <see cref="ReadingPassageValidator"/>.
    /// </summary>
    public interface IReadingExamService
    {
        Task<ServiceResult<ReadingPassageResponse>> GenerateAsync();
    }

    public class ReadingExamService(
        ILogger<ReadingExamService> logger,
        AzureSQLDBContext dbContext,
        IOpenAIPromptService promptService,
        IExamBankPicker bankPicker) : IReadingExamService
    {
        private readonly ILogger<ReadingExamService> _logger = logger;
        private readonly AzureSQLDBContext _dbContext = dbContext;
        private readonly IOpenAIPromptService _promptService = promptService;
        private readonly IExamBankPicker _bankPicker = bankPicker;

        // Same growth-threshold/no-repeat pattern as listening response — see the comment above
        // ListeningResponseExamService.ListeningResponseBankGrowthThreshold and ExamBankPicker.NoRepeatWindow.
        private const int ReadingBankGrowthThreshold = 20;
        private const string ReadingFeature = "exam.reading";

        private static readonly string[] ReadingTopics =
        [
            "daily life", "travel", "workplace", "technology", "environment", "health",
        ];

        private static readonly (string User, string Assistant) ReadingFewShotExample = (
            User: "travel",
            Assistant: AiJson.Serialize(new ReadingGenerationResult
            {
                Title = "A Weekend Trip to the Mountains",
                PassageText = "Last weekend, Maria decided to escape the noise of the city and drive up to the mountains with two friends. " +
                    "They left early on Saturday morning, hoping to reach the trail before it got too crowded. The weather forecast had " +
                    "predicted rain, so Maria packed extra layers and a waterproof jacket just in case. When they arrived, the sky was " +
                    "still clear, and they spent the first few hours hiking along a quiet trail lined with pine trees. Around noon, dark " +
                    "clouds began to gather, and by the time they reached the summit, a light drizzle had started. Rather than turning " +
                    "back immediately, they sheltered under a rocky outcrop and ate lunch while waiting for the rain to pass. By the time " +
                    "they returned to the car in the late afternoon, they were tired but glad they had come prepared.",
                Questions =
                [
                    new ReadingQuestionGen
                    {
                        QuestionText = "What is this passage mainly about?",
                        Options =
                        [
                            new() { Text = "A group's hiking trip that was interrupted by unexpected rain.", IsCorrect = true, Explanation = "正確。文章主要描述三人一起健行途中遇到下雨的過程。" },
                            new() { Text = "A city that suffers from constant noise pollution.", IsCorrect = false, Explanation = "錯誤。文章只在開頭提到逃離城市喧囂，並非文章主旨。" },
                            new() { Text = "A guide explaining how to choose hiking equipment.", IsCorrect = false, Explanation = "錯誤。文章沒有教學裝備挑選的內容。" },
                        ],
                    },
                    new ReadingQuestionGen
                    {
                        QuestionText = "Why did Maria pack extra layers and a waterproof jacket?",
                        Options =
                        [
                            new() { Text = "Because the weather forecast predicted rain.", IsCorrect = true, Explanation = "正確。文章明確提到氣象預報說會下雨，所以她多帶了衣物。" },
                            new() { Text = "Because her friends asked her to carry the group's gear.", IsCorrect = false, Explanation = "錯誤。文章沒有提到朋友要求她攜帶裝備。" },
                            new() { Text = "Because she planned to camp overnight in the mountains.", IsCorrect = false, Explanation = "錯誤。文章沒有提到過夜露營的計畫。" },
                        ],
                    },
                    new ReadingQuestionGen
                    {
                        QuestionText = "What can be inferred about how the group felt when they got back to the car?",
                        Options =
                        [
                            new() { Text = "Exhausted but satisfied with their decision to prepare well.", IsCorrect = true, Explanation = "正確。文章說他們「tired but glad they had come prepared」，暗示雖然累但慶幸有準備。" },
                            new() { Text = "Angry that the trip had been ruined by the weather.", IsCorrect = false, Explanation = "錯誤。文章語氣是慶幸而非生氣。" },
                            new() { Text = "Disappointed that they never reached the summit.", IsCorrect = false, Explanation = "錯誤。文章提到他們確實抵達了山頂（summit）。" },
                        ],
                    },
                    new ReadingQuestionGen
                    {
                        QuestionText = "In the passage, what does the word \"drizzle\" most likely mean?",
                        Options =
                        [
                            new() { Text = "A light rain.", IsCorrect = true, Explanation = "正確。前後文提到烏雲聚集後開始下起 drizzle，接著他們在岩石下躲雨，符合「小雨」的語境。" },
                            new() { Text = "A strong wind.", IsCorrect = false, Explanation = "錯誤。文中描述的是降雨相關的天氣現象，與風無關。" },
                            new() { Text = "A sudden temperature drop.", IsCorrect = false, Explanation = "錯誤。文章沒有提到氣溫驟降。" },
                        ],
                    },
                ],
            })
        );

        /// <summary>
        /// Asks for a passage and returns it only if it passes <see cref="ReadingPassageValidator"/>,
        /// retrying once. The retry is here because the failure this guards against — the model
        /// answering in the wrong language — is a sampling slip rather than a settled refusal, and
        /// GPT-4.1-nano on its second attempt usually complies. Two attempts and no more: past
        /// that the fallback to an existing passage is both faster and cheaper than a third roll.
        /// </summary>
        private async Task<(ReadingGenerationResult? Dto, string? Rejection)> GenerateValidReadingPassageAsync(string topic)
        {
            const int maxAttempts = 2;
            string? lastRejection = null;

            for (var attempt = 1; attempt <= maxAttempts; attempt++)
            {
                var (dto, error) = await _promptService.GetCompletionAsync<ReadingGenerationResult>(
                    "Prompt/Exam/ReadingSystem.txt", topic,
                    model: NewModels.GPT_6_LUNA, examples: new[] { ReadingFewShotExample },
                    feature: ReadingFeature);

                if (dto == null)
                {
                    lastRejection = error ?? "no response";
                }
                else
                {
                    lastRejection = ReadingPassageValidator.Validate(dto);
                    if (lastRejection == null)
                    {
                        return (dto, null);
                    }
                }

                _logger.LogWarning(
                    "Reading passage attempt {Attempt}/{MaxAttempts} rejected for topic {Topic}: {Reason}",
                    attempt, maxAttempts, topic, lastRejection);
            }

            return (null, lastRejection);
        }

        public async Task<ServiceResult<ReadingPassageResponse>> GenerateAsync()
        {
            var pool = await _bankPicker.OpenAsync(
                _dbContext.ReadingPassages, q => q.Include(p => p.Questions).ThenInclude(qq => qq.Options), a => a.ReadingPassageId);
            var shouldGenerate = pool.UnseenCount < ReadingBankGrowthThreshold || Random.Shared.Next(5) == 0;

            if (!shouldGenerate && pool.UnseenCount > 0)
            {
                var existing = await pool.PickAsync();
                return ServiceResult.Ok(ToReadingDto(existing));
            }

            var topic = ReadingTopics[Random.Shared.Next(ReadingTopics.Length)];
            var (dto, rejection) = await GenerateValidReadingPassageAsync(topic);

            if (dto == null)
            {
                _logger.LogError("Failed to generate a usable reading passage for topic {Topic}: {Reason}", topic, rejection);

                // Fall back to an existing one rather than a hard failure, if the bank has any.
                // A rejected passage must never be persisted: the bank is shared and reused, so
                // saving one bad passage serves it to everyone for as long as it is in there.
                if (pool.TotalCount > 0)
                {
                    var fallback = await pool.PickAsync();
                    return ServiceResult.Ok(ToReadingDto(fallback));
                }

                return ServiceResult.Fail(ErrorCodes.QueryOpenAIFailed,
                    "Query failed, OpenAI could not generate the passage.");
            }

            var passageId = Guid.NewGuid().ToString();
            var newPassage = new SQL.ReadingPassage
            {
                Id = passageId,
                Title = dto.Title,
                PassageText = dto.PassageText,
                CreatedAt = DateTime.UtcNow,
                Questions = dto.Questions.Select(q =>
                {
                    var questionId = Guid.NewGuid().ToString();
                    return new SQL.ReadingQuestion
                    {
                        Id = questionId,
                        QuestionText = q.QuestionText,
                        PassageId = passageId,
                        Options = q.Options.Select(o => new SQL.ReadingQuestionOption
                        {
                            Id = Guid.NewGuid().ToString(),
                            Text = o.Text,
                            IsCorrect = o.IsCorrect,
                            Explanation = o.Explanation,
                            QuestionId = questionId,
                        }).ToList(),
                    };
                }).ToList(),
            };

            _dbContext.ReadingPassages.Add(newPassage);
            await _dbContext.SaveChangesAsync();

            return ServiceResult.Ok(ToReadingDto(newPassage));
        }

        private static ReadingPassageResponse ToReadingDto(SQL.ReadingPassage p) => new()
        {
            Id = p.Id,
            Title = p.Title,
            PassageText = p.PassageText,
            Questions = p.Questions.Select(q => new ReadingQuestionDto
            {
                Id = q.Id,
                QuestionText = q.QuestionText,
                Options = q.Options.Select(o => new ReadingQuestionOptionDto
                {
                    Id = o.Id,
                    Text = o.Text,
                    IsCorrect = o.IsCorrect,
                    Explanation = o.Explanation,
                }).ToList(),
            }).ToList(),
        };
    }
}
