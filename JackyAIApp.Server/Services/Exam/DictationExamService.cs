using JackyAIApp.Server.Common;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.DTO;
using Microsoft.EntityFrameworkCore;

namespace JackyAIApp.Server.Services.Exam
{
    /// <summary>Dictation: hear one of the user's words and type it, graded here without AI.</summary>
    public interface IDictationExamService
    {
        /// <summary>
        /// Picks one word (the same due/weak/global-fallback choice as the vocabulary exams,
        /// <see cref="IExamWordPicker"/>) for dictation practice. Returns only the WordId — unlike
        /// Cloze/Translation, the word text is deliberately withheld since the whole exercise is
        /// identifying it from audio alone.
        /// </summary>
        Task<ServiceResult<DictationPromptResponse>> GenerateAsync();

        /// <summary>Looks up a dictation word's text, for the unbilled audio playback endpoint
        /// (usage was already charged when the prompt itself was fetched).</summary>
        Task<ServiceResult<string>> GetWordTextAsync(string wordId);

        /// <summary>
        /// Grades a dictation submission against the real word text looked up server-side (never
        /// trust a client-supplied answer). Zero underlying cost (in-memory string comparison),
        /// so deliberately not usage-limit-gated.
        /// </summary>
        Task<ServiceResult<DictationGradeResponse>> GradeAsync(DictationGradeRequest request);
    }

    public class DictationExamService(
        AzureSQLDBContext dbContext,
        ICurrentUser currentUser,
        IExamWordPicker wordPicker,
        IUserAccountService accounts) : IDictationExamService
    {
        private readonly AzureSQLDBContext _dbContext = dbContext;
        private readonly ICurrentUser _currentUser = currentUser;
        private readonly IExamWordPicker _wordPicker = wordPicker;
        private readonly IUserAccountService _accounts = accounts;

        /// <summary>
        /// Picks one word via <see cref="IExamWordPicker.PickWordIdAsync"/> (same
        /// due/weak/global-fallback behavior as Cloze/Translation/Sentence) and returns only its
        /// WordId. Deliberately does not include the word text — see DictationPromptResponse.
        /// </summary>
        public async Task<ServiceResult<DictationPromptResponse>> GenerateAsync()
        {
            var userId = _currentUser.RequireId();
            if (!await _accounts.ExistsAsync())
            {
                return UserAccountFailures.Missing;
            }

            var wordId = await _wordPicker.PickWordIdAsync(userId);
            if (wordId == null)
            {
                return ExamWordPicker.NoWords;
            }

            return ServiceResult.Ok(new DictationPromptResponse { WordId = wordId });
        }

        public async Task<ServiceResult<string>> GetWordTextAsync(string wordId)
        {
            var word = await _dbContext.Words.SingleOrDefaultAsync(w => w.Id == wordId);

            return word == null
                ? ServiceResult.Fail(ErrorCodes.NotFound, "Word not found.")
                : ServiceResult.Ok(word.WordText);
        }

        public async Task<ServiceResult<DictationGradeResponse>> GradeAsync(DictationGradeRequest request)
        {
            if (request == null || string.IsNullOrEmpty(request.WordId))
            {
                return ServiceResult.Fail(ErrorCodes.BadRequest, "WordId is required.");
            }

            // Never trust a client-supplied "correct answer" — resolve it server-side.
            var word = await _dbContext.Words.SingleOrDefaultAsync(w => w.Id == request.WordId);
            if (word == null)
            {
                return ServiceResult.Fail(ErrorCodes.NotFound, "Word not found.");
            }

            var correctText = word.WordText;
            var userInput = (request.UserInput ?? string.Empty).Trim();
            var isCorrect = string.Equals(userInput, correctText, StringComparison.OrdinalIgnoreCase);

            // Not [UsageLimit] — pure in-memory string comparison, no AI call, no TTS call.
            // The flat charge on GET api/exam/dictation already covers the one TTS clip this
            // attempt used. Do not add usage consumption here.
            int score;
            if (isCorrect)
            {
                score = 100;
            }
            else
            {
                var distance = LevenshteinDistance(userInput.ToUpperInvariant(), correctText.ToUpperInvariant());
                var maxLength = Math.Max(correctText.Length, 1);
                score = Math.Max(0, (int)Math.Round(100.0 * (1 - (double)distance / maxLength)));
            }

            return ServiceResult.Ok(new DictationGradeResponse
            {
                CorrectText = correctText,
                IsCorrect = isCorrect,
                Score = score,
            });
        }

        /// <summary>Standard O(n*m) edit-distance DP. Word-length strings only, no performance concern.</summary>
        private static int LevenshteinDistance(string a, string b)
        {
            var n = a.Length;
            var m = b.Length;
            var dp = new int[n + 1, m + 1];

            for (var i = 0; i <= n; i++) dp[i, 0] = i;
            for (var j = 0; j <= m; j++) dp[0, j] = j;

            for (var i = 1; i <= n; i++)
            {
                for (var j = 1; j <= m; j++)
                {
                    var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    dp[i, j] = Math.Min(
                        Math.Min(dp[i - 1, j] + 1, dp[i, j - 1] + 1),
                        dp[i - 1, j - 1] + cost);
                }
            }

            return dp[n, m];
        }
    }
}
