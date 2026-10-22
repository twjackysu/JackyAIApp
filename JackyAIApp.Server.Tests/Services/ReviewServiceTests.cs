using JackyAIApp.Server.Common;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.Data.Models.SQL;
using SQL = JackyAIApp.Server.Data.Models.SQL;
using JackyAIApp.Server.DTO;
using JackyAIApp.Server.Services;
using JackyAIApp.Server.Services.SpacedRepetition;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace JackyAIApp.Server.Tests.Services
{
    /// <summary>
    /// Reviewing saved words, now behind <see cref="ReviewService"/>: which words are due and in
    /// what order, and what submitting answers changes. The SM-2 arithmetic itself is
    /// <see cref="SpacedRepetitionService"/>'s.
    /// </summary>
    public class ReviewServiceTests : IDisposable
    {
        private readonly AzureSQLDBContext _db = new(new DbContextOptionsBuilder<AzureSQLDBContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        private static readonly DateTime Today = new(2026, 10, 21, 0, 0, 0, DateTimeKind.Utc);

        public void Dispose() => _db.Dispose();

        private sealed class Clock(DateTime now) : TimeProvider
        {
            public override DateTimeOffset GetUtcNow() => new(now, TimeSpan.Zero);
        }

        private ReviewService As(string userId)
        {
            var user = new Mock<ICurrentUser>();
            user.Setup(u => u.Id).Returns(userId);
            return new ReviewService(_db, user.Object, new SpacedRepetitionService(), new Clock(Today.AddHours(15)));
        }

        private UserWord AddWord(int id, string userId, DateTime? nextReview, string text = "word")
        {
            var word = new SQL.Word
            {
                Id = $"w{id}", WordText = $"{text}{id}", KKPhonics = "kk", DateAdded = Today, LastUpdated = Today,
                Meanings = [new SQL.WordMeaning { Id = $"m{id}", PartOfSpeech = "noun", WordId = $"w{id}" }],
            };
            var userWord = new UserWord { Id = id, UserId = userId, WordId = word.Id, Word = word, DateAdded = Today, NextReviewDate = nextReview };
            _db.UserWords.Add(userWord);
            return userWord;
        }

        [Fact]
        public async Task Due_IsYourWordsNeverReviewedOrDueByToday_NeverReviewedFirst()
        {
            AddWord(1, "alice", Today);
            AddWord(2, "alice", Today.AddDays(-1));
            AddWord(3, "alice", null);
            AddWord(4, "alice", Today.AddDays(1));
            AddWord(5, "bob", null);
            await _db.SaveChangesAsync();

            var due = (await As("alice").GetDueAsync(default)).Data!;

            Assert.Equal([3, 2, 1], due.DueWords.Select(w => w.UserWordId));
            Assert.Equal(3, due.TotalDueCount);
            Assert.Equal(("word3", "noun"), (due.DueWords[0].WordText, due.DueWords[0].Meanings.Single().PartOfSpeech));
            Assert.Equal(3, (await As("alice").GetDueCountAsync(default)).Data!.DueCount);
        }

        [Fact]
        public async Task Due_HandsOutABatchOfTwenty_ButCountsThemAll()
        {
            for (var i = 1; i <= 25; i++) AddWord(i, "alice", Today.AddDays(-i));
            await _db.SaveChangesAsync();

            var due = (await As("alice").GetDueAsync(default)).Data!;

            Assert.Equal(20, due.DueWords.Count);
            Assert.Equal(25, due.DueWords[0].UserWordId); // the longest overdue first
            Assert.Equal(25, due.TotalDueCount);
        }

        [Fact]
        public async Task Submit_SchedulesYourWords_AndAwardsXpForTheCorrectOnes()
        {
            _db.Users.Add(new User { Id = "alice", LastUpdated = Today, TotalXP = 10 });
            AddWord(1, "alice", null);
            AddWord(2, "alice", null);
            AddWord(3, "bob", null);
            await _db.SaveChangesAsync();

            var result = await As("alice").SubmitAsync(new ReviewSubmitRequest
            {
                Reviews =
                [
                    new ReviewAnswerRequest { UserWordId = 1, Quality = 4 },
                    new ReviewAnswerRequest { UserWordId = 2, Quality = 1 },
                    new ReviewAnswerRequest { UserWordId = 3, Quality = 5 }, // bob's: skipped
                ],
            }, default);

            Assert.Equal((3, 1, 3), (result.Data!.WordsReviewed, result.Data.CorrectCount, result.Data.XPEarned));
            Assert.Equal(13, _db.Users.Single().TotalXP);
            var words = _db.UserWords.AsNoTracking().OrderBy(w => w.Id).ToList();
            Assert.Equal((1, 1), (words[0].ReviewCount, words[0].ConsecutiveCorrect));
            Assert.Equal((1, 0), (words[1].ReviewCount, words[1].ConsecutiveCorrect));
            Assert.True(words[0].NextReviewDate > Today);
            Assert.Equal((0, (DateTime?)null), (words[2].ReviewCount, words[2].NextReviewDate));
            // A missed answer is a review too; what the weekly report counts.
            Assert.Equal([Today, Today, null], words.Select(w => w.LastReviewedDate));
        }

        [Fact]
        public async Task Submit_WithNoAnswers_IsABadRequest()
        {
            var result = await As("alice").SubmitAsync(new ReviewSubmitRequest { Reviews = [] }, default);

            Assert.Equal(ErrorCodes.BadRequest, result.Error);
        }
    }
}
