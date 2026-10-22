using JackyAIApp.Server.Common;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.Data.Models.SQL;
using JackyAIApp.Server.DTO;
using JackyAIApp.Server.Services;
using JackyAIApp.Server.Services.Exam;
using JackyAIApp.Server.Services.Exam.Generators;
using JackyAIApp.Server.Services.OpenAI;
using JackyAIApp.Server.Services.Portfolio.DrawdownBrake;
using JackyAIApp.Server.Services.SpacedRepetition;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SQLWord = JackyAIApp.Server.Data.Models.SQL.Word;

namespace JackyAIApp.Server.Tests.Services.Exam
{
    /// <summary>Recording an answer: the attempt log and the SM-2 schedules (<see cref="ExamResultRecorder"/>).</summary>
    public class ExamResultRecorderTests : IDisposable
    {
        private readonly Mock<ICurrentUser> _currentUserMock = new();
        private readonly Mock<IOpenAIPromptService> _promptServiceMock = new();
        private readonly AzureSQLDBContext _dbContext = new(new DbContextOptionsBuilder<AzureSQLDBContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options);
        private readonly ExamResultRecorder _service;
        private readonly string _testUserId = "test-user-123";

        public ExamResultRecorderTests()
        {
            _currentUserMock.Setup(x => x.Id).Returns(_testUserId);
            _service = new ExamResultRecorder(
                _dbContext,
                _currentUserMock.Object,
                new SpacedRepetitionService());
        }

        public void Dispose() => _dbContext.Dispose();

        [Fact]
        public async Task RecordExamResult_ListeningType_MissingListeningTestId_ReturnsError()
        {
            await SeedUserAsync();
            var request = new RecordExamResultRequest
            {
                ExamType = "Listening",
                Correct = true,
            };

            var result = await _service.RecordAsync(request);

            Assert.False(result.IsSuccess);
            Assert.Equal(ErrorCodes.BadRequest, result.Error);
        }

        [Fact]
        public async Task RecordExamResult_WordBasedType_MissingWordId_ReturnsError()
        {
            await SeedUserAsync();
            var request = new RecordExamResultRequest
            {
                ExamType = "Cloze",
                Correct = true,
            };

            var result = await _service.RecordAsync(request);

            Assert.False(result.IsSuccess);
            Assert.Equal(ErrorCodes.BadRequest, result.Error);
        }

        [Fact]
        public async Task RecordExamResult_ListeningType_RecordsAttemptWithoutTouchingSM2()
        {
            await SeedUserAsync();
            var request = new RecordExamResultRequest
            {
                ExamType = "Listening",
                ListeningTestId = "lst-1",
                Correct = false,
            };

            var result = await _service.RecordAsync(request);

            Assert.True(result.IsSuccess);
            var dto = Assert.IsType<RecordExamResultResponse>(result.Data);
            Assert.False(dto.ScheduleUpdated);
            Assert.Null(dto.NextReviewDate);

            var attempt = await _dbContext.ExamAttempts.SingleAsync(a => a.UserId == _testUserId);
            Assert.Equal("Listening", attempt.ExamType);
            Assert.Equal("lst-1", attempt.ListeningTestId);
            Assert.Null(attempt.WordId);
            Assert.False(attempt.IsCorrect);
        }

        [Fact]
        public async Task RecordExamResult_ListeningResponseType_MissingId_ReturnsError()
        {
            await SeedUserAsync();
            var request = new RecordExamResultRequest { ExamType = "ListeningResponse", Correct = true };

            var result = await _service.RecordAsync(request);

            Assert.False(result.IsSuccess);
            Assert.Equal(ErrorCodes.BadRequest, result.Error);
        }

        [Fact]
        public async Task RecordExamResult_ListeningResponseType_RecordsAttemptWithoutSM2()
        {
            await SeedUserAsync();
            var request = new RecordExamResultRequest
            {
                ExamType = "ListeningResponse",
                ListeningResponseTestId = "lrt-1",
                Correct = true,
            };

            var result = await _service.RecordAsync(request);

            Assert.True(result.IsSuccess);
            var dto = Assert.IsType<RecordExamResultResponse>(result.Data);
            Assert.False(dto.ScheduleUpdated);

            var attempt = await _dbContext.ExamAttempts.SingleAsync(a => a.UserId == _testUserId);
            Assert.Equal("ListeningResponse", attempt.ExamType);
            Assert.Equal("lrt-1", attempt.ListeningResponseTestId);
            Assert.Null(attempt.WordId);
        }

