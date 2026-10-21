using System.Text.Json;
using JackyAIApp.Server.Common;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.Data.Models.SQL;
using JackyAIApp.Server.DTO;
using JackyAIApp.Server.Services;
using JackyAIApp.Server.Services.DailyChallenge;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using Definition = JackyAIApp.Server.Data.Models.SQL.Definition;
using ExampleSentence = JackyAIApp.Server.Data.Models.SQL.ExampleSentence;
using Word = JackyAIApp.Server.Data.Models.SQL.Word;
using WordMeaning = JackyAIApp.Server.Data.Models.SQL.WordMeaning;

namespace JackyAIApp.Server.Tests.Services
{
    /// <summary>
    /// The day's questions are generated once and persisted (<see cref="DailyChallengeSet"/>), so a
    /// GET and the later submit grade against the same set. Before that, both regenerated from the
    /// current dictionary, and one word added in between shifted the generator's single random
    /// stream and changed every question's options and correct index.
    ///
    /// Real relational provider (SQLite) rather than InMemory: the concurrency test needs the primary
    /// key to actually reject a second insert, and EnsureCreated builds the same model the app uses.
    /// </summary>
    public class DailyChallengeServiceTests : IDisposable
    {
        private const string UserId = "daily-challenge-test-user";

        private readonly SqliteConnection _keepAliveConnection;
        private readonly DbContextOptions<AzureSQLDBContext> _options;

        public DailyChallengeServiceTests()
        {
            // Shared-cache in-memory database so a second context (the "other request" in the race
            // test) sees the same data; the keep-alive connection stops SQLite dropping it.
            var connectionString = $"Data Source=file:dailychallenge-{Guid.NewGuid():N}?mode=memory&cache=shared";
            _keepAliveConnection = new SqliteConnection(connectionString);
            _keepAliveConnection.Open();

            _options = new DbContextOptionsBuilder<AzureSQLDBContext>()
                .UseSqlite(connectionString)
                .Options;

            using var db = CreateContext();
            db.Database.EnsureCreated();
            db.Users.Add(new User { Id = UserId, LastUpdated = DateTime.UtcNow });
            db.SaveChanges();
        }

        public void Dispose() => _keepAliveConnection.Dispose();

        private AzureSQLDBContext CreateContext() => new(_options);

        private static DailyChallengeService CreateService(
            AzureSQLDBContext db, IDailyChallengeQuestionGenerator? generator = null)
        {
            var user = new Mock<ICurrentUser>();
            user.Setup(u => u.Id).Returns(UserId);
            return new DailyChallengeService(db, user.Object, generator ?? new DailyChallengeQuestionGenerator());
        }

        private async Task SeedWordsAsync(int count, int startIndex = 0)
        {
            await using var db = CreateContext();
            for (int i = startIndex; i < startIndex + count; i++)
                db.Words.Add(BuildWord(i));
            await db.SaveChangesAsync();
        }

        /// <summary>
        /// A word with a definition, an example sentence that contains it, and a synonym tag, so the
        /// generator can produce every question type from it. Ids are fixed (not Guids) so a run is
        /// reproducible.
        /// </summary>
        private static Word BuildWord(int i)
        {
            var wordId = $"w-{i:D4}";
            var meaningId = $"m-{i:D4}";
            return new Word
            {
                Id = wordId,
                WordText = $"vocab{i:D4}",
                KKPhonics = "/x/",
                DateAdded = DateTime.UtcNow,
                LastUpdated = DateTime.UtcNow,
                Meanings =
                [
                    new WordMeaning
                    {
                        Id = meaningId,
                        PartOfSpeech = "noun",
                        WordId = wordId,
                        Definitions =
                        [
                            new Definition { Id = $"d-{i:D4}", English = $"Definition number {i}", Chinese = $"定義第 {i} 號", WordMeaningId = meaningId }
                        ],
                        ExampleSentences =
                        [
                            new ExampleSentence { Id = $"e-{i:D4}", English = $"She used vocab{i:D4} in a sentence.", Chinese = $"範例 {i}", WordMeaningId = meaningId }
                        ],
                        Tags =
                        [
                            new WordMeaningTag { Id = $"t-{i:D4}", TagType = "Synonym", Word = $"syn{i:D4}", WordMeaningId = meaningId }
                        ]
                    }
                ]
            };
        }

