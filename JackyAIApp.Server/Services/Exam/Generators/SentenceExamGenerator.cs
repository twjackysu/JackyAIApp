using JackyAIApp.Server.Common;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.DTO;
using JackyAIApp.Server.Services.OpenAI;
using SQL = JackyAIApp.Server.Data.Models.SQL;

namespace JackyAIApp.Server.Services.Exam.Generators
{
    public class SentenceExamGenerator(
        ILogger<SentenceExamGenerator> logger,
        AzureSQLDBContext dbContext,
        IOpenAIPromptService promptService) : IWordExamGenerator
    {
        private readonly ILogger<SentenceExamGenerator> _logger = logger;
        private readonly AzureSQLDBContext _dbContext = dbContext;
        private readonly IOpenAIPromptService _promptService = promptService;

        public ExamType Type => ExamType.Sentence;

        public async Task<ServiceResult<object>> GenerateAsync(SQL.Word word)
        {
            if (word.SentenceTests != null && word.SentenceTests.Count > 3)
            {
                var randomTest = word.SentenceTests.ElementAt(Random.Shared.Next(word.SentenceTests.Count));
                return ServiceResult.Ok<object>(ToDto(randomTest, word.WordText, word.Id));
            }

            var example = (
                User: "adventure",
                Assistant: AiJson.Serialize(new SentenceTest
                {
                    Prompt = "Create a sentence using the word 'adventure' in the context of travel.",
                    SampleAnswer = "Last summer, I went on an amazing adventure to explore the mountains.",
                    Context = "Travel and exploration",
                    DifficultyLevel = 3,
                    GrammarPattern = "Past tense narrative"
                })
            );

            var (dto, error) = await _promptService.GetCompletionAsync<SentenceTest>(
                "Prompt/Exam/SentenceSystem.txt", word.WordText, model: NewModels.GPT_6_LUNA, examples: new[] { example });

            if (dto == null)
            {
                _logger.LogError("Failed to generate sentence test for word {Word}: {Error}", word.WordText, error);
                return ServiceResult.Fail(ErrorCodes.QueryOpenAIFailed,
                    "Query failed, OpenAI could not generate the corresponding sentence test.");
            }

            _logger.LogInformation("Generated sentence test for word: {Word}", word.WordText);

            var sentenceTest = new SQL.SentenceTest
            {
                Id = Guid.NewGuid().ToString(),
                Prompt = dto.Prompt,
                SampleAnswer = dto.SampleAnswer,
                Context = dto.Context,
                DifficultyLevel = dto.DifficultyLevel,
                GrammarPattern = dto.GrammarPattern,
                WordId = word.Id,
                Word = word
            };

            bool similarTestExists = word.SentenceTests?.Any(x => x.Prompt == sentenceTest.Prompt) ?? false;
            if (!similarTestExists)
            {
                word.SentenceTests ??= new List<SQL.SentenceTest>();
                word.SentenceTests.Add(sentenceTest);
                await _dbContext.SaveChangesAsync();
                _logger.LogInformation("SentenceTest added to DB for word: {Word}", word.WordText);
            }

            return ServiceResult.Ok<object>(ToDto(sentenceTest, word.WordText, word.Id));
        }

        private static SentenceTestResponse ToDto(SQL.SentenceTest sql, string wordText, string wordId) => new()
        {
            Word = wordText,
            Prompt = sql.Prompt,
            SampleAnswer = sql.SampleAnswer,
            Context = sql.Context,
            DifficultyLevel = sql.DifficultyLevel,
            GrammarPattern = sql.GrammarPattern,
            WordId = wordId
        };
    }
}
