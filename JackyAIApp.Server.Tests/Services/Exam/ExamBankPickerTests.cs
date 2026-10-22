using JackyAIApp.Server.Data;
using JackyAIApp.Server.Data.Models.SQL;
using JackyAIApp.Server.Services;
using JackyAIApp.Server.Services.Exam;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace JackyAIApp.Server.Tests.Services.Exam
{
    /// <summary>
    /// A shared question bank as one user sees it (<see cref="ExamBankPicker"/>): the questions they
    /// attempted most recently (their last 30) are skipped while others remain, and the whole bank
    /// is the pool once every question was seen.
    /// </summary>
    public class ExamBankPickerTests : IDisposable
    {
        private const string UserId = "alice";
        private readonly AzureSQLDBContext _db = new(new DbContextOptionsBuilder<AzureSQLDBContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        private readonly DateTime _t0 = DateTime.UtcNow.AddDays(-1);

        public void Dispose() => _db.Dispose();

        private ExamBankPicker As(string? userId)
        {
            var user = new Mock<ICurrentUser>();
            user.Setup(u => u.Id).Returns(userId);
            return new ExamBankPicker(_db, user.Object);
        }

        private Task<ExamBankPool<ListeningTest>> OpenAsync(string? userId) =>
            As(userId).OpenAsync(_db.ListeningTests, q => q.Include(t => t.Options), a => a.ListeningTestId);

        private void AddQuestions(int count)
        {
            for (var i = 0; i < count; i++)
            {
                var id = $"q{i:D2}";
                _db.ListeningTests.Add(new ListeningTest
                {
                    Id = id, ImageUrl = $"https://img/{id}.png", CreatedAt = _t0,
                    Options = [new ListeningTestOption { Id = $"{id}-a", Text = "A", IsCorrect = true, Explanation = "", ListeningTestId = id }],
                });
            }
        }

        private void Attempt(string userId, string questionId, int minute) =>
            _db.ExamAttempts.Add(new ExamAttempt
            {
                UserId = userId, ListeningTestId = questionId, ExamType = "Listening", IsCorrect = true, CreatedAt = _t0.AddMinutes(minute),
            });

        [Fact]
        public async Task RecentlyAttemptedQuestions_AreSkipped_WhileOthersRemain()
        {
            AddQuestions(3);
            Attempt(UserId, "q00", 1);
            Attempt(UserId, "q02", 2);
            await _db.SaveChangesAsync();

            var pool = await OpenAsync(UserId);

            Assert.Equal((3, 1), (pool.TotalCount, pool.UnseenCount));
            for (var i = 0; i < 10; i++)
            {
                var picked = await pool.PickAsync();
                Assert.Equal("q01", picked.Id);
                Assert.Single(picked.Options); // the bank's own eager loads
            }
        }

        [Fact]
        public async Task OnlyTheLast30AttemptedQuestions_CountAsRecentlySeen()
        {
            AddQuestions(31);
            for (var i = 0; i < 31; i++) Attempt(UserId, $"q{i:D2}", i);
            // A repeat of a recent question does not push an older one out of the window.
            Attempt(UserId, "q30", 40);
            await _db.SaveChangesAsync();

            var pool = await OpenAsync(UserId);

            Assert.Equal(1, pool.UnseenCount);
            Assert.Equal("q00", (await pool.PickAsync()).Id);
        }

        [Fact]
        public async Task EverythingRecentlySeen_ThePoolIsTheWholeBank()
        {
            AddQuestions(2);
            Attempt(UserId, "q00", 1);
            Attempt(UserId, "q01", 2);
            await _db.SaveChangesAsync();

            var pool = await OpenAsync(UserId);

            Assert.Equal((2, 0), (pool.TotalCount, pool.UnseenCount));
            var picks = new HashSet<string>();
            for (var i = 0; i < 40; i++) picks.Add((await pool.PickAsync()).Id);
            Assert.Equal(["q00", "q01"], picks.Order());
        }

        [Fact]
        public async Task SomeoneElsesAttempts_AndOtherBanks_DoNotCount()
        {
            AddQuestions(2);
            Attempt("bob", "q00", 1);
            _db.ExamAttempts.Add(new ExamAttempt { UserId = UserId, ReadingPassageId = "q01", ExamType = "Reading", IsCorrect = true, CreatedAt = _t0 });
            await _db.SaveChangesAsync();

            var pool = await OpenAsync(UserId);

            Assert.Equal((2, 2), (pool.TotalCount, pool.UnseenCount));
        }

        [Fact]
        public async Task AnAnonymousRequest_HasNoWindow()
        {
            AddQuestions(2);
            Attempt(UserId, "q00", 1);
            await _db.SaveChangesAsync();

            var pool = await OpenAsync(null);

            Assert.Equal((2, 2), (pool.TotalCount, pool.UnseenCount));
        }

        [Fact]
        public async Task AnEmptyBank_HasNothing()
        {
            var pool = await OpenAsync(UserId);

            Assert.Equal((0, 0), (pool.TotalCount, pool.UnseenCount));
        }
    }
}
