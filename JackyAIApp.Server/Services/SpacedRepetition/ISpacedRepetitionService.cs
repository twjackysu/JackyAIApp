using JackyAIApp.Server.Data.Models.SQL;

namespace JackyAIApp.Server.Services.SpacedRepetition
{
    /// <summary>
    /// Shared SM-2 spaced-repetition scheduling. Used by the Review flow and by exam
    /// results so both update a word's review schedule with one implementation.
    /// </summary>
    public interface ISpacedRepetitionService
    {
        /// <summary>
        /// Applies an SM-2 update to <paramref name="userWord"/> in place for a single grading.
        /// </summary>
        /// <param name="userWord">The word's per-user scheduling state (mutated), its last review date included.</param>
        /// <param name="quality">Recall quality 0–5 (≥3 is a pass, &lt;3 resets the interval).</param>
        /// <param name="today">The UTC date the review/exam happened (date only).</param>
        /// <returns>True if the answer counted as correct (quality ≥ 3).</returns>
        bool ApplyResult(UserWord userWord, int quality, DateTime today);
    }
}
