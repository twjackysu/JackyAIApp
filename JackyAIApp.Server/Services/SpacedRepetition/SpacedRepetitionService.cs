using JackyAIApp.Server.Data.Models.SQL;

namespace JackyAIApp.Server.Services.SpacedRepetition
{
    /// <summary>
    /// SM-2 implementation shared by the Review flow and exam-result feedback.
    /// Extracted verbatim from the original inline logic in ReviewController so behaviour
    /// is identical across entry points.
    /// </summary>
    public sealed class SpacedRepetitionService : ISpacedRepetitionService
    {
        public const double MIN_EASE_FACTOR = 1.3;
        public const double DEFAULT_EASE_FACTOR = 2.5;

        public bool ApplyResult(UserWord userWord, int quality, DateTime today)
        {
            quality = Math.Clamp(quality, 0, 5);
            userWord.ReviewCount++;

            bool correct = quality >= 3;
            if (correct)
            {
                userWord.ConsecutiveCorrect++;

                // SM-2: update ease factor
                userWord.EaseFactor = Math.Max(
                    MIN_EASE_FACTOR,
                    userWord.EaseFactor + (0.1 - (5 - quality) * (0.08 + (5 - quality) * 0.02))
                );

                // SM-2: update interval
                if (userWord.ConsecutiveCorrect == 1)
                {
                    userWord.ReviewIntervalDays = 1;
                }
                else if (userWord.ConsecutiveCorrect == 2)
                {
                    userWord.ReviewIntervalDays = 6;
                }
                else
                {
                    userWord.ReviewIntervalDays = Math.Round(userWord.ReviewIntervalDays * userWord.EaseFactor, 1);
                }
            }
            else
            {
                // Incorrect answer — reset
                userWord.ConsecutiveCorrect = 0;
                userWord.ReviewIntervalDays = 1;
                userWord.EaseFactor = Math.Max(MIN_EASE_FACTOR, userWord.EaseFactor - 0.2);
            }

            userWord.NextReviewDate = today.AddDays(userWord.ReviewIntervalDays);
            return correct;
        }
    }
}
