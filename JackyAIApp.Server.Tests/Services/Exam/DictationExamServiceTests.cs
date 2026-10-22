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
    /// <summary>Dictation practice, graded without AI (<see cref="DictationExamService"/>).</summary>
    public class DictationExamServiceTests : IDisposable
    {
        private readonly Mock<ICurrentUser> _currentUserMock = new();
        private readonly Mock<IOpenAIPromptService> _promptServiceMock = new();
        private readonly AzureSQLDBContext _dbContext = new(new DbContextOptionsBuilder<AzureSQLDBContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options);
        private readonly DictationExamService _service;
        private readonly string _testUserId = "test-user-123";

        public DictationExamServiceTests()
        {
            _currentUserMock.Setup(x => x.Id).Returns(_testUserId);
            _service = new DictationExamService(
                _dbContext,
                _currentUserMock.Object,
                new ExamWordPicker(_dbContext),
                new UserAccountService(
                NullLogger<UserAccountService>.Instance, _dbContext, _currentUserMock.Object, Mock.Of<IDrawdownBrakeAccess>()));
        }

        public void Dispose() => _dbContext.Dispose();

        [Fact]
        public async Task GenerateDictation_UserRowMissing_AsksToSignInAgain()
        {
            var result = await _service.GenerateAsync();

            Assert.False(result.IsSuccess);
            Assert.Equal(ErrorCodes.Unauthorized, result.Error);
        }

        [Fact]
        public async Task GenerateDictation_NoWordsAnywhere_ReturnsError()
        {
            await SeedUserAsync();

            var result = await _service.GenerateAsync();

            Assert.False(result.IsSuccess);
            Assert.Equal(ErrorCodes.Forbidden, result.Error);
        }

        [Fact]
        public async Task GenerateDictation_ReturnsOnlyWordId_NoWordTextAnywhereInResponse()
        {
            var word = await SeedUserWithWordAsync();

            var result = await _service.GenerateAsync();

            Assert.True(result.IsSuccess);
            var dto = Assert.IsType<DictationPromptResponse>(result.Data);
            Assert.Equal(word.Id, dto.WordId);

            // Guards against a future DTO change accidentally leaking the answer: serialize the
            // response the way the API would and confirm the word text never appears in it.
            var json = System.Text.Json.JsonSerializer.Serialize(dto);
            Assert.DoesNotContain(word.WordText, json);
            Assert.Equal(
                new[] { "WordId" },
                dto.GetType().GetProperties().Select(p => p.Name).ToArray());
        }

        [Fact]
        public async Task GetDictationWordText_NotFound_ReturnsError()
        {
            var result = await _service.GetWordTextAsync("missing-id");

            Assert.False(result.IsSuccess);
            Assert.Equal(ErrorCodes.NotFound, result.Error);
        }

        [Fact]
        public async Task GetDictationWordText_Found_ReturnsWordText()
        {
            var word = await SeedUserWithWordAsync();

            var result = await _service.GetWordTextAsync(word.Id);

            Assert.True(result.IsSuccess);
            Assert.Equal(word.WordText, result.Data);
        }

        [Fact]
        public async Task GradeDictation_MissingWordId_ReturnsError()
        {
            var result = await _service.GradeAsync(new DictationGradeRequest { WordId = "", UserInput = "sun" });

            Assert.False(result.IsSuccess);
            Assert.Equal(ErrorCodes.BadRequest, result.Error);
        }

        [Fact]
        public async Task GradeDictation_UnknownWordId_ReturnsNotFound()
        {
            var result = await _service.GradeAsync(new DictationGradeRequest { WordId = "missing-id", UserInput = "sun" });

            Assert.False(result.IsSuccess);
            Assert.Equal(ErrorCodes.NotFound, result.Error);
        }

        [Fact]
        public async Task GradeDictation_ExactMatch_ReturnsCorrectWithFullScore()
        {
            var word = await SeedUserWithWordAsync();

            var result = await _service.GradeAsync(new DictationGradeRequest { WordId = word.Id, UserInput = "sun" });

            Assert.True(result.IsSuccess);
            var dto = Assert.IsType<DictationGradeResponse>(result.Data);
            Assert.True(dto.IsCorrect);
            Assert.Equal(100, dto.Score);
            Assert.Equal("sun", dto.CorrectText);
        }

        [Fact]
        public async Task GradeDictation_CaseDifferenceOnly_StillCorrect()
        {
            var word = await SeedUserWithWordAsync();

            var result = await _service.GradeAsync(new DictationGradeRequest { WordId = word.Id, UserInput = "  SUN  " });

            Assert.True(result.IsSuccess);
            var dto = Assert.IsType<DictationGradeResponse>(result.Data);
            Assert.True(dto.IsCorrect);
            Assert.Equal(100, dto.Score);
        }

        [Fact]
        public async Task GradeDictation_GenuinelyWrong_ReturnsIncorrectWithPartialScore()
        {
            var word = await SeedUserWithWordAsync();

            var result = await _service.GradeAsync(new DictationGradeRequest { WordId = word.Id, UserInput = "son" });

            Assert.True(result.IsSuccess);
            var dto = Assert.IsType<DictationGradeResponse>(result.Data);
            Assert.False(dto.IsCorrect);
            Assert.True(dto.Score > 0 && dto.Score < 100);
            Assert.Equal("sun", dto.CorrectText);
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
