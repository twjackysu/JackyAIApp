using JackyAIApp.Server.DTO;
using SQL = JackyAIApp.Server.Data.Models.SQL;

namespace JackyAIApp.Server.Services.DailyChallenge
{
    /// <summary>
    /// Generates the deterministic (date-seeded) set of daily-challenge questions
    /// from the available word pool. Pure logic — no DB or user state.
    /// </summary>
    public interface IDailyChallengeQuestionGenerator
    {
        List<DailyChallengeQuestion> Generate(List<SQL.Word> allWords, DateTime date);

        /// <summary>
        /// One question about each of <paramref name="targets"/>, in order, of the same kinds as the daily challenge,
        /// the wrong options drawn from <paramref name="pool"/> (more words give better ones); the same seed, the same set.
        /// </summary>
        List<DailyChallengeQuestion> GenerateFor(IReadOnlyList<SQL.Word> targets, IReadOnlyList<SQL.Word> pool, int seed);
    }
}
