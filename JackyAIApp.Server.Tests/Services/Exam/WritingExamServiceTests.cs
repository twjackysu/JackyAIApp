using JackyAIApp.Server.Common;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.Data.Models.SQL;
using JackyAIApp.Server.DTO;
using JackyAIApp.Server.Services;
using JackyAIApp.Server.Services.Exam;
using JackyAIApp.Server.Services.Exam.Generators;
using JackyAIApp.Server.Services.OpenAI;
using JackyAIApp.Server.Services.SpacedRepetition;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SQLWord = JackyAIApp.Server.Data.Models.SQL.Word;

namespace JackyAIApp.Server.Tests.Services.Exam
{
    /// <summary>Writing practice (<see cref="WritingExamService"/>).</summary>
    public class WritingExamServiceTests : IDisposable
    {
        private readonly Mock<ICurrentUser> _currentUserMock = new();
        private readonly Mock<IOpenAIPromptService> _promptServiceMock = new();
        private readonly AzureSQLDBContext _dbContext = new(new DbContextOptionsBuilder<AzureSQLDBContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options);
        private readonly WritingExamService _service;
        private readonly string _testUserId = "test-user-123";

        public WritingExamServiceTests()
        {
            _currentUserMock.Setup(x => x.Id).Returns(_testUserId);
            _service = new WritingExamService(
                NullLogger<WritingExamService>.Instance,
                _currentUserMock.Object,
                _promptServiceMock.Object,
                new ExamWordPicker(_dbContext),
                new UserAccountService(
                NullLogger<UserAccountService>.Instance, _dbContext, _currentUserMock.Object, Mock.Of<IDrawdownBrakeAccess>()));
        }

        public void Dispose() => _dbContext.Dispose();

        [Fact]
        public async Task GenerateWritingPrompt_UserRowMissing_AsksToSignInAgain()
        {
            var result = await _service.GeneratePromptAsync();

            Assert.False(result.IsSuccess);
            Assert.Equal(ErrorCodes.Unauthorized, result.Error);
        }

        [Fact]
        public async Task GenerateWritingPrompt_EmptyRepository_ReturnsPromptWithNoRequiredWords()
        {
            await SeedUserAsync();

            var result = await _service.GeneratePromptAsync();

            Assert.True(result.IsSuccess);
            var dto = Assert.IsType<WritingPromptResponse>(result.Data);
            Assert.NotNull(dto.Topic);
            Assert.Empty(dto.RequiredWords);
        }

        [Fact]
        public async Task GenerateWritingPrompt_FewerThanFiveWords_ReturnsAllOfThem()
        {
            var words = await SeedUserWithWordsAsync(3);

            var result = await _service.GeneratePromptAsync();

            Assert.True(result.IsSuccess);
            var dto = Assert.IsType<WritingPromptResponse>(result.Data);
            Assert.Equal(3, dto.RequiredWords.Count);
            Assert.Equal(
                words.Select(w => w.Id).OrderBy(x => x),
                dto.RequiredWords.Select(w => w.WordId).OrderBy(x => x));
        }

        [Fact]
        public async Task GenerateWritingPrompt_MoreThanFiveWords_ReturnsAtMostFiveDistinct()
        {
            await SeedUserWithWordsAsync(8);

            var result = await _service.GeneratePromptAsync();

            Assert.True(result.IsSuccess);
            var dto = Assert.IsType<WritingPromptResponse>(result.Data);
            Assert.Equal(5, dto.RequiredWords.Count);
            Assert.Equal(5, dto.RequiredWords.Select(w => w.WordId).Distinct().Count());
        }

        [Fact]
        public async Task GradeWriting_NullInput_ReturnsError()
        {
            var result = await _service.GradeAsync(null!);

            Assert.False(result.IsSuccess);
            Assert.Equal(ErrorCodes.BadRequest, result.Error);
        }

        [Fact]
        public async Task GradeWriting_ReflectsUnusedRequiredWord()
        {
            await SeedUserAsync();
            var expected = new WritingGradingResponse
            {
                Score = 60,
                GrammarFeedback = "尚可。",
                ContentFeedback = "切題。",
                VocabularyFeedback = "有一個單字沒有使用到。",
                WordsUsed = new List<WritingWordUsageDto>
                {
                    new() { Word = "adventure", Used = true },
                    new() { Word = "journey", Used = false },
                },
                Suggestions = new List<string> { "試著用上 journey 這個字。" },
            };
            _promptServiceMock.Setup(x => x.GetCompletionAsync<WritingGradingResponse>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IEnumerable<(string, string)>>(), It.IsAny<string>()))
                .ReturnsAsync((expected, (string?)null));

            var request = new WritingSubmissionRequest
            {
                Topic = "a memorable experience you had",
                RequiredWords = new List<string> { "adventure", "journey" },
                Submission = "My adventure last year was amazing.",
            };

            var result = await _service.GradeAsync(request);

            Assert.True(result.IsSuccess);
            var dto = Assert.IsType<WritingGradingResponse>(result.Data);
            Assert.Equal(2, dto.WordsUsed.Count);
            Assert.True(dto.WordsUsed.Single(w => w.Word == "adventure").Used);
            Assert.False(dto.WordsUsed.Single(w => w.Word == "journey").Used);
        }

        [Fact]
        public async Task GradeWriting_PromptServiceFails_ReturnsError()
        {
            await SeedUserAsync();
            _promptServiceMock.Setup(x => x.GetCompletionAsync<WritingGradingResponse>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IEnumerable<(string, string)>>(), It.IsAny<string>()))
                .ReturnsAsync(((WritingGradingResponse?)null, "OpenAI error"));

            var request = new WritingSubmissionRequest
            {
                Topic = "topic",
                RequiredWords = new List<string>(),
                Submission = "text",
            };

            var result = await _service.GradeAsync(request);

            Assert.False(result.IsSuccess);
            Assert.Equal(ErrorCodes.QueryOpenAIFailed, result.Error);
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
    }
}