        [Fact]
        public async Task RecordExamResult_ReadingType_MissingId_ReturnsError()
        {
            await SeedUserAsync();
            var request = new RecordExamResultRequest { ExamType = "Reading", Correct = true };

            var result = await _service.RecordAsync(request);

            Assert.False(result.IsSuccess);
            Assert.Equal(ErrorCodes.BadRequest, result.Error);
        }

        [Fact]
        public async Task RecordExamResult_ReadingType_RecordsAttemptWithoutSM2()
        {
            await SeedUserAsync();
            var request = new RecordExamResultRequest
            {
                ExamType = "Reading",
                ReadingPassageId = "rp-1",
                Correct = true,
            };

            var result = await _service.RecordAsync(request);

            Assert.True(result.IsSuccess);
            var dto = Assert.IsType<RecordExamResultResponse>(result.Data);
            Assert.False(dto.ScheduleUpdated);

            var attempt = await _dbContext.ExamAttempts.SingleAsync(a => a.UserId == _testUserId);
            Assert.Equal("Reading", attempt.ExamType);
            Assert.Equal("rp-1", attempt.ReadingPassageId);
            Assert.Null(attempt.WordId);
        }

        [Fact]
        public async Task RecordExamResult_WritingType_MissingWritingWordIds_ReturnsError()
        {
            await SeedUserAsync();
            var request = new RecordExamResultRequest { ExamType = "Writing", Correct = true };

            var result = await _service.RecordAsync(request);

            Assert.False(result.IsSuccess);
            Assert.Equal(ErrorCodes.BadRequest, result.Error);
        }

        [Fact]
        public async Task RecordExamResult_WritingType_EmptyWritingWordIds_AcceptedAndUpdatesNothing()
        {
            await SeedUserAsync();
            var request = new RecordExamResultRequest
            {
                ExamType = "Writing",
                WritingWordIds = new List<string>(),
                Correct = true,
            };

            var result = await _service.RecordAsync(request);

            Assert.True(result.IsSuccess);
            var dto = Assert.IsType<RecordExamResultResponse>(result.Data);
            Assert.False(dto.ScheduleUpdated);

            var attempt = await _dbContext.ExamAttempts.SingleAsync(a => a.UserId == _testUserId);
            Assert.Equal("Writing", attempt.ExamType);
            Assert.Null(attempt.WordId);
        }

        [Fact]
        public async Task RecordExamResult_WritingType_AppliesSM2ToEveryMatchingRequiredWord()
        {
            var words = await SeedUserWithWordsAsync(3);
            // Third id has no matching UserWord (e.g. removed from the repository since the
            // prompt was fetched) — must be skipped, not error, and not counted toward the update.
            var missingId = Guid.NewGuid().ToString();
            var request = new RecordExamResultRequest
            {
                ExamType = "Writing",
                WritingWordIds = new List<string> { words[0].Id, words[1].Id, missingId },
                Correct = true,
                Score = 90,
            };

            var result = await _service.RecordAsync(request);

            Assert.True(result.IsSuccess);
            var dto = Assert.IsType<RecordExamResultResponse>(result.Data);
            Assert.True(dto.ScheduleUpdated);

            var attempt = await _dbContext.ExamAttempts.SingleAsync(a => a.UserId == _testUserId);
            Assert.Equal("Writing", attempt.ExamType);
            Assert.Null(attempt.WordId);

            var updatedWord0 = await _dbContext.UserWords.SingleAsync(uw => uw.UserId == _testUserId && uw.WordId == words[0].Id);
            var updatedWord1 = await _dbContext.UserWords.SingleAsync(uw => uw.UserId == _testUserId && uw.WordId == words[1].Id);
            var untouchedWord2 = await _dbContext.UserWords.SingleAsync(uw => uw.UserId == _testUserId && uw.WordId == words[2].Id);
            Assert.NotNull(updatedWord0.NextReviewDate);
            Assert.NotNull(updatedWord1.NextReviewDate);
            Assert.Null(untouchedWord2.NextReviewDate);
        }

