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
    /// For a quiz on words the reader named (<see cref="IReviewQuizService.CreateForAsync"/>), <see cref="Asked"/> is those
    /// words as looked up and <see cref="FromDue"/> whether every word asked is due; otherwise <see cref="Asked"/> is null.
    /// </summary>
    public sealed record ReviewQuiz(
        IReadOnlyList<ReviewQuizQuestion> Questions, bool FromDue, int TotalDueCount, IReadOnlyList<string>? Asked = null);

    /// <summary>
    /// A short quiz on the reader's saved words, of the daily challenge's kinds of question (the same generator, no AI),
    /// for Studio AI to put in the chat (<c>review_quiz</c>). Making one changes nothing: an answer goes to the review
    /// schedule only when the reader sends it (<see cref="IReviewService.SubmitAsync"/>, the review page's own call).
    /// </summary>
    public interface IReviewQuizService
    {
        /// <summary>At most <paramref name="count"/> questions (1 to <see cref="ReviewQuizService.MaxQuestions"/>), none for a reader with no saved words.</summary>
        Task<ServiceResult<ReviewQuiz>> CreateAsync(int count, CancellationToken ct);

        /// <summary>
        /// Questions on the words named (Studio AI's 「考我這幾個字」 after <c>weak_spots</c>), in their order, at most
        /// <see cref="ReviewQuizService.MaxQuestions"/>: only those among the reader's saved words that a question can be
        /// made of; none when none is. The answers go to the review schedule only when every word asked is due
        /// (<see cref="ReviewQuiz.FromDue"/>), since an early answer would push a word out.
        /// </summary>
        Task<ServiceResult<ReviewQuiz>> CreateForAsync(IReadOnlyList<string> words, CancellationToken ct);
    }

    public sealed class ReviewQuizService(
        AzureSQLDBContext dbContext,
        ICurrentUser currentUser,
        IDailyChallengeQuestionGenerator generator,
        TimeProvider time) : IReviewQuizService
    {
        public const int MaxQuestions = 5;

        /// <summary>The most other words the wrong options are drawn from.</summary>
        internal const int PoolSize = 60;

        /// <summary>Fewer of the reader's own other words than this, and the dictionary's fill the pool.</summary>
        internal const int MinOwnPool = 12;

        public async Task<ServiceResult<ReviewQuiz>> CreateAsync(int count, CancellationToken ct)
        {
            var userId = currentUser.RequireId();
            count = Math.Clamp(count, 1, MaxQuestions);
            // The review schedule's day, as ReviewService counts it.
            var today = time.GetUtcNow().UtcDateTime.Date;

            var mine = Mine(userId);
            var due = mine.Where(uw => uw.NextReviewDate == null || uw.NextReviewDate <= today);
            var totalDue = await due.CountAsync(ct);

            var picked = await (totalDue > 0 ? due : mine)
                .OrderBy(uw => uw.NextReviewDate ?? DateTime.MinValue) // never reviewed first, as the review page
                .ThenBy(uw => uw.Id)
                .Take(count)
                .Select(uw => new Pick(uw.Id, uw.WordId))
                .ToListAsync(ct);
            if (picked.Count == 0) return ServiceResult.Ok(new ReviewQuiz([], FromDue: false, TotalDueCount: 0));

            var questions = await QuestionsAsync(userId, picked, ct);
            return ServiceResult.Ok(new ReviewQuiz(questions, FromDue: totalDue > 0, totalDue));
        }

        /// <summary>The longest word looked up: the dictionary's are single words.</summary>
        internal const int MaxWordLength = 50;

        public async Task<ServiceResult<ReviewQuiz>> CreateForAsync(IReadOnlyList<string> words, CancellationToken ct)
        {
            var userId = currentUser.RequireId();
            var today = time.GetUtcNow().UtcDateTime.Date;
            // The dictionary keeps words lowercase (lookups are lowercased).
            var wanted = words
                .Select(w => w.Trim().ToLowerInvariant())
                .Where(w => w.Length is > 0 and <= MaxWordLength)
                .Distinct()
                .Take(MaxQuestions)
                .ToList();

            var mine = Mine(userId);
            var totalDue = await mine.CountAsync(uw => uw.NextReviewDate == null || uw.NextReviewDate <= today, ct);
            List<Found> found = wanted.Count == 0
                ? []
                : await mine
                    .Where(uw => wanted.Contains(uw.Word.WordText))
                    .Select(uw => new Found(
                        uw.Id, uw.WordId, uw.Word.WordText, uw.NextReviewDate == null || uw.NextReviewDate <= today))
                    .ToListAsync(ct);
            // In the order named; a word saved twice is asked once.
            var picked = wanted
                .Select(w => found.FirstOrDefault(f => f.WordText == w))
                .OfType<Found>()
                .ToList();
            if (picked.Count == 0) return ServiceResult.Ok(new ReviewQuiz([], FromDue: false, totalDue, wanted));

            var questions = await QuestionsAsync(userId, [.. picked.Select(p => new Pick(p.Id, p.WordId))], ct);
            return ServiceResult.Ok(new ReviewQuiz(questions, FromDue: picked.All(p => p.Due), totalDue, wanted));
        }

        /// <summary>A saved word found by its text, and whether it is due.</summary>
        private sealed record Found(int Id, string WordId, string WordText, bool Due);

        /// <summary>One of the reader's saved words to ask: the saved row (the answer is recorded against it) and its word.</summary>
        private sealed record Pick(int Id, string WordId);

        /// <summary>The reader's saved words a question can be made of: valid, with a definition.</summary>
        private IQueryable<SQL.UserWord> Mine(string userId) =>
            dbContext.UserWords.Where(uw =>
                uw.UserId == userId &&
                (uw.Word.DataInvalid == null || uw.Word.DataInvalid == false) &&
                uw.Word.Meanings.Any(m => m.Definitions.Any()));

        /// <summary>A question on each word picked, in order, the wrong options from the reader's other words.</summary>
        private async Task<List<ReviewQuizQuestion>> QuestionsAsync(string userId, IReadOnlyList<Pick> picked, CancellationToken ct)
        {
            var targetIds = picked.Select(p => p.WordId).ToList();
            var targets = (await WithMeanings(dbContext.Words.Where(w => targetIds.Contains(w.Id)))
                    .ToListAsync(ct))
                .ToDictionary(w => w.Id);
            // The wrong options: the reader's other saved words first (words they are learning), then, for a small
            // repository, the dictionary's first by id. Both read along an index, never sorting the whole dictionary.
            var others = await WithMeanings(Askable(dbContext.Words)
                    .Where(w => !targetIds.Contains(w.Id) &&
                                dbContext.UserWords.Any(uw => uw.UserId == userId && uw.WordId == w.Id))
                    .OrderBy(w => w.Id)
                    .Take(PoolSize))
                .ToListAsync(ct);
            if (others.Count < MinOwnPool)
            {
                var have = others.Select(w => w.Id).Concat(targetIds).ToList();
                others.AddRange(await WithMeanings(Askable(dbContext.Words)
                        .Where(w => !have.Contains(w.Id))
                        .OrderBy(w => w.Id)
                        .Take(PoolSize - others.Count))
                    .ToListAsync(ct));
            }

            // A word removed between the two reads is left out.
            var asked = picked.Where(p => targets.ContainsKey(p.WordId)).ToList();
            var words = asked.Select(p => targets[p.WordId]).ToList();
            var generated = generator.GenerateFor(words, [.. words, .. others], Random.Shared.Next());

            return generated
                .Select((q, i) => new ReviewQuizQuestion(
                    asked[i].Id, q.Word ?? words[i].WordText, q.Prompt, q.Options, q.CorrectIndex, q.Explanation))
                .ToList();
        }

        /// <summary>Words a question can be made of: valid, with a definition.</summary>
        private static IQueryable<SQL.Word> Askable(IQueryable<SQL.Word> words) =>
            words.Where(w => (w.DataInvalid == null || w.DataInvalid == false) && w.Meanings.Any(m => m.Definitions.Any()));

        private static IQueryable<SQL.Word> WithMeanings(IQueryable<SQL.Word> words) =>
            words
                .Include(w => w.Meanings).ThenInclude(m => m.Definitions)
                .Include(w => w.Meanings).ThenInclude(m => m.ExampleSentences)
                .Include(w => w.Meanings).ThenInclude(m => m.Tags)
                .AsSplitQuery();
    }
}
