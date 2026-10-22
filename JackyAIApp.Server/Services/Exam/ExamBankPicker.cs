using System.Linq.Expressions;
using JackyAIApp.Server.Data;
using Microsoft.EntityFrameworkCore;
using SQL = JackyAIApp.Server.Data.Models.SQL;

namespace JackyAIApp.Server.Services.Exam
{
    /// <summary>
    /// One of the shared question banks (listening, listening response, reading) as the signed-in
    /// user sees it: how many questions it has, how many of them the user has not attempted
    /// recently, and a random pick that prefers those.
    /// </summary>
    public sealed class ExamBankPool<T>(
        IQueryable<T> bank, Func<IQueryable<T>, IQueryable<T>> include,
        HashSet<string> recentlySeen, int totalCount, int unseenCount) where T : class, SQL.IStringId
    {
        public int TotalCount => totalCount;

        /// <summary>Questions outside the user's no-repeat window.</summary>
        public int UnseenCount => unseenCount;

        /// <summary>A random question, preferring the unseen ones; the whole bank when every
        /// question was seen recently. The bank must not be empty.</summary>
        public async Task<T> PickAsync()
        {
            var pool = unseenCount > 0 ? bank.Where(t => !recentlySeen.Contains(t.Id)) : bank;
            var poolCount = unseenCount > 0 ? unseenCount : totalCount;
            return await include(pool)
                .OrderBy(t => t.Id)
                .Skip(Random.Shared.Next(poolCount))
                .FirstAsync();
        }
    }

    public interface IExamBankPicker
    {
        /// <summary>
        /// The bank as the signed-in user sees it. <paramref name="include"/> applies the bank's own
        /// eager loads; <paramref name="attemptedId"/> picks the bank's id out of an exam attempt.
        /// An anonymous request has no no-repeat window.
        /// </summary>
        Task<ExamBankPool<T>> OpenAsync<T>(
            IQueryable<T> bank, Func<IQueryable<T>, IQueryable<T>> include,
            Expression<Func<SQL.ExamAttempt, string?>> attemptedId) where T : class, SQL.IStringId;
    }

    public class ExamBankPicker(AzureSQLDBContext dbContext, ICurrentUser currentUser) : IExamBankPicker
    {
        // How many of the user's most recent attempts to treat as "just seen" and exclude from
        // re-selection. Sized well past one round (ROUND_SIZE=10 on the frontend) — a user who
        // keeps playing past round boundaries still shouldn't hit a repeat for a while. Paired
        // with the generated banks' "always generate while unseen < threshold" rule, a bank is
        // forced to grow to at least NoRepeatWindow + threshold questions before a repeat becomes
        // possible at all.
        internal const int NoRepeatWindow = 30;

        public async Task<ExamBankPool<T>> OpenAsync<T>(
            IQueryable<T> bank, Func<IQueryable<T>, IQueryable<T>> include,
            Expression<Func<SQL.ExamAttempt, string?>> attemptedId) where T : class, SQL.IStringId
        {
            var userId = currentUser.Id;
            var recentlySeen = string.IsNullOrEmpty(userId)
                ? new HashSet<string>()
                : await GetRecentAttemptIdsAsync(userId, NoRepeatWindow, attemptedId);

            var totalCount = await bank.CountAsync();
            // When nothing is excluded, the whole bank is the pool — skip the extra COUNT.
            var unseenCount = recentlySeen.Count == 0
                ? totalCount
                : await bank.CountAsync(t => !recentlySeen.Contains(t.Id));
            return new ExamBankPool<T>(bank, include, recentlySeen, totalCount, unseenCount);
        }

        /// <summary>The ids of this user's most recently attempted items of one bank (whichever
        /// <paramref name="idSelector"/> picks), newest first, deduped — the no-repeat window.</summary>
        private async Task<HashSet<string>> GetRecentAttemptIdsAsync(
            string userId, int take, Expression<Func<SQL.ExamAttempt, string?>> idSelector)
        {
            var ids = await dbContext.ExamAttempts
                .Where(a => a.UserId == userId)
                .OrderByDescending(a => a.CreatedAt)
                .Select(idSelector)
                .Where(id => id != null)
                .Take(take * 3)
                .ToListAsync();
            return ids.Select(id => id!).Distinct().Take(take).ToHashSet();
        }
    }
}
