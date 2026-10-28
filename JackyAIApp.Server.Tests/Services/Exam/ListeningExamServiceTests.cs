using JackyAIApp.Server.Common;
using JackyAIApp.Server.Services.Portfolio.DrawdownBrake;
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
    /// <summary>TOEIC Part 1 listening questions (<see cref="ListeningExamService"/>).</summary>
    public class ListeningExamServiceTests : IDisposable
    {
        private readonly Mock<ICurrentUser> _currentUserMock = new();
        private readonly Mock<IOpenAIPromptService> _promptServiceMock = new();
        private readonly AzureSQLDBContext _dbContext = new(new DbContextOptionsBuilder<AzureSQLDBContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options);
        private readonly ListeningExamService _service;
        private readonly string _testUserId = "test-user-123";

        public ListeningExamServiceTests()
        {
            _currentUserMock.Setup(x => x.Id).Returns(_testUserId);
            _service = new ListeningExamService(_dbContext, new ExamBankPicker(_dbContext, _currentUserMock.Object));
        }

        public void Dispose() => _dbContext.Dispose();

        [Fact]
        public async Task GenerateListeningExam_EmptyBank_ReturnsError()
        {
            var result = await _service.GenerateAsync();

            Assert.False(result.IsSuccess);
            Assert.Equal(ErrorCodes.NotFound, result.Error);
        }

        [Fact]
        public async Task GenerateListeningExam_ReturnsQuestionWithOptions()
        {
            var listeningTest = await SeedListeningTestAsync();

            var result = await _service.GenerateAsync();

            Assert.True(result.IsSuccess);
            var dto = Assert.IsType<ListeningTestResponse>(result.Data);
            Assert.Equal(listeningTest.Id, dto.Id);
            Assert.Equal(listeningTest.ImageUrl, dto.ImageUrl);
            Assert.Equal(4, dto.Options.Count);
            Assert.Single(dto.Options, o => o.IsCorrect);
        }

        [Fact]
        public async Task GetListeningOptionText_NotFound_ReturnsError()
        {
            var result = await _service.GetOptionTextAsync("missing-id");

            Assert.False(result.IsSuccess);
            Assert.Equal(ErrorCodes.NotFound, result.Error);
        }

        [Fact]
        public async Task GetListeningOptionText_Found_ReturnsText()
        {
            var listeningTest = await SeedListeningTestAsync();
            var option = listeningTest.Options.First();

            var result = await _service.GetOptionTextAsync(option.Id);

            Assert.True(result.IsSuccess);
            Assert.Equal(option.Text, result.Data);
        }

        [Fact]
        public async Task GenerateListeningExam_ExcludesRecentlyAttemptedQuestion()
        {
            var seen = await SeedListeningTestAsync();
            var unseen = await SeedListeningTestAsync();
            await SeedUserAsync();
            _dbContext.ExamAttempts.Add(new ExamAttempt
            {
                UserId = _testUserId,
                ListeningTestId = seen.Id,
                ExamType = "Listening",
                IsCorrect = true,
                CreatedAt = DateTime.UtcNow,
            });
            await _dbContext.SaveChangesAsync();

            var result = await _service.GenerateAsync();

            Assert.True(result.IsSuccess);
            var dto = Assert.IsType<ListeningTestResponse>(result.Data);
            Assert.Equal(unseen.Id, dto.Id);
        }

        [Fact]
        public async Task GetListeningBank_IsNewestFirst_OnePageAtATime_WithTheTotal()
        {
            var oldest = await SeedListeningTestAsync();
            var middle = await SeedListeningTestAsync();
            var newest = await SeedListeningTestAsync();
            var t0 = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
            (oldest.CreatedAt, middle.CreatedAt, newest.CreatedAt) = (t0, t0.AddHours(1), t0.AddHours(2));
            await _dbContext.SaveChangesAsync();

            var first = (await _service.GetBankAsync(1, 2)).Data!;
            var second = (await _service.GetBankAsync(2, 2)).Data!;

            Assert.Equal([newest.Id, middle.Id], first.Items.Select(i => i.Id));
            Assert.Equal([oldest.Id], second.Items.Select(i => i.Id));
            Assert.Equal((3, 3), (first.TotalCount, second.TotalCount));
            Assert.Equal(4, first.Items[0].Options.Count);
        }

        [Fact]
        public async Task GetListeningBank_TakesAPageBeforeTheFirstAsTheFirst_AndCapsItsSize()
        {
            await SeedListeningTestAsync();

            var result = await _service.GetBankAsync(0, int.MaxValue);

            Assert.True(result.IsSuccess);
            Assert.Single(result.Data!.Items);
            Assert.True((await _service.GetBankAsync(-3, 0)).IsSuccess);
        }

        private async Task<ListeningTest> SeedListeningTestAsync()
        {
            var listeningTestId = Guid.NewGuid().ToString();
            var listeningTest = new ListeningTest
            {
                Id = listeningTestId,
                ImageUrl = "https://example.blob.core.windows.net/listening-images/lst-0001.jpg",
                ImagePrompt = "A man pointing at a chart on a whiteboard.",
                CreatedAt = DateTime.UtcNow,
                Options = new List<ListeningTestOption>
                {
                    new() { Id = Guid.NewGuid().ToString(), Text = "A man is pointing at a chart.", IsCorrect = true, Explanation = "正確。", ListeningTestId = listeningTestId },
                    new() { Id = Guid.NewGuid().ToString(), Text = "The people are seated.", IsCorrect = false, Explanation = "錯誤。", ListeningTestId = listeningTestId },
                    new() { Id = Guid.NewGuid().ToString(), Text = "A woman is writing notes.", IsCorrect = false, Explanation = "錯誤。", ListeningTestId = listeningTestId },
                    new() { Id = Guid.NewGuid().ToString(), Text = "The whiteboard is being removed.", IsCorrect = false, Explanation = "錯誤。", ListeningTestId = listeningTestId },
                },
            };
            _dbContext.ListeningTests.Add(listeningTest);
            await _dbContext.SaveChangesAsync();
            return listeningTest;
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
    }
}
