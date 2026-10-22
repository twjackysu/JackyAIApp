using JackyAIApp.Server.Common;
using JackyAIApp.Server.Data;
using Microsoft.EntityFrameworkCore;
using SQL = JackyAIApp.Server.Data.Models.SQL;

namespace JackyAIApp.Server.Services.Exam
{
    /// <summary>
    /// Which of a user's words an exam asks about. Picks at the database level (COUNT + OFFSET)
    /// instead of loading the user's whole collection into memory.
    /// </summary>
    public interface IExamWordPicker
    {
        /// <summary>
        /// The word for the next question, biased toward the SM-2 review schedule instead of pure
        /// uniform random — otherwise exams and the Review flow drift apart and well-known words get
        /// quizzed just as often as weak ones: one that is due (or never reviewed); with none due,
        /// one of the weakest; with an empty repository, any valid word in the dictionary, so exams
        /// work before anyone has favorited anything. Null when there is no word at all.
        /// </summary>
        Task<string?> PickWordIdAsync(string userId);

        /// <summary>
        /// Up to <paramref name="take"/> distinct random rows from the user's saved words, with their
        /// <see cref="SQL.UserWord.Word"/> loaded. Fewer (down to none) when the repository is that small.
        /// </summary>
        Task<List<SQL.UserWord>> PickSavedWordsAsync(string userId, int take);
    }

    public class ExamWordPicker(AzureSQLDBContext dbContext) : IExamWordPicker
    {
        /// <summary>What an exam answers when <see cref="PickWordIdAsync"/> finds no word at all.</summary>
        public static ServiceFailure NoWords => ServiceResult.Fail(ErrorCodes.Forbidden,
            "No words are available yet to generate an exam from. Try searching a word in the Dictionary first.");

        /// <summary>How many of the weakest words a pick chooses from when none is due.</summary>
        private const int WeakPoolSize = 10;

        public async Task<string?> PickWordIdAsync(string userId)
        {
            var query = dbContext.UserWords.Where(uw => uw.UserId == userId);
            var count = await query.CountAsync();
            if (count == 0)
            {
                return await PickDictionaryWordIdAsync();
            }

            var now = DateTime.UtcNow;

            // Prefer words that are due (or have never been reviewed).
            var dueQuery = query.Where(uw => uw.NextReviewDate == null || uw.NextReviewDate <= now);
            var dueCount = await dueQuery.CountAsync();
            if (dueCount > 0)
            {
                var skip = Random.Shared.Next(dueCount);
                return await dueQuery
                    .OrderBy(uw => uw.WordId)
                    .Skip(skip)
                    .Select(uw => uw.WordId)
                    .FirstOrDefaultAsync();
            }

            // Nothing due yet — surface the weakest words (fewest consecutive-correct
            // streaks) so struggling words still get practiced between scheduled reviews.
            var weakestIds = await query
                .OrderBy(uw => uw.ConsecutiveCorrect)
                .ThenBy(uw => uw.ReviewCount)
                .Select(uw => uw.WordId)
                .Take(Math.Min(WeakPoolSize, count))
                .ToListAsync();

            return weakestIds[Random.Shared.Next(weakestIds.Count)];
        }

        /// <summary>Random word from the whole dictionary, used when the user has no repository words yet.</summary>
        private async Task<string?> PickDictionaryWordIdAsync()
        {
            var query = dbContext.Words.Where(w => w.DataInvalid != true);
            var count = await query.CountAsync();
            if (count == 0)
            {
                return null;
            }

            var skip = Random.Shared.Next(count);
            return await query
                .OrderBy(w => w.Id)
                .Skip(skip)
                .Select(w => w.Id)
                .FirstOrDefaultAsync();
        }

        public async Task<List<SQL.UserWord>> PickSavedWordsAsync(string userId, int take)
        {
            var query = dbContext.UserWords.Where(uw => uw.UserId == userId);
            var count = await query.CountAsync();
            if (count == 0)
            {
                return [];
            }

            take = Math.Min(take, count);
            var offsets = new HashSet<int>();
            while (offsets.Count < take)
            {
                offsets.Add(Random.Shared.Next(count));
            }

            var results = new List<SQL.UserWord>();
            foreach (var offset in offsets)
            {
                var userWord = await query
                    .Include(uw => uw.Word)
                    .OrderBy(uw => uw.WordId)
                    .Skip(offset)
                    .FirstAsync();
                results.Add(userWord);
            }
            return results;
        }
    }
}