        [Fact]
        public async Task RecordExamResult_DictationType_MissingWordId_ReturnsError()
        {
            await SeedUserAsync();
            var request = new RecordExamResultRequest { ExamType = "Dictation", Correct = true };

            var result = await _service.RecordAsync(request);

            Assert.False(result.IsSuccess);
            Assert.Equal(ErrorCodes.BadRequest, result.Error);
        }

        [Fact]
        public async Task RecordExamResult_DictationType_UpdatesSM2LikeClozeWould()
        {
            var word = await SeedUserWithWordAsync();
            var request = new RecordExamResultRequest
            {
                ExamType = "Dictation",
                WordId = word.Id,
                Correct = true,
                Score = 100,
            };

            var result = await _service.RecordAsync(request);

            Assert.True(result.IsSuccess);
            var dto = Assert.IsType<RecordExamResultResponse>(result.Data);
            Assert.True(dto.ScheduleUpdated);

            var attempt = await _dbContext.ExamAttempts.SingleAsync(a => a.UserId == _testUserId);
            Assert.Equal("Dictation", attempt.ExamType);
            Assert.Equal(word.Id, attempt.WordId);

            var updatedWord = await _dbContext.UserWords.SingleAsync(uw => uw.UserId == _testUserId && uw.WordId == word.Id);
            Assert.NotNull(updatedWord.NextReviewDate);
        }

        /// <summary>The SM-2 quality an outcome maps to shows in the ease factor (2.5 to start): a wrong
        /// answer is 2 (−0.2), a right one 4 (unchanged), a right one scoring 85 or more 5 (+0.1).</summary>
        [Theory]
        [InlineData(false, null, 2.3)]
        [InlineData(false, 95, 2.3)]
        [InlineData(true, null, 2.5)]
        [InlineData(true, 84, 2.5)]
        [InlineData(true, 85, 2.6)]
        public async Task RecordExamResult_OutcomeMapsToAnSm2Quality(bool correct, int? score, double expectedEase)
        {
            var word = await SeedUserWithWordAsync();

            await _service.RecordAsync(new RecordExamResultRequest { ExamType = "Cloze", WordId = word.Id, Correct = correct, Score = score });

            var updated = await _dbContext.UserWords.SingleAsync(uw => uw.WordId == word.Id);
            Assert.Equal(expectedEase, updated.EaseFactor, 6);
        }

        private async Task<User> SeedUserAsync()
        {
            var user = new User
            {
                Id = _testUserId,
                Name = "Test User",
                Email = "test@example.com",
                LastUpdated = DateTime.UtcNow
            };
            _dbContext.Users.Add(user);
            await _dbContext.SaveChangesAsync();
            return user;
        }

        private async Task<List<SQLWord>> SeedUserWithWordsAsync(int count)
        {
            await SeedUserAsync();

            var words = new List<SQLWord>();
            for (var i = 0; i < count; i++)
            {
                var word = new SQLWord
                {
                    Id = Guid.NewGuid().ToString(),
                    WordText = $"word{i}",
                    KKPhonics = "/wɜːrd/",
                    DateAdded = DateTime.UtcNow,
                    LastUpdated = DateTime.UtcNow
                };
                _dbContext.Words.Add(word);
                _dbContext.UserWords.Add(new UserWord
                {
                    UserId = _testUserId,
                    WordId = word.Id,
                    DateAdded = DateTime.UtcNow
                });
                words.Add(word);
            }
            await _dbContext.SaveChangesAsync();
            return words;
        }

        private async Task<SQLWord> SeedUserWithWordAsync()
        {
            await SeedUserAsync();

            var word = new SQLWord
            {
                Id = Guid.NewGuid().ToString(),
                WordText = "sun",
                KKPhonics = "/sʌn/",
                DateAdded = DateTime.UtcNow,
                LastUpdated = DateTime.UtcNow
            };
            _dbContext.Words.Add(word);
            _dbContext.UserWords.Add(new UserWord
            {
                UserId = _testUserId,
                WordId = word.Id,
                DateAdded = DateTime.UtcNow
            });
            await _dbContext.SaveChangesAsync();
            return word;
        }
    }
}
