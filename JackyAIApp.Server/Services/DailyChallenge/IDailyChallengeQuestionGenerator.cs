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
    }
}
