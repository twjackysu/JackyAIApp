using JackyAIApp.Server.Common;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.Services.DailyChallenge;
using Microsoft.EntityFrameworkCore;
using SQL = JackyAIApp.Server.Data.Models.SQL;

namespace JackyAIApp.Server.Services.SpacedRepetition
{
    /// <summary>One question of a review quiz, about one of the reader's saved words (<see cref="UserWordId"/>).</summary>
    public sealed record ReviewQuizQuestion(
        int UserWordId,
        string Word,
        string Prompt,
        IReadOnlyList<string> Options,
        int CorrectIndex,
        string Explanation);

    /// <summary>
    /// A few multiple-choice questions on the reader's own words: those due for review (<see cref="FromDue"/>), or,
    /// with none due, the saved words whose review comes soonest. <see cref="TotalDueCount"/> is how many are due in all.
    /// </summary>
    public sealed record ReviewQuiz(IReadOnlyList<ReviewQuizQuestion> Questions, bool FromDue, int TotalDueCount);

    /// <summary>
    /// A short quiz on the reader's saved words, of the daily challenge's kinds of question (the same generator, no AI),
    /// for Studio AI to put in the chat (<c>review_quiz</c>). Making one changes nothing: an answer goes to the review
    /// schedule only when the reader sends it (<see cref="IReviewService.SubmitAsync"/>, the review page's own call).
    /// </summary>
    public interface IReviewQuizService
    {
        /// <summary>At most <paramref name="count"/> questions (1 to <see cref="ReviewQuizService.MaxQuestions"/>), none for a reader with no saved words.</summary>
        Task<ServiceResult<ReviewQuiz>> CreateAsync(int count, CancellationToken ct);
    }

    public sealed class ReviewQuizService(
        AzureSQLDBContext dbContext,
        ICurrentUser currentUser,
        IDailyChallengeQuestionGenerator generator,
        TimeProvider time) : IReviewQuizService
    {
        public const int MaxQuestions = 5;

        /// <summary>The most other words the wrong options are drawn from (the dictionary's latest).</summary>
        internal const int PoolSize = 60;

        public async Task<ServiceResult<ReviewQuiz>> CreateAsync(int count, CancellationToken ct)
        {
            var userId = currentUser.RequireId();
            count = Math.Clamp(count, 1, MaxQuestions);
            // The review schedule's day, as ReviewService counts it.
            var today = time.GetUtcNow().UtcDateTime.Date;

            // Only words a question can be made of: valid, with a definition.
            var mine = dbContext.UserWords.Where(uw =>
                uw.UserId == userId &&
                (uw.Word.DataInvalid == null || uw.Word.DataInvalid == false) &&
                uw.Word.Meanings.Any(m => m.Definitions.Any()));
            var due = mine.Where(uw => uw.NextReviewDate == null || uw.NextReviewDate <= today);
            var totalDue = await due.CountAsync(ct);

            var picked = await (totalDue > 0 ? due : mine)
                .OrderBy(uw => uw.NextReviewDate ?? DateTime.MinValue) // never reviewed first, as the review page
                .ThenBy(uw => uw.Id)
                .Take(count)
                .Select(uw => new { uw.Id, uw.WordId })
                .ToListAsync(ct);
            if (picked.Count == 0) return ServiceResult.Ok(new ReviewQuiz([], FromDue: false, TotalDueCount: 0));

            var targetIds = picked.Select(p => p.WordId).ToList();
            var targets = (await WithMeanings(dbContext.Words.Where(w => targetIds.Contains(w.Id)))
                    .ToListAsync(ct))
                .ToDictionary(w => w.Id);
            var others = await WithMeanings(dbContext.Words
                    .Where(w => !targetIds.Contains(w.Id) &&
                                (w.DataInvalid == null || w.DataInvalid == false) &&
                                w.Meanings.Any(m => m.Definitions.Any()))
                    .OrderByDescending(w => w.LastUpdated)
                    .ThenBy(w => w.Id)
                    .Take(PoolSize))
                .ToListAsync(ct);

            // A word removed between the two reads is left out.
            var asked = picked.Where(p => targets.ContainsKey(p.WordId)).ToList();
            var words = asked.Select(p => targets[p.WordId]).ToList();
            var generated = generator.GenerateFor(words, [.. words, .. others], Random.Shared.Next());

            var questions = generated
                .Select((q, i) => new ReviewQuizQuestion(
                    asked[i].Id, q.Word ?? words[i].WordText, q.Prompt, q.Options, q.CorrectIndex, q.Explanation))
                .ToList();
            return ServiceResult.Ok(new ReviewQuiz(questions, FromDue: totalDue > 0, totalDue));
        }

        private static IQueryable<SQL.Word> WithMeanings(IQueryable<SQL.Word> words) =>
            words
                .Include(w => w.Meanings).ThenInclude(m => m.Definitions)
                .Include(w => w.Meanings).ThenInclude(m => m.ExampleSentences)
                .Include(w => w.Meanings).ThenInclude(m => m.Tags)
                .AsSplitQuery();
    }
}
