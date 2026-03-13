using JackyAIApp.Server.Common;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.Data.Models.SQL;
using JackyAIApp.Server.DTO;
using JackyAIApp.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

// Alias to resolve ambiguity with DTO.Word
using Word = JackyAIApp.Server.Data.Models.SQL.Word;

namespace JackyAIApp.Server.Controllers
{
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class DailyChallengeController : ControllerBase
    {
        private const int QUESTIONS_PER_CHALLENGE = 5;
        private const int MIN_WORDS_FOR_CHALLENGE = 20; // Minimum words in DB to generate challenge
        private const int OPTIONS_PER_QUESTION = 4;

        private readonly ILogger<DailyChallengeController> _logger;
        private readonly IMyResponseFactory _responseFactory;
        private readonly AzureSQLDBContext _dbContext;
        private readonly IUserService _userService;

        public DailyChallengeController(
            ILogger<DailyChallengeController> logger,
            IMyResponseFactory responseFactory,
            AzureSQLDBContext dbContext,
            IUserService userService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _responseFactory = responseFactory ?? throw new ArgumentNullException(nameof(responseFactory));
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
            _userService = userService ?? throw new ArgumentNullException(nameof(userService));
        }

        /// <summary>
        /// Get today's daily challenge. Same questions for all users on the same day (date-seeded).
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetChallenge()
        {
            var userId = _userService.GetUserId();
            if (userId == null)
                return _responseFactory.CreateErrorResponse(ErrorCodes.Unauthorized, "User not found.");

            var today = DateTime.UtcNow.Date;

            // Check if already completed
            var existingResult = await _dbContext.DailyChallengeResults
                .FirstOrDefaultAsync(r => r.UserId == userId && r.ChallengeDate == today);

            // Get words with enough data for question generation
            var words = await _dbContext.Words
                .Where(w => w.DataInvalid == null || w.DataInvalid == false)
                .Include(w => w.Meanings)
                    .ThenInclude(m => m.Definitions)
                .Include(w => w.Meanings)
                    .ThenInclude(m => m.ExampleSentences)
                .Include(w => w.Meanings)
                    .ThenInclude(m => m.Tags)
                .Where(w => w.Meanings.Any(m => m.Definitions.Any()))
                .ToListAsync();

            if (words.Count < MIN_WORDS_FOR_CHALLENGE)
            {
                return _responseFactory.CreateErrorResponse(ErrorCodes.BadRequest,
                    $"Not enough words in the database to generate a challenge. Need at least {MIN_WORDS_FOR_CHALLENGE}.");
            }

            var questions = GenerateQuestions(words, today);

            var response = new DailyChallengeResponse
            {
                Questions = questions,
                ChallengeDate = today.ToString("yyyy-MM-dd"),
                AlreadyCompleted = existingResult != null,
                PreviousScore = existingResult?.Score
            };

            return _responseFactory.CreateOKResponse(response);
        }

        /// <summary>
        /// Submit answers for today's daily challenge.
        /// </summary>
        [HttpPost("submit")]
        public async Task<IActionResult> SubmitChallenge([FromBody] DailyChallengeSubmitRequest request)
        {
            var userId = _userService.GetUserId();
            if (userId == null)
                return _responseFactory.CreateErrorResponse(ErrorCodes.Unauthorized, "User not found.");

            var today = DateTime.UtcNow.Date;

            // Check if already completed today
            var existingResult = await _dbContext.DailyChallengeResults
                .FirstOrDefaultAsync(r => r.UserId == userId && r.ChallengeDate == today);

            if (existingResult != null)
            {
                return _responseFactory.CreateOKResponse(new DailyChallengeSubmitResponse
                {
                    Score = existingResult.Score,
                    TotalQuestions = existingResult.TotalQuestions,
                    XPEarned = 0,
                    StreakUpdated = false,
                    NewStreak = (await _dbContext.Users.FindAsync(userId))?.CurrentStreak ?? 0,
                    AlreadyCompleted = true
                });
            }

            // Regenerate questions to validate answers
            var words = await _dbContext.Words
                .Where(w => w.DataInvalid == null || w.DataInvalid == false)
                .Include(w => w.Meanings)
                    .ThenInclude(m => m.Definitions)
                .Include(w => w.Meanings)
                    .ThenInclude(m => m.ExampleSentences)
                .Include(w => w.Meanings)
                    .ThenInclude(m => m.Tags)
                .Where(w => w.Meanings.Any(m => m.Definitions.Any()))
                .ToListAsync();

            var questions = GenerateQuestions(words, today);

            // Score the answers
            int score = 0;
            if (request.Answers != null)
            {
                foreach (var answer in request.Answers)
                {
                    var question = questions.FirstOrDefault(q => q.Id == answer.QuestionId);
                    if (question != null && answer.SelectedIndex == question.CorrectIndex)
                    {
                        score++;
                    }
                }
            }

            // Calculate XP: perfect score = 50, otherwise score * 8
            int xpEarned = score == QUESTIONS_PER_CHALLENGE ? 50 : score * 8;

            // Save result
            var result = new DailyChallengeResult
            {
                UserId = userId,
                ChallengeDate = today,
                Score = score,
                TotalQuestions = QUESTIONS_PER_CHALLENGE,
                XPEarned = xpEarned,
                CompletedAt = DateTime.UtcNow
            };
            _dbContext.DailyChallengeResults.Add(result);

            // Update streak
            var user = await _dbContext.Users.FindAsync(userId);
            if (user == null)
                return _responseFactory.CreateErrorResponse(ErrorCodes.Unauthorized, "User not found.");

            bool streakUpdated = false;
            var yesterday = today.AddDays(-1);

            if (user.LastStreakDate == null || user.LastStreakDate < yesterday)
            {
                // Streak broken or first time
                user.CurrentStreak = 1;
                streakUpdated = true;
            }
            else if (user.LastStreakDate == yesterday)
            {
                // Continue streak
                user.CurrentStreak++;
                streakUpdated = true;
            }
            // If LastStreakDate == today, streak already counted (shouldn't happen due to duplicate check above)

            if (streakUpdated)
            {
                user.LastStreakDate = today;
                if (user.CurrentStreak > user.LongestStreak)
                {
                    user.LongestStreak = user.CurrentStreak;
                }
            }

            user.TotalXP += xpEarned;

            await _dbContext.SaveChangesAsync();

            return _responseFactory.CreateOKResponse(new DailyChallengeSubmitResponse
            {
                Score = score,
                TotalQuestions = QUESTIONS_PER_CHALLENGE,
                XPEarned = xpEarned,
                StreakUpdated = streakUpdated,
                NewStreak = user.CurrentStreak,
                AlreadyCompleted = false
            });
        }

        /// <summary>
        /// Get user's daily challenge stats (streak, XP, level).
        /// </summary>
        [HttpGet("stats")]
        public async Task<IActionResult> GetStats()
        {
            var userId = _userService.GetUserId();
            if (userId == null)
                return _responseFactory.CreateErrorResponse(ErrorCodes.Unauthorized, "User not found.");

            var user = await _dbContext.Users.FindAsync(userId);
            if (user == null)
                return _responseFactory.CreateErrorResponse(ErrorCodes.Unauthorized, "User not found.");

            var today = DateTime.UtcNow.Date;
            var todayResult = await _dbContext.DailyChallengeResults
                .FirstOrDefaultAsync(r => r.UserId == userId && r.ChallengeDate == today);

            // Check if streak is still active (not broken)
            int displayStreak = user.CurrentStreak;
            if (user.LastStreakDate.HasValue)
            {
                var daysSinceLastStreak = (today - user.LastStreakDate.Value).Days;
                if (daysSinceLastStreak > 1)
                {
                    // Streak is broken but not yet saved — display 0
                    displayStreak = 0;
                }
            }

            return _responseFactory.CreateOKResponse(new DailyChallengeStatsResponse
            {
                CurrentStreak = displayStreak,
                LongestStreak = user.LongestStreak,
                TotalXP = user.TotalXP,
                Level = GetLevel(user.TotalXP),
                TodayCompleted = todayResult != null,
                TodayScore = todayResult?.Score
            });
        }

        private static string GetLevel(int xp) => xp switch
        {
            >= 5000 => "Master 👑",
            >= 2000 => "Advanced 🎯",
            >= 500 => "Explorer 🧭",
            >= 100 => "Learner 📖",
            _ => "Beginner 🌱"
        };

        /// <summary>
        /// Generate deterministic questions for a given date using the date as a seed.
        /// </summary>
        private List<DailyChallengeQuestion> GenerateQuestions(List<Word> allWords, DateTime date)
        {
            // Use date as seed for deterministic randomness
            int seed = date.Year * 10000 + date.Month * 100 + date.Day;
            var rng = new Random(seed);

            // Shuffle words deterministically
            var shuffled = allWords.OrderBy(_ => rng.Next()).ToList();

            var questions = new List<DailyChallengeQuestion>();
            var questionTypes = new[]
            {
                QuestionType.VocabularyDefinition,
                QuestionType.FillInTheBlank,
                QuestionType.Translation,
                QuestionType.Synonym,
                QuestionType.VocabularyDefinition
            };

            for (int i = 0; i < QUESTIONS_PER_CHALLENGE && i < shuffled.Count; i++)
            {
                var targetWord = shuffled[i];
                var otherWords = shuffled.Where(w => w.Id != targetWord.Id).ToList();
                var type = questionTypes[i % questionTypes.Length];

                // Fall back to VocabularyDefinition if the word doesn't have data for the chosen type
                var question = type switch
                {
                    QuestionType.FillInTheBlank => TryGenerateFillInBlank(i, targetWord, otherWords, rng),
                    QuestionType.Translation => TryGenerateTranslation(i, targetWord, otherWords, rng),
                    QuestionType.Synonym => TryGenerateSynonymOrAntonym(i, targetWord, otherWords, rng, isSynonym: true),
                    QuestionType.Antonym => TryGenerateSynonymOrAntonym(i, targetWord, otherWords, rng, isSynonym: false),
                    _ => null
                };

                // Fallback
                question ??= GenerateVocabularyDefinition(i, targetWord, otherWords, rng);
                questions.Add(question);
            }

            return questions;
        }

        private DailyChallengeQuestion GenerateVocabularyDefinition(int id, Word word, List<Word> others, Random rng)
        {
            var meaning = word.Meanings.FirstOrDefault(m => m.Definitions.Any());
            var definition = meaning?.Definitions.First();
            string correctAnswer = definition?.English ?? "No definition available";
            string explanation = definition?.Chinese ?? "";

            // Get wrong options from other words
            var wrongDefinitions = others
                .SelectMany(w => w.Meanings)
                .SelectMany(m => m.Definitions)
                .Select(d => d.English)
                .Where(d => d != correctAnswer)
                .Distinct()
                .OrderBy(_ => rng.Next())
                .Take(OPTIONS_PER_QUESTION - 1)
                .ToList();

            // Pad if not enough wrong answers
            while (wrongDefinitions.Count < OPTIONS_PER_QUESTION - 1)
                wrongDefinitions.Add($"An unrelated definition #{wrongDefinitions.Count + 1}");

            var options = wrongDefinitions.Append(correctAnswer).OrderBy(_ => rng.Next()).ToList();
            int correctIndex = options.IndexOf(correctAnswer);

            return new DailyChallengeQuestion
            {
                Id = id,
                Type = QuestionType.VocabularyDefinition,
                Prompt = $"What is the meaning of \"{word.WordText}\"?",
                Options = options,
                CorrectIndex = correctIndex,
                Explanation = $"{word.WordText}: {correctAnswer}" + (string.IsNullOrEmpty(explanation) ? "" : $" ({explanation})")
            };
        }

        private DailyChallengeQuestion? TryGenerateFillInBlank(int id, Word word, List<Word> others, Random rng)
        {
            var example = word.Meanings
                .SelectMany(m => m.ExampleSentences)
                .FirstOrDefault(e => e.English.Contains(word.WordText, StringComparison.OrdinalIgnoreCase));

            if (example == null) return null;

            // Replace the word with a blank
            string blankedSentence = example.English.Replace(word.WordText, "_____", StringComparison.OrdinalIgnoreCase);
            if (blankedSentence == example.English) return null; // word wasn't found

            var wrongWords = others
                .Select(w => w.WordText)
                .Where(w => w != word.WordText)
                .Distinct()
                .OrderBy(_ => rng.Next())
                .Take(OPTIONS_PER_QUESTION - 1)
                .ToList();

            while (wrongWords.Count < OPTIONS_PER_QUESTION - 1)
                wrongWords.Add($"word{wrongWords.Count + 1}");

            var options = wrongWords.Append(word.WordText).OrderBy(_ => rng.Next()).ToList();
            int correctIndex = options.IndexOf(word.WordText);

            return new DailyChallengeQuestion
            {
                Id = id,
                Type = QuestionType.FillInTheBlank,
                Prompt = $"Fill in the blank: {blankedSentence}",
                Options = options,
                CorrectIndex = correctIndex,
                Explanation = $"Answer: {word.WordText}. Full sentence: {example.English}" +
                    (string.IsNullOrEmpty(example.Chinese) ? "" : $" ({example.Chinese})")
            };
        }

        private DailyChallengeQuestion? TryGenerateTranslation(int id, Word word, List<Word> others, Random rng)
        {
            var definition = word.Meanings
                .SelectMany(m => m.Definitions)
                .FirstOrDefault(d => !string.IsNullOrEmpty(d.Chinese));

            if (definition == null) return null;

            var wrongWords = others
                .Select(w => w.WordText)
                .Where(w => w != word.WordText)
                .Distinct()
                .OrderBy(_ => rng.Next())
                .Take(OPTIONS_PER_QUESTION - 1)
                .ToList();

            while (wrongWords.Count < OPTIONS_PER_QUESTION - 1)
                wrongWords.Add($"word{wrongWords.Count + 1}");

            var options = wrongWords.Append(word.WordText).OrderBy(_ => rng.Next()).ToList();
            int correctIndex = options.IndexOf(word.WordText);

            return new DailyChallengeQuestion
            {
                Id = id,
                Type = QuestionType.Translation,
                Prompt = $"Which English word means \"{definition.Chinese}\"?",
                Options = options,
                CorrectIndex = correctIndex,
                Explanation = $"{definition.Chinese} = {word.WordText}: {definition.English}"
            };
        }

        private DailyChallengeQuestion? TryGenerateSynonymOrAntonym(int id, Word word, List<Word> others, Random rng, bool isSynonym)
        {
            var tags = word.Meanings
                .SelectMany(m => m.Tags)
                .Where(t => t.TagType == (isSynonym ? "Synonym" : "Antonym"))
                .Select(t => t.Word)
                .Distinct()
                .ToList();

            if (!tags.Any()) return null;

            string correctAnswer = tags.First();
            string label = isSynonym ? "synonym" : "antonym";

            var wrongOptions = others
                .Select(w => w.WordText)
                .Where(w => w != correctAnswer && w != word.WordText)
                .Distinct()
                .OrderBy(_ => rng.Next())
                .Take(OPTIONS_PER_QUESTION - 1)
                .ToList();

            while (wrongOptions.Count < OPTIONS_PER_QUESTION - 1)
                wrongOptions.Add($"word{wrongOptions.Count + 1}");

            var options = wrongOptions.Append(correctAnswer).OrderBy(_ => rng.Next()).ToList();
            int correctIndex = options.IndexOf(correctAnswer);

            return new DailyChallengeQuestion
            {
                Id = id,
                Type = isSynonym ? QuestionType.Synonym : QuestionType.Antonym,
                Prompt = $"Which word is a {label} of \"{word.WordText}\"?",
                Options = options,
                CorrectIndex = correctIndex,
                Explanation = $"{correctAnswer} is a {label} of {word.WordText}"
            };
        }
    }
}
