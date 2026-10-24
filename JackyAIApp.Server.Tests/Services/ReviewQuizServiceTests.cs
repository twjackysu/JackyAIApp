using JackyAIApp.Server.Data;
using JackyAIApp.Server.Data.Models.SQL;
using JackyAIApp.Server.Services;
using JackyAIApp.Server.Services.DailyChallenge;
using JackyAIApp.Server.Services.SpacedRepetition;
using JackyAIApp.Server.Tests.TestHelpers;
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
    /// Studio AI's quiz on the reader's own words: which of their words it asks (due first, never reviewed first; with none
    /// due, the soonest), that each question names its saved word for the answer to be recorded against, and that only
    /// words a question can be made of are asked. On SQLite, so the queries run as a relational provider runs them.
    /// </summary>
    public class ReviewQuizServiceTests : IDisposable
    {
        private const string UserId = "quiz-user";
        private const string OtherUserId = "quiz-other";
        private static readonly DateTimeOffset Now = new(2026, 10, 25, 3, 0, 0, TimeSpan.Zero);
        private static readonly DateTime Today = Now.UtcDateTime.Date;

        private readonly SqliteConnection _connection;
        private readonly DbContextOptions<AzureSQLDBContext> _options;

        public ReviewQuizServiceTests()
        {
            _connection = new SqliteConnection("Data Source=:memory:");
            _connection.Open();
            _options = new DbContextOptionsBuilder<AzureSQLDBContext>().UseSqlite(_connection).Options;
            using var db = new AzureSQLDBContext(_options);
            db.Database.EnsureCreated();
            db.Users.Add(new User { Id = UserId, LastUpdated = Now.UtcDateTime });
            db.Users.Add(new User { Id = OtherUserId, LastUpdated = Now.UtcDateTime });
            // The dictionary the wrong options come from.
            for (var i = 0; i < 12; i++) db.Words.Add(BuildWord(i));
            db.SaveChanges();
        }

        public void Dispose() => _connection.Dispose();

        private static Word BuildWord(int i, bool withDefinition = true)
        {
            var wordId = $"w-{i:D4}";
            var meaningId = $"m-{i:D4}";
            return new Word
            {
                Id = wordId,
                WordText = $"vocab{i:D4}",
                KKPhonics = "/x/",
                DateAdded = Now.UtcDateTime,
                LastUpdated = Now.UtcDateTime.AddMinutes(i),
                Meanings =
                [
                    new WordMeaning
                    {
                        Id = meaningId,
                        PartOfSpeech = "noun",
                        WordId = wordId,
                        Definitions = withDefinition
                            ? [new Definition { Id = $"d-{i:D4}", English = $"Definition number {i}", Chinese = $"定義 {i}", WordMeaningId = meaningId }]
                            : [],
                        ExampleSentences =
                        [
                            new ExampleSentence { Id = $"e-{i:D4}", English = $"She used vocab{i:D4} today.", Chinese = $"例句 {i}", WordMeaningId = meaningId }
                        ],
                    }
                ]
            };
        }

        private async Task<int> SaveAsync(string userId, string wordId, DateTime? nextReview)
        {
            await using var db = new AzureSQLDBContext(_options);
            var saved = new UserWord { UserId = userId, WordId = wordId, NextReviewDate = nextReview, DateAdded = Now.UtcDateTime };
            db.UserWords.Add(saved);
            await db.SaveChangesAsync();
            return saved.Id;
        }

        private async Task<ReviewQuiz> CreateAsync(int count)
        {
            await using var db = new AzureSQLDBContext(_options);
            var user = new Mock<ICurrentUser>();
            user.Setup(u => u.Id).Returns(UserId);
            var service = new ReviewQuizService(db, user.Object, new DailyChallengeQuestionGenerator(), new TestClock(Now));
            var result = await service.CreateAsync(count, default);
            Assert.True(result.IsSuccess);
            return result.Data!;
        }

        [Fact]
        public async Task AsksTheWordsDue_NeverReviewedFirst_EachNamingItsSavedWord()
        {
            var later = await SaveAsync(UserId, "w-0001", Today.AddDays(-1));
            var never = await SaveAsync(UserId, "w-0002", null);
            await SaveAsync(UserId, "w-0003", Today.AddDays(5)); // not due
            await SaveAsync(OtherUserId, "w-0004", null); // someone else's

            var quiz = await CreateAsync(5);

            Assert.True(quiz.FromDue);
            Assert.Equal(2, quiz.TotalDueCount);
            Assert.Equal(new[] { never, later }, quiz.Questions.Select(q => q.UserWordId));
            Assert.Equal(new[] { "vocab0002", "vocab0001" }, quiz.Questions.Select(q => q.Word));
            Assert.All(quiz.Questions, q =>
            {
                Assert.Equal(4, q.Options.Count);
                Assert.InRange(q.CorrectIndex, 0, 3);
                Assert.Equal(4, q.Options.Distinct().Count());
            });
        }

        [Fact]
        public async Task WithNoneDue_AsksTheSoonest_AndNoMoreThanAsked()
        {
            await SaveAsync(UserId, "w-0005", Today.AddDays(9));
            var soonest = await SaveAsync(UserId, "w-0006", Today.AddDays(2));
            var next = await SaveAsync(UserId, "w-0007", Today.AddDays(4));

            var quiz = await CreateAsync(2);

            Assert.False(quiz.FromDue);
            Assert.Equal(0, quiz.TotalDueCount);
            Assert.Equal(new[] { soonest, next }, quiz.Questions.Select(q => q.UserWordId));
        }

        [Fact]
        public async Task LeavesOutAWordNoQuestionCanBeMadeOf()
        {
            await using (var db = new AzureSQLDBContext(_options))
            {
                db.Words.Add(BuildWord(50, withDefinition: false));
                await db.SaveChangesAsync();
            }
            await SaveAsync(UserId, "w-0050", null);
            var asked = await SaveAsync(UserId, "w-0008", Today);

            var quiz = await CreateAsync(3);

            Assert.Equal(new[] { asked }, quiz.Questions.Select(q => q.UserWordId));
            Assert.Equal(1, quiz.TotalDueCount);
        }

        [Fact]
        public async Task AReaderWithNoSavedWords_GetsNoQuestions()
        {
            var quiz = await CreateAsync(3);

            Assert.Empty(quiz.Questions);
        }

        [Fact]
        public async Task AskingForTooMany_IsHeldToTheMost()
        {
            for (var i = 0; i < 7; i++) await SaveAsync(UserId, $"w-{i:D4}", null);

            Assert.Equal(ReviewQuizService.MaxQuestions, (await CreateAsync(50)).Questions.Count);
            Assert.Single((await CreateAsync(0)).Questions);
        }
    }
}
