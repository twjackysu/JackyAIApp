using JackyAIApp.Server.Data;
using JackyAIApp.Server.Data.Models.SQL;
using JackyAIApp.Server.Services.Exam;
using Microsoft.EntityFrameworkCore;

namespace JackyAIApp.Server.Tests.Services.Exam
{
    /// <summary>
    /// Which word an exam asks about (<see cref="ExamWordPicker"/>): a due one first, else one of the
    /// ten weakest, else any valid dictionary word. The picks are random, so the rules are checked
    /// over repeated picks: a word the rule excludes must never come up.
    /// </summary>
    public class ExamWordPickerTests : IDisposable
    {
        private const string UserId = "alice";
        private readonly AzureSQLDBContext _db = new(new DbContextOptionsBuilder<AzureSQLDBContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        private readonly ExamWordPicker _picker;

        public ExamWordPickerTests() => _picker = new ExamWordPicker(_db);

        public void Dispose() => _db.Dispose();

        private Word AddWord(string id, bool? invalid = null)
        {
            var word = new Word { Id = id, WordText = id, KKPhonics = "/k/", DateAdded = DateTime.UtcNow, LastUpdated = DateTime.UtcNow, DataInvalid = invalid };
            _db.Words.Add(word);
            return word;
        }

        private void Save(string userId, string wordId, DateTime? nextReview = null, int consecutiveCorrect = 0) =>
            _db.UserWords.Add(new UserWord
            {
                UserId = userId, WordId = wordId, DateAdded = DateTime.UtcNow,
                NextReviewDate = nextReview, ConsecutiveCorrect = consecutiveCorrect,
            });

        [Fact]
        public async Task ADueWord_ComesFirst()
        {
            foreach (var id in new[] { "w1", "w2", "w3" }) AddWord(id);
            Save(UserId, "w1", DateTime.UtcNow.AddDays(5));
            Save(UserId, "w2", DateTime.UtcNow.AddDays(-1));
            Save(UserId, "w3", DateTime.UtcNow.AddDays(5));
            await _db.SaveChangesAsync();

            for (var i = 0; i < 10; i++)
                Assert.Equal("w2", await _picker.PickWordIdAsync(UserId));
        }

        [Fact]
        public async Task ANeverReviewedWord_CountsAsDue()
        {
            AddWord("new"); AddWord("later");
            Save(UserId, "new");
            Save(UserId, "later", DateTime.UtcNow.AddDays(5));
            await _db.SaveChangesAsync();

            for (var i = 0; i < 10; i++)
                Assert.Equal("new", await _picker.PickWordIdAsync(UserId));
        }

        [Fact]
        public async Task NothingDue_PicksAmongTheTenWeakest()
        {
            for (var i = 0; i < 12; i++)
            {
                AddWord($"w{i}");
                Save(UserId, $"w{i}", DateTime.UtcNow.AddDays(5), consecutiveCorrect: i is 3 or 8 ? 5 : 1);
            }
            await _db.SaveChangesAsync();

            for (var i = 0; i < 40; i++)
                Assert.DoesNotContain(await _picker.PickWordIdAsync(UserId), new[] { "w3", "w8" });
        }

        [Fact]
        public async Task SomeoneElsesSavedWords_AreNotYours()
        {
            AddWord("mine"); AddWord("bobs");
            Save(UserId, "mine", DateTime.UtcNow.AddDays(5));
            Save("bob", "bobs");
            await _db.SaveChangesAsync();

            for (var i = 0; i < 10; i++)
                Assert.Equal("mine", await _picker.PickWordIdAsync(UserId));
        }

        [Fact]
        public async Task AnEmptyRepository_PicksAValidDictionaryWord()
        {
            AddWord("valid");
            AddWord("invalid", invalid: true);
            await _db.SaveChangesAsync();

            for (var i = 0; i < 20; i++)
                Assert.Equal("valid", await _picker.PickWordIdAsync(UserId));
        }

        [Fact]
        public async Task NoValidWordAnywhere_IsNull()
        {
            AddWord("invalid", invalid: true);
            await _db.SaveChangesAsync();

            Assert.Null(await _picker.PickWordIdAsync(UserId));
        }

        [Fact]
        public async Task SavedWords_AreDistinct_YourOwn_AtMostTheNumberAsked_WithTheirWord()
        {
            for (var i = 0; i < 8; i++) { AddWord($"w{i}"); Save(UserId, $"w{i}"); }
            AddWord("bobs"); Save("bob", "bobs");
            await _db.SaveChangesAsync();

            var picked = await _picker.PickSavedWordsAsync(UserId, 5);

            Assert.Equal(5, picked.Select(w => w.WordId).Distinct().Count());
            Assert.All(picked, w => Assert.Equal((UserId, w.WordId), (w.UserId, w.Word.WordText)));
        }

        [Fact]
        public async Task SavedWords_AreAllOfThem_WhenFewerThanAsked_AndNoneForAnEmptyRepository()
        {
            AddWord("w1"); AddWord("w2");
            Save(UserId, "w1"); Save(UserId, "w2");
            await _db.SaveChangesAsync();

            Assert.Equal(["w1", "w2"], (await _picker.PickSavedWordsAsync(UserId, 5)).Select(w => w.WordId).Order());
            Assert.Empty(await _picker.PickSavedWordsAsync("bob", 5));
        }
    }
}