        private static List<DailyChallengeQuestion> QuestionsOf(ServiceResult<DailyChallengeResponse> result)
        {
            Assert.True(result.IsSuccess, result.ErrorMessage);
            return result.Data!.Questions;
        }

        private static DailyChallengeSubmitRequest AnswerAllCorrectly(IEnumerable<DailyChallengeQuestion> questions) =>
            new()
            {
                Answers = questions
                    .Select(q => new DailyChallengeAnswer { QuestionId = q.Id, SelectedIndex = q.CorrectIndex })
                    .ToList()
            };

        private static void AssertSameQuestions(
            IReadOnlyList<DailyChallengeQuestion> expected, IReadOnlyList<DailyChallengeQuestion> actual)
        {
            Assert.Equal(expected.Count, actual.Count);
            for (int i = 0; i < expected.Count; i++)
            {
                Assert.Equal(expected[i].Id, actual[i].Id);
                Assert.Equal(expected[i].Type, actual[i].Type);
                Assert.Equal(expected[i].Prompt, actual[i].Prompt);
                Assert.Equal(expected[i].Options, actual[i].Options); // order matters
                Assert.Equal(expected[i].CorrectIndex, actual[i].CorrectIndex);
                Assert.Equal(expected[i].Explanation, actual[i].Explanation);
                Assert.Equal(expected[i].WordId, actual[i].WordId);
                Assert.Equal(expected[i].Word, actual[i].Word);
            }
        }

        [Fact]
        public async Task GetChallenge_TwiceInADay_ReturnsTheSameSet_EvenIfAWordIsAddedBetween()
        {
            await SeedWordsAsync(30);

            await using var db = CreateContext();
            var first = QuestionsOf(await CreateService(db).GetChallengeAsync());

            await SeedWordsAsync(1, startIndex: 30);

            await using var db2 = CreateContext();
            var second = QuestionsOf(await CreateService(db2).GetChallengeAsync());

            AssertSameQuestions(first, second);
        }

        [Fact]
        public async Task Submit_AfterAWordIsAdded_StillGradesAgainstTheSetTheUserSaw()
        {
            // Regression: before the set was persisted, Submit regenerated from the (now larger)
            // dictionary, so the options/correct indexes the user answered no longer matched.
            await SeedWordsAsync(30);

            await using var db = CreateContext();
            var questions = QuestionsOf(await CreateService(db).GetChallengeAsync());

            await SeedWordsAsync(1, startIndex: 30);

            await using var db2 = CreateContext();
            var result = await CreateService(db2).SubmitChallengeAsync(AnswerAllCorrectly(questions));

            Assert.True(result.IsSuccess, result.ErrorMessage);
            var submit = result.Data!;
            Assert.Equal(DailyChallengeConstants.QuestionsPerChallenge, submit.Score);
        }

        [Fact]
        public async Task Submit_AfterGet_ReusesTheDaysSingleSetRow()
        {
            await SeedWordsAsync(30);

            await using var seen = CreateContext();
            var questions = QuestionsOf(await CreateService(seen).GetChallengeAsync());

            await using var db = CreateContext();
            var submit = (DailyChallengeSubmitResponse)(await CreateService(db)
                .SubmitChallengeAsync(AnswerAllCorrectly(questions))).Data!;

            Assert.Equal(DailyChallengeConstants.QuestionsPerChallenge, submit.Score);
            await using var verify = CreateContext();
            Assert.Single(await verify.DailyChallengeSets.ToListAsync());
        }

        [Fact]
        public async Task Submit_AsTheFirstRequestOfTheDay_CreatesTheSet_ThatALaterGetThenServes()
        {
            await SeedWordsAsync(30);

            await using var db = CreateContext();
            var submit = (DailyChallengeSubmitResponse)(await CreateService(db)
                .SubmitChallengeAsync(new DailyChallengeSubmitRequest { Answers = [] })).Data!;
            Assert.Equal(0, submit.Score);

            await using var verify = CreateContext();
            var row = Assert.Single(await verify.DailyChallengeSets.ToListAsync());
            var persisted = JsonSerializer.Deserialize<List<DailyChallengeQuestion>>(row.QuestionsJson)!;
            Assert.Equal(DailyChallengeConstants.QuestionsPerChallenge, persisted.Count);

            await using var db2 = CreateContext();
            AssertSameQuestions(persisted, QuestionsOf(await CreateService(db2).GetChallengeAsync()));
        }

