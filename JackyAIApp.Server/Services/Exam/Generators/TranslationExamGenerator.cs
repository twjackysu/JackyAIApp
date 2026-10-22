using JackyAIApp.Server.Common;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.DTO;
using JackyAIApp.Server.Services.OpenAI;
using SQL = JackyAIApp.Server.Data.Models.SQL;

namespace JackyAIApp.Server.Services.Exam.Generators
{
    public class TranslationExamGenerator(
        ILogger<TranslationExamGenerator> logger,
        AzureSQLDBContext dbContext,
        IOpenAIPromptService promptService) : IWordExamGenerator
    {
        private readonly ILogger<TranslationExamGenerator> _logger = logger;
        private readonly AzureSQLDBContext _dbContext = dbContext;
        private readonly IOpenAIPromptService _promptService = promptService;

        public ExamType Type => ExamType.Translation;

        public async Task<ServiceResult<object>> GenerateAsync(SQL.Word word)
        {
            if (word.TranslationTests != null && word.TranslationTests.Count > 3)
            {
                var randomTest = word.TranslationTests.ElementAt(Random.Shared.Next(word.TranslationTests.Count));
                return ServiceResult.Ok<object>(ToDto(randomTest, word.WordText, word.Id));
            }

            var example = (
                User: "appetizer",
                Assistant: AiJson.Serialize(new TranslationTest
                {
                    English = "Soup is often a good choice for an appetizer.",
                    Chinese = "湯經常是開胃菜的好選擇。"
                })
            );

            var (dto, error) = await _promptService.GetCompletionAsync<TranslationTest>(
                "Prompt/Exam/TranslationSystem.txt", word.WordText, model: NewModels.GPT_6_LUNA, examples: new[] { example });

            if (dto == null)
            {
                _logger.LogError("Failed to generate translation test for word {Word}: {Error}", word.WordText, error);
                return ServiceResult.Fail(ErrorCodes.QueryOpenAIFailed,
                    "Query failed, OpenAI could not generate the corresponding translation test.");
            }

            _logger.LogInformation("Generated translation test for word: {Word}", word.WordText);

            var translationTest = new SQL.TranslationTest
            {
                Id = Guid.NewGuid().ToString(),
                English = dto.English,
                Chinese = dto.Chinese,
                WordId = word.Id,
                Word = word
            };

            bool similarTestExists = word.TranslationTests?.Any(x => x.Chinese == translationTest.Chinese) ?? false;
            if (!similarTestExists)
            {
                word.TranslationTests ??= new List<SQL.TranslationTest>();
                word.TranslationTests.Add(translationTest);
                await _dbContext.SaveChangesAsync();
                _logger.LogInformation("TranslationTest added to DB for word: {Word}", word.WordText);
            }

            return ServiceResult.Ok<object>(ToDto(translationTest, word.WordText, word.Id));
        }

        private static TranslationTestResponse ToDto(SQL.TranslationTest sql, string wordText, string wordId) => new()
        {
            Word = wordText,
            Chinese = sql.Chinese,
            English = sql.English,
            WordId = wordId
        };
    }
}
