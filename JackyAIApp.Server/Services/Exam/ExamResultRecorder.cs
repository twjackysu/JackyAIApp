using JackyAIApp.Server.Common;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.DTO;
using JackyAIApp.Server.Services.SpacedRepetition;
using Microsoft.EntityFrameworkCore;
using SQL = JackyAIApp.Server.Data.Models.SQL;

namespace JackyAIApp.Server.Services.Exam
{
    /// <summary>What happens once the user has answered: the attempt is logged and the words' review schedules move.</summary>
    public interface IExamResultRecorder
    {
        /// <summary>
        /// Records an exam outcome: logs an <see cref="SQL.ExamAttempt"/> and feeds the
        /// word's spaced-repetition schedule (wrong → relearn soon, right → mild boost). Free.
        /// </summary>
        Task<ServiceResult<RecordExamResultResponse>> RecordAsync(RecordExamResultRequest request);
    }

    public class ExamResultRecorder(
        AzureSQLDBContext dbContext,
        ICurrentUser currentUser,
        ISpacedRepetitionService spacedRepetition) : IExamResultRecorder
    {
        private readonly AzureSQLDBContext _dbContext = dbContext;
        private readonly ICurrentUser _currentUser = currentUser;
        private readonly ISpacedRepetitionService _spacedRepetition = spacedRepetition;

        public async Task<ServiceResult<RecordExamResultResponse>> RecordAsync(RecordExamResultRequest request)
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
                // A prompt asks at most RequiredWordCount words: more ids are not a real result, and each one was a query (D-165).
                var wordIds = request.WritingWordIds!.Where(id => !string.IsNullOrEmpty(id)).Distinct()
                    .Take(WritingExamService.RequiredWordCount).ToList();
                var writingWords = await _dbContext.UserWords
                    .Where(uw => uw.UserId == userId && wordIds.Contains(uw.WordId))
                    .ToListAsync();
                foreach (var writingWord in writingWords)
                {
                    _spacedRepetition.ApplyResult(writingWord, quality, DateTime.UtcNow.Date);
                    scheduleUpdated = true;
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