        [Fact]
        public async Task GetChallenge_NotEnoughWords_FailsAsBefore_AndPersistsNothing()
        {
            await SeedWordsAsync(DailyChallengeConstants.MinWordsForChallenge - 1);

            await using var db = CreateContext();
            var result = await CreateService(db).GetChallengeAsync();

            Assert.False(result.IsSuccess);
            Assert.Equal(ErrorCodes.BadRequest, result.Error);
            Assert.Equal(
                $"Not enough words in the database to generate a challenge. Need at least {DailyChallengeConstants.MinWordsForChallenge}.",
                result.ErrorMessage);

            await using var verify = CreateContext();
            Assert.Empty(await verify.DailyChallengeSets.ToListAsync());
        }

        [Fact]
        public async Task Submit_NotEnoughWords_Fails_PersistsNoSet_AndRecordsNoResult()
        {
            await SeedWordsAsync(DailyChallengeConstants.MinWordsForChallenge - 1);

            await using var db = CreateContext();
            var result = await CreateService(db).SubmitChallengeAsync(new DailyChallengeSubmitRequest { Answers = [] });

            Assert.False(result.IsSuccess);
            Assert.Equal(ErrorCodes.BadRequest, result.Error);

            await using var verify = CreateContext();
            Assert.Empty(await verify.DailyChallengeSets.ToListAsync());
            Assert.Empty(await verify.DailyChallengeResults.ToListAsync());
        }

        [Fact]
        public async Task GetChallenge_LosingTheRaceToAnotherRequest_ReturnsTheWinnersSet_WithoutThrowing()
        {
            await SeedWordsAsync(30);
            var today = DateTime.UtcNow.Date;
            var winnersQuestions = new List<DailyChallengeQuestion>
            {
                new() { Id = 0, Type = QuestionType.Synonym, Prompt = "winner", Options = ["a", "b", "c", "d"], CorrectIndex = 2, Explanation = "first writer" }
            };
            var losersQuestions = new List<DailyChallengeQuestion>
            {
                new() { Id = 0, Type = QuestionType.Synonym, Prompt = "loser", Options = ["w", "x", "y", "z"], CorrectIndex = 0, Explanation = "second writer" }
            };

            // Generate runs after this request found the day empty and before it saves — exactly the
            // window in which the other request persists its set.
            var generator = new Mock<IDailyChallengeQuestionGenerator>();
            generator.Setup(g => g.Generate(It.IsAny<List<Word>>(), It.IsAny<DateTime>()))
                .Returns(() =>
                {
                    using var other = CreateContext();
                    other.DailyChallengeSets.Add(new DailyChallengeSet
                    {
                        ChallengeDate = today,
                        QuestionsJson = JsonSerializer.Serialize(winnersQuestions),
                        CreatedAt = DateTime.UtcNow
                    });
                    other.SaveChanges();
                    return losersQuestions;
                });

            await using var db = CreateContext();
            var service = CreateService(db, generator.Object);

            var result = await service.GetChallengeAsync();

            AssertSameQuestions(winnersQuestions, QuestionsOf(result));
            Assert.Empty(db.ChangeTracker.Entries<DailyChallengeSet>()); // the failed insert is not left tracked

            await using var verify = CreateContext();
            var rows = await verify.DailyChallengeSets.ToListAsync();
            var row = Assert.Single(rows);
            Assert.Contains("winner", row.QuestionsJson);
        }

