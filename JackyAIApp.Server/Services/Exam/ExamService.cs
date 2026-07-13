using Betalgo.Ranul.OpenAI.ObjectModels;
using JackyAIApp.Server.Common;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.DTO;
using JackyAIApp.Server.Services.OpenAI;
using JackyAIApp.Server.Services.SpacedRepetition;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using SQL = JackyAIApp.Server.Data.Models.SQL;

namespace JackyAIApp.Server.Services.Exam
{
    public class ExamService(
        ILogger<ExamService> logger,
        AzureSQLDBContext dbContext,
        IUserService userService,
        IOpenAIPromptService promptService,
        ICreditService creditService,
        ISpacedRepetitionService spacedRepetition,
        IEnumerable<IWordExamGenerator> generators) : IExamService
    {
        private readonly ILogger<ExamService> _logger = logger;
        private readonly AzureSQLDBContext _dbContext = dbContext;
        private readonly IUserService _userService = userService;
        private readonly IOpenAIPromptService _promptService = promptService;
        private readonly ICreditService _creditService = creditService;
        private readonly ISpacedRepetitionService _spacedRepetition = spacedRepetition;
        private readonly IReadOnlyDictionary<ExamType, IWordExamGenerator> _generators =
            generators.ToDictionary(g => g.Type);

        private const string NoWordsMessage =
            "No words are available yet to generate an exam from. Try searching a word in the Dictionary first.";

        public async Task<ServiceResult> GenerateWordExamAsync(ExamType type)
        {
            var userId = _userService.GetUserId();
            var user = await _dbContext.Users.SingleOrDefaultAsync(x => x.Id == userId);
            if (user == null)
            {
                return ServiceResult.Fail(ErrorCodes.Forbidden, "User not found.");
            }

            var randomWordId = await GetRandomUserWordIdAsync(userId!);
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

        // How many of the user's most recent attempts to treat as "just seen" and exclude from
        // re-selection. Sized well past one round (ROUND_SIZE=10 on the frontend) — a user who
        // keeps playing past round boundaries still shouldn't hit a repeat for a while. Paired
        // with ListeningResponseBankGrowthThreshold's "always generate while unseen < threshold"
        // rule, the bank is forced to grow to at least NoRepeatWindow + threshold questions before
        // a repeat becomes possible at all.
        private const int NoRepeatWindow = 30;

        private async Task<HashSet<string>> GetRecentListeningTestIdsAsync(string userId, int take)
        {
            var ids = await _dbContext.ExamAttempts
                .Where(a => a.UserId == userId && a.ListeningTestId != null)
                .OrderByDescending(a => a.CreatedAt)
                .Select(a => a.ListeningTestId!)
                .Take(take * 3)
                .ToListAsync();
            return ids.Distinct().Take(take).ToHashSet();
        }

        public async Task<ServiceResult> GenerateListeningExamAsync()
        {
            var count = await _dbContext.ListeningTests.CountAsync();
            if (count == 0)
            {
                return ServiceResult.Fail(ErrorCodes.NotFound,
                    "No listening questions are available yet.");
            }

            var userId = _userService.GetUserId();
            var excludeIds = string.IsNullOrEmpty(userId)
                ? new HashSet<string>()
                : await GetRecentListeningTestIdsAsync(userId, NoRepeatWindow);

            var poolQuery = _dbContext.ListeningTests.Where(t => !excludeIds.Contains(t.Id));
            var poolCount = await poolQuery.CountAsync();
            // Bank smaller than the exclusion window would empty the pool — fall back to a repeat.
            if (poolCount == 0)
            {
                poolQuery = _dbContext.ListeningTests;
                poolCount = count;
            }

            var skip = Random.Shared.Next(poolCount);
            var test = await poolQuery
                .Include(lt => lt.Options)
                .OrderBy(lt => lt.Id)
                .Skip(skip)
                .FirstAsync();

            return ServiceResult.Ok(new ListeningTestResponse
            {
                Id = test.Id,
                ImageUrl = test.ImageUrl,
                Options = test.Options.Select(o => new ListeningTestOptionDto
                {
                    Id = o.Id,
                    Text = o.Text,
                    IsCorrect = o.IsCorrect,
                    Explanation = o.Explanation,
                }).ToList(),
            });
        }

        public async Task<ServiceResult> GetListeningOptionTextAsync(string optionId)
        {
            var option = await _dbContext.ListeningTestOptions
                .SingleOrDefaultAsync(o => o.Id == optionId);

            return option == null
                ? ServiceResult.Fail(ErrorCodes.NotFound, "Listening question option not found.")
                : ServiceResult.Ok(option.Text);
        }

        public async Task<ServiceResult> GetListeningBankAsync(int pageNumber, int pageSize)
        {
            var totalCount = await _dbContext.ListeningTests.CountAsync();
            var items = await _dbContext.ListeningTests
                .Include(lt => lt.Options)
                .OrderByDescending(lt => lt.CreatedAt)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return ServiceResult.Ok(new ListeningBankPageResponse
            {
                TotalCount = totalCount,
                Items = items.Select(t => new ListeningTestResponse
                {
                    Id = t.Id,
                    ImageUrl = t.ImageUrl,
                    Options = t.Options.Select(o => new ListeningTestOptionDto
                    {
                        Id = o.Id,
                        Text = o.Text,
                        IsCorrect = o.IsCorrect,
                        Explanation = o.Explanation,
                    }).ToList(),
                }).ToList(),
            });
        }

        // Gate generation on the user's *unseen* pool, not the raw bank size — otherwise a user who
        // burns through a small-but-growing bank faster than it fills would start seeing repeats.
        // While unseen < threshold, always generate a fresh Part 2 question; once there's enough
        // unseen backlog, mostly reuse a random one (cheap) with an occasional AI top-up.
        private const int ListeningResponseBankGrowthThreshold = 20;
        private const string ListeningResponseFeature = "exam.listening_response";

        private static readonly (string User, string Assistant) ListeningResponseFewShotExample = (
            User: "scheduling",
            Assistant: JsonConvert.SerializeObject(new ListeningResponseGenerationResult
            {
                Prompt = "When is the quarterly report due?",
                Options =
                [
                    new ListeningResponseOptionGen { Text = "By the end of this Friday.", IsCorrect = true, Explanation = "正確。直接回答了「何時」，給出明確的截止時間。" },
                    new ListeningResponseOptionGen { Text = "In the third-floor conference room.", IsCorrect = false, Explanation = "錯誤。回答的是地點，答非所問（問題問的是時間）。" },
                    new ListeningResponseOptionGen { Text = "Yes, I reported it already.", IsCorrect = false, Explanation = "錯誤。像是回應「report」這個字的聯想，並未回答何時截止。" },
                ],
            })
        );

        private async Task<HashSet<string>> GetRecentListeningResponseTestIdsAsync(string userId, int take)
        {
            var ids = await _dbContext.ExamAttempts
                .Where(a => a.UserId == userId && a.ListeningResponseTestId != null)
                .OrderByDescending(a => a.CreatedAt)
                .Select(a => a.ListeningResponseTestId!)
                .Take(take * 3)
                .ToListAsync();
            return ids.Distinct().Take(take).ToHashSet();
        }

        private async Task<SQL.ListeningResponseTest> PickFromPoolAsync(HashSet<string> excludeIds, int unseenCount, int totalCount)
        {
            var poolQuery = unseenCount > 0
                ? _dbContext.ListeningResponseTests.Where(t => !excludeIds.Contains(t.Id))
                : _dbContext.ListeningResponseTests;
            var poolCount = unseenCount > 0 ? unseenCount : totalCount;

            return await poolQuery
                .Include(t => t.Options)
                .OrderBy(t => t.Id)
                .Skip(Random.Shared.Next(poolCount))
                .FirstAsync();
        }

        public async Task<ServiceResult> GenerateListeningResponseExamAsync()
        {
            var userId = _userService.GetUserId();
            var excludeIds = string.IsNullOrEmpty(userId)
                ? new HashSet<string>()
                : await GetRecentListeningResponseTestIdsAsync(userId, NoRepeatWindow);

            var count = await _dbContext.ListeningResponseTests.CountAsync();
            var unseenCount = await _dbContext.ListeningResponseTests.CountAsync(t => !excludeIds.Contains(t.Id));
            var shouldGenerate = unseenCount < ListeningResponseBankGrowthThreshold || Random.Shared.Next(5) == 0;

            if (!shouldGenerate && unseenCount > 0)
            {
                var existing = await PickFromPoolAsync(excludeIds, unseenCount, count);
                return ServiceResult.Ok(ToListeningResponseDto(existing));
            }

            var scenario = ListeningResponseScenarios[Random.Shared.Next(ListeningResponseScenarios.Length)];
            var (dto, error) = await _promptService.GetCompletionAsync<ListeningResponseGenerationResult>(
                "Prompt/Exam/ListeningResponseSystem.txt", scenario,
                model: NewModels.GPT_4_1_NANO, examples: new[] { ListeningResponseFewShotExample },
                feature: ListeningResponseFeature);

            if (dto == null)
            {
                _logger.LogError("Failed to generate listening response question: {Error}", error);

                // Fall back to an existing one rather than a hard failure, if the bank has any.
                if (count > 0)
                {
                    var fallback = await PickFromPoolAsync(excludeIds, unseenCount, count);
                    return ServiceResult.Ok(ToListeningResponseDto(fallback));
                }

                return ServiceResult.Fail(ErrorCodes.QueryOpenAIFailed,
                    "Query failed, OpenAI could not generate the question.");
            }
            if (dto.Options.Count != 3 || dto.Options.Count(o => o.IsCorrect) != 1)
            {
                _logger.LogError("Listening response generation returned malformed options ({Count} options, {CorrectCount} correct)",
                    dto.Options.Count, dto.Options.Count(o => o.IsCorrect));
                return ServiceResult.Fail(ErrorCodes.QueryOpenAIFailed,
                    "Generated question had an invalid option shape (expected 3 options, 1 correct).");
            }

            var testId = Guid.NewGuid().ToString();
            var newTest = new SQL.ListeningResponseTest
            {
                Id = testId,
                Prompt = dto.Prompt,
                CreatedAt = DateTime.UtcNow,
                Options = dto.Options.Select(o => new SQL.ListeningResponseOption
                {
                    Id = Guid.NewGuid().ToString(),
                    Text = o.Text,
                    IsCorrect = o.IsCorrect,
                    Explanation = o.Explanation,
                    ListeningResponseTestId = testId,
                }).ToList(),
            };

            _dbContext.ListeningResponseTests.Add(newTest);
            await _dbContext.SaveChangesAsync();

            return ServiceResult.Ok(ToListeningResponseDto(newTest));
        }

        private static readonly string[] ListeningResponseScenarios =
        [
            "scheduling", "office supplies", "travel arrangements", "meeting logistics",
            "deliveries and shipping", "IT support", "restaurant reservations", "job interviews",
            "client requests", "building maintenance",
        ];

        private static ListeningResponseTestResponse ToListeningResponseDto(SQL.ListeningResponseTest t) => new()
        {
            Id = t.Id,
            Prompt = t.Prompt,
            Options = t.Options.Select(o => new ListeningResponseOptionDto
            {
                Id = o.Id,
                Text = o.Text,
                IsCorrect = o.IsCorrect,
                Explanation = o.Explanation,
            }).ToList(),
        };

        public async Task<ServiceResult> GetListeningResponsePromptTextAsync(string testId)
        {
            var test = await _dbContext.ListeningResponseTests.SingleOrDefaultAsync(t => t.Id == testId);
            return test == null
                ? ServiceResult.Fail(ErrorCodes.NotFound, "Listening response question not found.")
                : ServiceResult.Ok(test.Prompt);
        }

        public async Task<ServiceResult> GetListeningResponseOptionTextAsync(string optionId)
        {
            var option = await _dbContext.ListeningResponseOptions.SingleOrDefaultAsync(o => o.Id == optionId);
            return option == null
                ? ServiceResult.Fail(ErrorCodes.NotFound, "Listening response option not found.")
                : ServiceResult.Ok(option.Text);
        }

        public async Task<ServiceResult> GradeTranslationAsync(TranslationTestUserResponse userResponse)
        {
            // Credit gate (conditional on an authenticated user)
            var userId = _userService.GetUserId();
            if (!string.IsNullOrEmpty(userId) &&
                !await _creditService.ConsumeCreditsAsync(userId, CreditCosts.TranslationGrading, "translation_grading", "Translation quality grading"))
            {
                return ServiceResult.Fail(ErrorCodes.Forbidden, $"Insufficient credits. Required: {CreditCosts.TranslationGrading}.");
            }

            if (userResponse == null || string.IsNullOrEmpty(userResponse.Translation) ||
                string.IsNullOrEmpty(userResponse.ExaminationQuestion) || string.IsNullOrEmpty(userResponse.UnfamiliarWords))
            {
                return ServiceResult.Fail(ErrorCodes.BadRequest, "Input cannot be null or empty.");
            }

            var example = (
                User: JsonConvert.SerializeObject(new TranslationTestUserResponse
                {
                    UnfamiliarWords = "attentively",
                    ExaminationQuestion = "他專注地閱讀著一本有趣的書。",
                    Translation = "He was reading an interesting book attentively."
                }),
                Assistant: JsonConvert.SerializeObject(new TranslationQualityGradingAssistantResponse
                {
                    Grade = "A",
                    Reason = "這個翻譯完整保留了原文的所有語意，語法和拼寫完全正確，語句流暢且符合英文的表達習慣。",
                    CorrectedTranslation = "He was reading an interesting book attentively.",
                    WordUsed = true
                })
            );

            var (grading, error) = await _promptService.GetCompletionAsync<TranslationQualityGradingAssistantResponse>(
                "Prompt/Exam/TranslationQualityGradingSystem.txt",
                JsonConvert.SerializeObject(userResponse),
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

        public async Task<ServiceResult> StartConversationAsync(ConversationStartRequest request)
        {
            var userId = _userService.GetUserId();
            var user = await _dbContext.Users.SingleOrDefaultAsync(x => x.Id == userId);
            if (user == null)
            {
                return ServiceResult.Fail(ErrorCodes.Forbidden, "User not found.");
            }

            if (request == null || string.IsNullOrEmpty(request.Scenario) ||
                string.IsNullOrEmpty(request.UserRole) || string.IsNullOrEmpty(request.AiRole))
            {
                return ServiceResult.Fail(ErrorCodes.BadRequest, "Scenario, UserRole, and AiRole are required.");
            }

            // Was previously a raw ChatCompletion call outside IOpenAIPromptService — bypassed
            // the JSON-force/token-cap reliability fixes and, more importantly, the AiUsageLog
            // instrumentation, so this endpoint's cost was completely invisible. Routed through
            // the shared wrapper now; Temperature=0.8 (vs. the wrapper's default) is preserved
            // for scenario variety.
            var (conversationStart, error) = await _promptService.GetCompletionAsync<ConversationStartResponse>(
                "Prompt/Exam/ConversationStartSystem.txt",
                JsonConvert.SerializeObject(request),
                model: Models.Gpt_4o_mini,
                feature: "exam.conversation_start",
                temperature: 0.8f);

            if (conversationStart == null)
            {
                _logger.LogError("Failed to generate conversation start: {Error}", error);
                return ServiceResult.Fail(ErrorCodes.QueryOpenAIFailed,
                    "Query failed, OpenAI could not generate the conversation scenario.");
            }

            _logger.LogInformation("Generated conversation start for scenario: {Scenario}", request.Scenario);
            return ServiceResult.Ok(conversationStart);
        }

        public async Task<ServiceResult> RespondToConversationAsync(ConversationResponseRequest request)
        {
            if (request == null || string.IsNullOrEmpty(request.UserMessage) || request.ConversationContext == null)
            {
                return ServiceResult.Fail(ErrorCodes.BadRequest, "Invalid conversation request.");
            }

            var userId = _userService.GetUserId();

            // Credit gate: each conversation turn costs credits
            if (!string.IsNullOrEmpty(userId) &&
                !await _creditService.ConsumeCreditsAsync(userId, CreditCosts.ConversationResponse, "conversation_response", "Conversation turn"))
            {
                return ServiceResult.Fail(ErrorCodes.Forbidden, $"Insufficient credits. Required: {CreditCosts.ConversationResponse}.");
            }

            var user = await _dbContext.Users.SingleOrDefaultAsync(x => x.Id == userId);
            if (user == null)
            {
                return ServiceResult.Fail(ErrorCodes.Forbidden, "User not found.");
            }

            var (response, error) = await _promptService.GetCompletionAsync<ConversationResponseResponse>(
                "Prompt/Exam/ConversationResponseSystem.txt",
                JsonConvert.SerializeObject(request));

            if (response == null)
            {
                _logger.LogError("Failed to generate conversation response: {Error}", error);
                return ServiceResult.Fail(ErrorCodes.QueryOpenAIFailed,
                    "Query failed, OpenAI could not generate the conversation response.");
            }

            _logger.LogInformation("Generated conversation response for turn {Turn}", request.ConversationContext.TurnNumber);
            return ServiceResult.Ok(response);
        }

        public async Task<ServiceResult> SummarizeConversationAsync(ConversationSummaryRequest request)
        {
            if (request == null || request.ConversationContext == null ||
                request.ConversationHistory == null || request.ConversationHistory.Count == 0)
            {
                return ServiceResult.Fail(ErrorCodes.BadRequest, "Invalid conversation summary request.");
            }

            var (summary, error) = await _promptService.GetCompletionAsync<ConversationSummaryResponse>(
                "Prompt/Exam/ConversationSummarySystem.txt",
                JsonConvert.SerializeObject(request));

            if (summary == null)
            {
                _logger.LogError("Failed to generate conversation summary: {Error}", error);
                return ServiceResult.Fail(ErrorCodes.QueryOpenAIFailed,
                    "Query failed, OpenAI could not generate the conversation summary.");
            }

            _logger.LogInformation("Generated conversation summary for scenario: {Scenario}", request.ConversationContext.Scenario);
            return ServiceResult.Ok(summary);
        }

        public async Task<ServiceResult> TranscribeAudioAsync(Stream audioStream, string fileName)
        {
            var userId = _userService.GetUserId();
            var user = await _dbContext.Users.SingleOrDefaultAsync(x => x.Id == userId);
            if (user == null)
            {
                return ServiceResult.Fail(ErrorCodes.Forbidden, "User not found.");
            }

            if (!await _creditService.ConsumeCreditsAsync(userId!, CreditCosts.AudioTranscription, "audio_transcription", "Whisper audio transcription"))
            {
                return ServiceResult.Fail(ErrorCodes.Forbidden, $"Insufficient credits. Required: {CreditCosts.AudioTranscription}.");
            }

            var (transcription, error) = await _promptService.TranscribeAudioAsync(audioStream, fileName);

            if (transcription == null)
            {
                _logger.LogError("Whisper transcription failed: {Error}", error);
                return ServiceResult.Fail(ErrorCodes.QueryOpenAIFailed, "Failed to transcribe audio.");
            }

            _logger.LogInformation("Whisper transcription successful");
            return ServiceResult.Ok(new WhisperTranscriptionResponse { Text = transcription.Trim() });
        }

        public async Task<ServiceResult> EvaluateSentenceAsync(SentenceTestUserResponse userResponse)
        {
            if (userResponse == null || string.IsNullOrEmpty(userResponse.UserSentence) ||
                string.IsNullOrEmpty(userResponse.Word) || string.IsNullOrEmpty(userResponse.Prompt))
            {
                return ServiceResult.Fail(ErrorCodes.BadRequest, "Input cannot be null or empty.");
            }

            var userId = _userService.GetUserId();
            if (!string.IsNullOrEmpty(userId) &&
                !await _creditService.ConsumeCreditsAsync(userId, CreditCosts.SentenceEvaluation, "sentence_evaluation", "Sentence AI evaluation"))
            {
                return ServiceResult.Fail(ErrorCodes.Forbidden, $"Insufficient credits. Required: {CreditCosts.SentenceEvaluation}.");
            }

            var example = (
                User: JsonConvert.SerializeObject(new SentenceTestUserResponse
                {
                    Word = "adventure",
                    Prompt = "Create a sentence using the word 'adventure' in the context of travel.",
                    Context = "Travel and exploration",
                    UserSentence = "My adventure to Japan was incredible and full of surprises.",
                    DifficultyLevel = 3,
                    GrammarPattern = "Past tense narrative"
                }),
                Assistant: JsonConvert.SerializeObject(new SentenceTestGradingResponse
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
                JsonConvert.SerializeObject(userResponse),
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

        public async Task<ServiceResult> RecordExamResultAsync(RecordExamResultRequest request)
        {
            var userId = _userService.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return ServiceResult.Fail(ErrorCodes.Forbidden, "User not found.");
            }

            if (request == null || string.IsNullOrEmpty(request.ExamType))
            {
                return ServiceResult.Fail(ErrorCodes.BadRequest, "ExamType is required.");
            }

            bool isListening = request.ExamType == "Listening";
            bool isListeningResponse = request.ExamType == "ListeningResponse";
            bool isWordless = isListening || isListeningResponse;
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

            // Log the attempt regardless of whether the word is still in the user's repository.
            _dbContext.ExamAttempts.Add(new SQL.ExamAttempt
            {
                UserId = userId,
                WordId = request.WordId,
                ListeningTestId = request.ListeningTestId,
                ListeningResponseTestId = request.ListeningResponseTestId,
                ExamType = request.ExamType,
                IsCorrect = request.Correct,
                Score = request.Score,
                CreatedAt = DateTime.UtcNow,
            });

            // Listening exam types aren't word-scoped — no SM-2 vocab schedule to feed.
            bool scheduleUpdated = false;
            SQL.UserWord? userWord = null;
            if (!isWordless)
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

        /// <summary>
        /// Picks a random WordId from the user's saved words at the database level
        /// (COUNT + OFFSET) instead of loading the whole collection into memory.
        /// </summary>
        /// <summary>
        /// Picks a word for the next exam question, biased toward the SM-2 review schedule
        /// instead of pure uniform random — otherwise exams and the Review flow drift apart
        /// and well-known words get quizzed just as often as weak ones. Falls back to a
        /// random word from the whole dictionary when the user's repository is empty, so
        /// exams work before anyone has favorited anything.
        /// </summary>
        private async Task<string?> GetRandomUserWordIdAsync(string userId)
        {
            var query = _dbContext.UserWords.Where(uw => uw.UserId == userId);
            var count = await query.CountAsync();
            if (count == 0)
            {
                return await GetRandomGlobalWordIdAsync();
            }

            var now = DateTime.UtcNow;

            // Prefer words that are due (or have never been reviewed).
            var dueQuery = query.Where(uw => uw.NextReviewDate == null || uw.NextReviewDate <= now);
            var dueCount = await dueQuery.CountAsync();
            if (dueCount > 0)
            {
                var skip = Random.Shared.Next(dueCount);
                return await dueQuery
                    .OrderBy(uw => uw.WordId)
                    .Skip(skip)
                    .Select(uw => uw.WordId)
                    .FirstOrDefaultAsync();
            }

            // Nothing due yet — surface the weakest words (fewest consecutive-correct
            // streaks) so struggling words still get practiced between scheduled reviews.
            const int weakPoolSize = 10;
            var weakestIds = await query
                .OrderBy(uw => uw.ConsecutiveCorrect)
                .ThenBy(uw => uw.ReviewCount)
                .Select(uw => uw.WordId)
                .Take(Math.Min(weakPoolSize, count))
                .ToListAsync();

            return weakestIds[Random.Shared.Next(weakestIds.Count)];
        }

        /// <summary>Random word from the whole dictionary, used when the user has no repository words yet.</summary>
        private async Task<string?> GetRandomGlobalWordIdAsync()
        {
            var query = _dbContext.Words.Where(w => w.DataInvalid != true);
            var count = await query.CountAsync();
            if (count == 0)
            {
                return null;
            }

            var skip = Random.Shared.Next(count);
            return await query
                .OrderBy(w => w.Id)
                .Skip(skip)
                .Select(w => w.Id)
                .FirstOrDefaultAsync();
        }
    }
}
