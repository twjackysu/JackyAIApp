using JackyAIApp.Server.Common;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.DTO;
using JackyAIApp.Server.Services.OpenAI;
using Newtonsoft.Json;
using SQL = JackyAIApp.Server.Data.Models.SQL;

namespace JackyAIApp.Server.Services.Exam.Generators
{
    public class ClozeExamGenerator(
        ILogger<ClozeExamGenerator> logger,
        AzureSQLDBContext dbContext,
        IOpenAIPromptService promptService) : IWordExamGenerator
    {
        private readonly ILogger<ClozeExamGenerator> _logger = logger;
        private readonly AzureSQLDBContext _dbContext = dbContext;
        private readonly IOpenAIPromptService _promptService = promptService;

        public ExamType Type => ExamType.Cloze;

        public async Task<ExamResult> GenerateAsync(SQL.Word word)
        {
            // Reuse a stored test once enough have accumulated.
            if (word.ClozeTests != null && word.ClozeTests.Count > 3)
            {
                var randomTest = word.ClozeTests.ElementAt(Random.Shared.Next(word.ClozeTests.Count));
                return ExamResult.Ok(ToDto(randomTest));
            }

            var example = (
                User: "coast",
                Assistant: JsonConvert.SerializeObject(new ClozeTest
                {
                    Question = "The lighthouse was built on the __________ to help guide ships safely to shore.",
                    Answer = "coast",
                    Options = new List<string> { "coast", "cost", "cast", "post" }
                })
            );

            var (dto, error) = await _promptService.GetCompletionAsync<ClozeTest>(
                "Prompt/Exam/ClozeSystem.txt", word.WordText, examples: new[] { example });

            if (dto == null)
            {
                _logger.LogError("Failed to generate cloze test for word {Word}: {Error}", word.WordText, error);
                return ExamResult.Fail(ErrorCodes.QueryOpenAIFailed,
                    "Query failed, OpenAI could not generate the corresponding cloze test.");
            }

            _logger.LogInformation("Generated cloze test for word: {Word}", word.WordText);

            // Randomize options order
            var optionsList = dto.Options.OrderBy(_ => Random.Shared.NextDouble()).ToList();

            var clozeTestId = Guid.NewGuid().ToString();
            var newClozeTest = new SQL.ClozeTest
            {
                Id = clozeTestId,
                Question = dto.Question,
                Answer = dto.Answer,
                WordId = word.Id,
                Word = word
            };
            foreach (var option in optionsList)
            {
                newClozeTest.Options.Add(new SQL.ClozeTestOption
                {
                    Id = Guid.NewGuid().ToString(),
                    OptionText = option,
                    ClozeTestId = clozeTestId,
                    ClozeTest = newClozeTest
                });
            }

            bool similarTestExists = word.ClozeTests?.Any(x => x.Question == newClozeTest.Question) ?? false;
            if (!similarTestExists)
            {
                word.ClozeTests ??= new List<SQL.ClozeTest>();
                word.ClozeTests.Add(newClozeTest);
                await _dbContext.SaveChangesAsync();
                _logger.LogInformation("ClozeTest added to DB for word: {Word}", word.WordText);
            }

            return ExamResult.Ok(ToDto(newClozeTest));
        }

        private static ClozeTest ToDto(SQL.ClozeTest sql) => new()
        {
            Question = sql.Question,
            Answer = sql.Answer,
            Options = sql.Options.Select(o => o.OptionText).ToList()
        };
    }
}