        [Fact]
        public async Task Submit_LosingTheRaceToAnotherRequest_GradesAgainstTheWinnersSet_AndSavesTheResult()
        {
            await SeedWordsAsync(30);
            var today = DateTime.UtcNow.Date;
            var winnersQuestions = Enumerable.Range(0, DailyChallengeConstants.QuestionsPerChallenge)
                .Select(i => new DailyChallengeQuestion
                {
                    Id = i, Type = QuestionType.Translation, Prompt = $"winner {i}",
                    Options = ["a", "b", "c", "d"], CorrectIndex = i % 4, Explanation = "e"
                })
                .ToList();
            var losersQuestions = winnersQuestions
                .Select(q => new DailyChallengeQuestion
                {
                    Id = q.Id, Type = q.Type, Prompt = $"loser {q.Id}",
                    Options = ["a", "b", "c", "d"], CorrectIndex = (q.CorrectIndex + 1) % 4, Explanation = "e"
                })
                .ToList();

            var generator = new Mock<IDailyChallengeQuestionGenerator>();
            generator.Setup(g => g.Generate(It.IsAny<List<Word>>(), It.IsAny<DateTime>()))
                .Returns(() =>
                {
                    using var other = CreateContext();
                    other.DailyChallengeSets.Add(new DailyChallengeSet
                    {
                        ChallengeDate = today,
                        QuestionsJson = JsonSerializer.Serialize(winnersQuestions),
                        CreatedAt = DateTime.UtcNow
                    });
                    other.SaveChanges();
                    return losersQuestions;
                });

            await using var db = CreateContext();
            var result = await CreateService(db, generator.Object)
                .SubmitChallengeAsync(AnswerAllCorrectly(winnersQuestions));

            Assert.True(result.IsSuccess, result.ErrorMessage);
            var submit = result.Data!;
            Assert.Equal(DailyChallengeConstants.QuestionsPerChallenge, submit.Score);

            await using var verify = CreateContext();
            Assert.Single(await verify.DailyChallengeSets.ToListAsync());
            Assert.Single(await verify.DailyChallengeResults.ToListAsync());
        }

        [Fact]
        public async Task GetChallenge_RoundTripsEveryQuestionFieldThroughTheDatabase()
        {
            await SeedWordsAsync(30);

            await using var db = CreateContext();
            var created = QuestionsOf(await CreateService(db).GetChallengeAsync());

            await using var db2 = CreateContext();
            var reloaded = QuestionsOf(await CreateService(db2).GetChallengeAsync());

            AssertSameQuestions(created, reloaded);
            // The seed words let the generator build every type it uses, so the enum round trip
            // is covered for each of them.
            Assert.Equal(
                [QuestionType.VocabularyDefinition, QuestionType.FillInTheBlank, QuestionType.Translation, QuestionType.Synonym],
                created.Select(q => q.Type).Distinct());
        }

        [Fact]
        public async Task GetChallenge_RoundTrip_PreservesNullsQuotesAndNonAsciiText()
        {
            await SeedWordsAsync(30);
            var tricky = new List<DailyChallengeQuestion>
            {
                new()
                {
                    Id = 3, Type = QuestionType.Antonym,
                    Prompt = "Which word means \"熱\" — hot / cold? <b>&amp;</b> 😀",
                    Options = ["冷", "it's", "line\nbreak", "\"quoted\""],
                    CorrectIndex = 3, Explanation = "冷 = cold (\\ backslash)",
                    WordId = null, Word = null
                }
            };
            var generator = new Mock<IDailyChallengeQuestionGenerator>();
            generator.Setup(g => g.Generate(It.IsAny<List<Word>>(), It.IsAny<DateTime>())).Returns(tricky);

            await using var db = CreateContext();
            await CreateService(db, generator.Object).GetChallengeAsync();

            await using var db2 = CreateContext();
            var reloaded = QuestionsOf(await CreateService(db2, generator.Object).GetChallengeAsync());

            AssertSameQuestions(tricky, reloaded);
            // The second GET read the persisted set; it did not regenerate.
            generator.Verify(g => g.Generate(It.IsAny<List<Word>>(), It.IsAny<DateTime>()), Times.Once);
        }

        [Fact]
        public async Task GetChallenge_AlreadyCompleted_StillReportsThePreviousScore()
        {
            await SeedWordsAsync(30);

            await using var db = CreateContext();
            var questions = QuestionsOf(await CreateService(db).GetChallengeAsync());
            await using var db2 = CreateContext();
            await CreateService(db2).SubmitChallengeAsync(AnswerAllCorrectly(questions));

            await using var db3 = CreateContext();
            var again = (DailyChallengeResponse)(await CreateService(db3).GetChallengeAsync()).Data!;

            Assert.True(again.AlreadyCompleted);
            Assert.Equal(DailyChallengeConstants.QuestionsPerChallenge, again.PreviousScore);
            AssertSameQuestions(questions, again.Questions);
        }
    }
}
