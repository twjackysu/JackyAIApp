using JackyAIApp.Server.Common;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.DTO;
using JackyAIApp.Server.Services.OpenAI;
using JackyAIApp.Server.Services.SpacedRepetition;
using Microsoft.EntityFrameworkCore;
using SQL = JackyAIApp.Server.Data.Models.SQL;

namespace JackyAIApp.Server.Services.Exam
{
    public class ExamService(
        ILogger<ExamService> logger,
        AzureSQLDBContext dbContext,
        ICurrentUser currentUser,
        IOpenAIPromptService promptService,
        ISpacedRepetitionService spacedRepetition,
        IEnumerable<IWordExamGenerator> generators,
        IExamWordPicker wordPicker,
        IUserAccountService accounts) : IExamService
    {
        private readonly ILogger<ExamService> _logger = logger;
        private readonly AzureSQLDBContext _dbContext = dbContext;
        private readonly ICurrentUser _currentUser = currentUser;
        private readonly IOpenAIPromptService _promptService = promptService;
        private readonly ISpacedRepetitionService _spacedRepetition = spacedRepetition;
        private readonly IReadOnlyDictionary<ExamType, IWordExamGenerator> _generators =
            generators.ToDictionary(g => g.Type);
        private readonly IExamWordPicker _wordPicker = wordPicker;
        private readonly IUserAccountService _accounts = accounts;

        private const string NoWordsMessage =
            "No words are available yet to generate an exam from. Try searching a word in the Dictionary first.";

        public async Task<ServiceResult<object>> GenerateWordExamAsync(ExamType type)
        {
            var userId = _currentUser.RequireId();
            if (!await _accounts.ExistsAsync())
            {
                return UserAccountFailures.Missing;
            }

            var randomWordId = await _wordPicker.PickWordIdAsync(userId);
            if (randomWordId == null)
            {
                return ServiceResult.Fail(ErrorCodes.Forbidden, NoWordsMessage);
            }

            var word = await _dbContext.Words
                .Include(w => w.ClozeTests)
                    .ThenInclude(ct => ct.Options)
                .Include(w => w.TranslationTests)
                .Include(w => w.SentenceTests)
                .SingleOrDefaultAsync(x => x.Id == randomWordId);

            if (word == null)
            {
                return ServiceResult.Fail(ErrorCodes.NotFound, "Word not found.");
            }

            if (!_generators.TryGetValue(type, out var generator))
            {
                return ServiceResult.Fail(ErrorCodes.BadRequest, $"Unsupported exam type: {type}");
            }

            return await generator.GenerateAsync(word);
        }

        private static readonly string[] WritingTopics =
        [
            "a typical day in your life",
            "a memorable experience you had",
            "a skill you want to learn and why",
            "your opinion on something you care about",
        ];

        private const string WritingFeature = "exam.writing_evaluation";

        /// <summary>
        /// Serves an open-ended writing prompt: a topic from a fixed rotation plus up to 5 of the
        /// calling user's own saved words. Both plain DB queries — no AI call, so this is
        /// deliberately not usage-limit-gated (unlike every other GET api/exam/* generation endpoint).
        /// </summary>
        public async Task<ServiceResult<WritingPromptResponse>> GenerateWritingPromptAsync()
        {
            var userId = _currentUser.RequireId();
            if (!await _accounts.ExistsAsync())
            {
                return UserAccountFailures.Missing;
            }

            var topic = WritingTopics[Random.Shared.Next(WritingTopics.Length)];
            var words = await _wordPicker.PickSavedWordsAsync(userId, 5);

            return ServiceResult.Ok(new WritingPromptResponse
            {
                Topic = topic,
                RequiredWords = words.Select(w => new WritingPromptWordDto
                {
                    WordId = w.WordId,
                    WordText = w.Word.WordText,
                }).ToList(),
            });
        }

        public async Task<ServiceResult<WritingGradingResponse>> GradeWritingAsync(WritingSubmissionRequest request)
        {
            if (request == null || string.IsNullOrEmpty(request.Topic) || string.IsNullOrEmpty(request.Submission))
            {
                return ServiceResult.Fail(ErrorCodes.BadRequest, "Input cannot be null or empty.");
            }

            var example = (
                User: AiJson.Serialize(new WritingSubmissionRequest
                {
                    Topic = "a memorable experience you had",
                    RequiredWords = new List<string> { "adventure", "journey", "unexpected" },
                    Submission = "Last summer I had an unexpected adventure when my flight got cancelled. " +
                        "It turned into a journey I will never forget, full of new friends and unplanned stops."
                }),
                Assistant: AiJson.Serialize(new WritingGradingResponse
                {
                    Score = 88,
                    GrammarFeedback = "文法大致正確，時態一致，僅有少數用詞可以更道地。",
                    ContentFeedback = "內容切題，段落結構清楚，有開頭、轉折與結尾。",
                    VocabularyFeedback = "三個指定單字都自然地融入句子中，語意恰當。",
                    WordsUsed = new List<WritingWordUsageDto>
                    {
                        new() { Word = "adventure", Used = true },
                        new() { Word = "journey", Used = true },
                        new() { Word = "unexpected", Used = true },
                    },
                    Suggestions = new List<string> { "可以多加一些具體細節，例如地點或時間，讓故事更生動。" }
                })
            );

            var (grading, error) = await _promptService.GetCompletionAsync<WritingGradingResponse>(
                "Prompt/Exam/WritingEvaluationSystem.txt",
                AiJson.Serialize(request),
                model: NewModels.GPT_6_LUNA, examples: new[] { example },
                feature: WritingFeature);

            if (grading == null)
            {
                _logger.LogError("Failed to generate writing evaluation: {Error}", error);
                return ServiceResult.Fail(ErrorCodes.QueryOpenAIFailed,
                    "Query failed, OpenAI could not generate the writing evaluation.");
            }

            _logger.LogInformation("Generated writing evaluation for topic: {Topic}", request.Topic);
            return ServiceResult.Ok(grading);
        }

        /// <summary>
        /// Picks one word via <see cref="IExamWordPicker.PickWordIdAsync"/> (same
        /// due/weak/global-fallback behavior as Cloze/Translation/Sentence) and returns only its
        /// WordId. Deliberately does not include the word text — see DictationPromptResponse.
        /// </summary>
        public async Task<ServiceResult<DictationPromptResponse>> GenerateDictationExamAsync()
        {
            var userId = _currentUser.RequireId();
            if (!await _accounts.ExistsAsync())
            {
                return UserAccountFailures.Missing;
            }

            var wordId = await _wordPicker.PickWordIdAsync(userId);
            if (wordId == null)
            {
                return ServiceResult.Fail(ErrorCodes.Forbidden, NoWordsMessage);
            }

            return ServiceResult.Ok(new DictationPromptResponse { WordId = wordId });
        }

        public async Task<ServiceResult<string>> GetDictationWordTextAsync(string wordId)
        {
            var word = await _dbContext.Words.SingleOrDefaultAsync(w => w.Id == wordId);

            return word == null
                ? ServiceResult.Fail(ErrorCodes.NotFound, "Word not found.")
                : ServiceResult.Ok(word.WordText);
        }

        public async Task<ServiceResult<DictationGradeResponse>> GradeDictationAsync(DictationGradeRequest request)
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

        public async Task<ServiceResult<TranslationQualityGradingAssistantResponse>> GradeTranslationAsync(TranslationTestUserResponse userResponse)
        {
            if (userResponse == null || string.IsNullOrEmpty(userResponse.Translation) ||
                string.IsNullOrEmpty(userResponse.ExaminationQuestion) || string.IsNullOrEmpty(userResponse.UnfamiliarWords))
            {
                return ServiceResult.Fail(ErrorCodes.BadRequest, "Input cannot be null or empty.");
            }

            var example = (
                User: AiJson.Serialize(new TranslationTestUserResponse
                {
                    UnfamiliarWords = "attentively",
                    ExaminationQuestion = "他專注地閱讀著一本有趣的書。",
                    Translation = "He was reading an interesting book attentively."
                }),
                Assistant: AiJson.Serialize(new TranslationQualityGradingAssistantResponse
                {
                    Grade = "A",
                    Reason = "這個翻譯完整保留了原文的所有語意，語法和拼寫完全正確，語句流暢且符合英文的表達習慣。",
                    CorrectedTranslation = "He was reading an interesting book attentively.",
                    WordUsed = true
                })
            );

            var (grading, error) = await _promptService.GetCompletionAsync<TranslationQualityGradingAssistantResponse>(
                "Prompt/Exam/TranslationQualityGradingSystem.txt",
                AiJson.Serialize(userResponse),
                examples: new[] { example });

            if (grading == null)
            {
                _logger.LogError("Failed to generate translation quality grading: {Error}", error);
                return ServiceResult.Fail(ErrorCodes.QueryOpenAIFailed,
                    "Query failed, OpenAI could not generate the corresponding quality grading.");
            }

            _logger.LogInformation("Generated translation quality grading for user response");
            return ServiceResult.Ok(grading);
        }

        public async Task<ServiceResult<ConversationStartResponse>> StartConversationAsync(ConversationStartRequest request)
        {
            if (!await _accounts.ExistsAsync())
            {
                return UserAccountFailures.Missing;
            }

            if (request == null || string.IsNullOrEmpty(request.Scenario) ||
                string.IsNullOrEmpty(request.UserRole) || string.IsNullOrEmpty(request.AiRole))
            {
                return ServiceResult.Fail(ErrorCodes.BadRequest, "Scenario, UserRole, and AiRole are required.");
            }

            // Was previously a raw ChatCompletion call outside IOpenAIPromptService — bypassed
            // the JSON-force/token-cap reliability fixes and, more importantly, the AiUsageLog
            // instrumentation, so this endpoint's cost was completely invisible. Routed through
            // the shared wrapper now. It used gpt-4o-mini at temperature 0.8 for scenario variety;
            // gpt-6-luna is a reasoning model that takes only the default temperature (1), which
            // varies at least as much.
            var (conversationStart, error) = await _promptService.GetCompletionAsync<ConversationStartResponse>(
                "Prompt/Exam/ConversationStartSystem.txt",
                AiJson.Serialize(request),
                model: NewModels.GPT_6_LUNA,
                feature: "exam.conversation_start");

            if (conversationStart == null)
            {
                _logger.LogError("Failed to generate conversation start: {Error}", error);
                return ServiceResult.Fail(ErrorCodes.QueryOpenAIFailed,
                    "Query failed, OpenAI could not generate the conversation scenario.");
            }

            _logger.LogInformation("Generated conversation start for scenario: {Scenario}", request.Scenario);
            return ServiceResult.Ok(conversationStart);
        }

        public async Task<ServiceResult<ConversationResponseResponse>> RespondToConversationAsync(ConversationResponseRequest request)
        {
            if (request == null || string.IsNullOrEmpty(request.UserMessage) || request.ConversationContext == null)
            {
                return ServiceResult.Fail(ErrorCodes.BadRequest, "Invalid conversation request.");
            }

            if (!await _accounts.ExistsAsync())
            {
                return UserAccountFailures.Missing;
            }

            var (response, error) = await _promptService.GetCompletionAsync<ConversationResponseResponse>(
                "Prompt/Exam/ConversationResponseSystem.txt",
                AiJson.Serialize(request));

            if (response == null)
            {
                _logger.LogError("Failed to generate conversation response: {Error}", error);
                return ServiceResult.Fail(ErrorCodes.QueryOpenAIFailed,
                    "Query failed, OpenAI could not generate the conversation response.");
            }

            _logger.LogInformation("Generated conversation response for turn {Turn}", request.ConversationContext.TurnNumber);
            return ServiceResult.Ok(response);
        }

        public async Task<ServiceResult<ConversationSummaryResponse>> SummarizeConversationAsync(ConversationSummaryRequest request)
        {
            if (request == null || request.ConversationContext == null ||
                request.ConversationHistory == null || request.ConversationHistory.Count == 0)
            {
                return ServiceResult.Fail(ErrorCodes.BadRequest, "Invalid conversation summary request.");
            }

            var (summary, error) = await _promptService.GetCompletionAsync<ConversationSummaryResponse>(
                "Prompt/Exam/ConversationSummarySystem.txt",
                AiJson.Serialize(request));

            if (summary == null)
            {
                _logger.LogError("Failed to generate conversation summary: {Error}", error);
                return ServiceResult.Fail(ErrorCodes.QueryOpenAIFailed,
                    "Query failed, OpenAI could not generate the conversation summary.");
            }

            _logger.LogInformation("Generated conversation summary for scenario: {Scenario}", request.ConversationContext.Scenario);
            return ServiceResult.Ok(summary);
        }

        public async Task<ServiceResult<WhisperTranscriptionResponse>> TranscribeAudioAsync(Stream audioStream, string fileName)
        {
            if (!await _accounts.ExistsAsync())
            {
                return UserAccountFailures.Missing;
            }

            var (result, error) = await _promptService.TranscribeAudioAsync(audioStream, fileName);

            if (result == null)
            {
                _logger.LogError("Whisper transcription failed: {Error}", error);
                return ServiceResult.Fail(ErrorCodes.QueryOpenAIFailed, "Failed to transcribe audio.");
            }

            var text = result.Text.Trim();
            var deliveryFeedback = SpeechFluencyAnalyzer.Analyze(text, result.DurationSeconds, result.Words);

            _logger.LogInformation("Whisper transcription successful");
            return ServiceResult.Ok(new WhisperTranscriptionResponse { Text = text, DeliveryFeedback = deliveryFeedback });
        }

        public async Task<ServiceResult<SentenceTestGradingResponse>> EvaluateSentenceAsync(SentenceTestUserResponse userResponse)
        {
            if (userResponse == null || string.IsNullOrEmpty(userResponse.UserSentence) ||
                string.IsNullOrEmpty(userResponse.Word) || string.IsNullOrEmpty(userResponse.Prompt))
            {
                return ServiceResult.Fail(ErrorCodes.BadRequest, "Input cannot be null or empty.");
            }

            var example = (
                User: AiJson.Serialize(new SentenceTestUserResponse
                {
                    Word = "adventure",
                    Prompt = "Create a sentence using the word 'adventure' in the context of travel.",
                    Context = "Travel and exploration",
                    UserSentence = "My adventure to Japan was incredible and full of surprises.",
                    DifficultyLevel = 3,
                    GrammarPattern = "Past tense narrative"
                }),
                Assistant: AiJson.Serialize(new SentenceTestGradingResponse
                {
                    Score = 85,
                    GrammarFeedback = "語法正確，時態使用恰當。",
                    UsageFeedback = "單字 'adventure' 使用正確，完全符合旅遊情境。",
                    CreativityFeedback = "句子表達生動，用詞豐富。",
                    OverallFeedback = "整體表現優秀，句子結構完整，意思清楚。",
                    Suggestions = new List<string> { "可以考慮添加更多細節描述", "嘗試使用更多形容詞來豐富句子" }
                })
            );

            var (evaluation, error) = await _promptService.GetCompletionAsync<SentenceTestGradingResponse>(
                "Prompt/Exam/SentenceEvaluationSystem.txt",
                AiJson.Serialize(userResponse),
                examples: new[] { example });

            if (evaluation == null)
            {
                _logger.LogError("Failed to generate sentence evaluation: {Error}", error);
                return ServiceResult.Fail(ErrorCodes.QueryOpenAIFailed,
                    "Query failed, OpenAI could not generate the sentence evaluation.");
            }

            _logger.LogInformation("Generated sentence evaluation for word: {Word}", userResponse.Word);
            return ServiceResult.Ok(evaluation);
        }

        public async Task<ServiceResult<RecordExamResultResponse>> RecordExamResultAsync(RecordExamResultRequest request)
        {
            var userId = _currentUser.RequireId();
            if (request == null || string.IsNullOrEmpty(request.ExamType))
            {
                return ServiceResult.Fail(ErrorCodes.BadRequest, "ExamType is required.");
            }

            bool isListening = request.ExamType == "Listening";
            bool isListeningResponse = request.ExamType == "ListeningResponse";
            bool isReading = request.ExamType == "Reading";
            bool isWriting = request.ExamType == "Writing";
            bool isWordless = isListening || isListeningResponse || isReading || isWriting;
            if (!isWordless && string.IsNullOrEmpty(request.WordId))
            {
                return ServiceResult.Fail(ErrorCodes.BadRequest, "WordId is required for word-based exam types.");
            }
            if (isListening && string.IsNullOrEmpty(request.ListeningTestId))
            {
                return ServiceResult.Fail(ErrorCodes.BadRequest, "ListeningTestId is required for Listening exam type.");
            }
            if (isListeningResponse && string.IsNullOrEmpty(request.ListeningResponseTestId))
            {
                return ServiceResult.Fail(ErrorCodes.BadRequest, "ListeningResponseTestId is required for ListeningResponse exam type.");
            }
            if (isReading && string.IsNullOrEmpty(request.ReadingPassageId))
            {
                return ServiceResult.Fail(ErrorCodes.BadRequest, "ReadingPassageId is required for Reading exam type.");
            }
            // WritingWordIds must be present (even as an empty list, for a legitimate zero-
            // required-words open-topic submission) — only its absence is a client bug.
            if (isWriting && request.WritingWordIds == null)
            {
                return ServiceResult.Fail(ErrorCodes.BadRequest, "WritingWordIds is required for Writing exam type.");
            }

            // Log the attempt regardless of whether the word is still in the user's repository.
            _dbContext.ExamAttempts.Add(new SQL.ExamAttempt
            {
                UserId = userId,
                WordId = request.WordId,
                ListeningTestId = request.ListeningTestId,
                ListeningResponseTestId = request.ListeningResponseTestId,
                ReadingPassageId = request.ReadingPassageId,
                ExamType = request.ExamType,
                IsCorrect = request.Correct,
                Score = request.Score,
                CreatedAt = DateTime.UtcNow,
            });

            // Listening/Reading exam types aren't word-scoped — no SM-2 vocab schedule to feed.
            // Writing is also wordless at the ExamAttempt level (no single WordId fits a multi-word
            // submission) but still feeds SM-2 — fanned out below across every required word.
            bool scheduleUpdated = false;
            SQL.UserWord? userWord = null;
            if (isWriting)
            {
                // One holistic AI grade for the whole paragraph is applied to every required word's
                // schedule — not a per-word pass/fail (see ARCHITECTURE.md's writing-practice decision).
                var quality = MapQuality(request.Correct, request.Score);
                foreach (var wordId in request.WritingWordIds!)
                {
                    var writingWord = await _dbContext.UserWords
                        .SingleOrDefaultAsync(uw => uw.UserId == userId && uw.WordId == wordId);

                    if (writingWord != null)
                    {
                        _spacedRepetition.ApplyResult(writingWord, quality, DateTime.UtcNow.Date);
                        scheduleUpdated = true;
                    }
                }
            }
            else if (!isWordless)
            {
                // Feed the spaced-repetition schedule for this word, if the user has it saved.
                userWord = await _dbContext.UserWords
                    .SingleOrDefaultAsync(uw => uw.UserId == userId && uw.WordId == request.WordId);

                if (userWord != null)
                {
                    _spacedRepetition.ApplyResult(userWord, MapQuality(request.Correct, request.Score), DateTime.UtcNow.Date);
                    scheduleUpdated = true;
                }
            }

            await _dbContext.SaveChangesAsync();

            return ServiceResult.Ok(new RecordExamResultResponse
            {
                ScheduleUpdated = scheduleUpdated,
                NextReviewDate = userWord?.NextReviewDate,
            });
        }

        /// <summary>
        /// Maps an exam outcome to an SM-2 quality (0–5):
        /// wrong → 2 (relearn soon), correct → 4, correct with a high score (≥85) → 5.
        /// </summary>
        private static int MapQuality(bool correct, int? score)
        {
            if (!correct) return 2;
            return score.HasValue && score.Value >= 85 ? 5 : 4;
        }
    }
}
