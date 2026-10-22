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
    /// <summary>TOEIC Part 2 listening questions (<see cref="ListeningResponseExamService"/>).</summary>
    public class ListeningResponseExamServiceTests : IDisposable
    {
        private readonly Mock<ICurrentUser> _currentUserMock = new();
        private readonly Mock<IOpenAIPromptService> _promptServiceMock = new();
        private readonly AzureSQLDBContext _dbContext = new(new DbContextOptionsBuilder<AzureSQLDBContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options);
        private readonly ListeningResponseExamService _service;
        private readonly string _testUserId = "test-user-123";

        public ListeningResponseExamServiceTests()
        {
            _currentUserMock.Setup(x => x.Id).Returns(_testUserId);
            _service = new ListeningResponseExamService(NullLogger<ListeningResponseExamService>.Instance, _dbContext, _promptServiceMock.Object, new ExamBankPicker(_dbContext, _currentUserMock.Object));
        }

        public void Dispose() => _dbContext.Dispose();

        [Fact]
        public async Task GenerateListeningResponse_BankBelowThreshold_GeneratesAndPersists()
        {
            var generated = new ListeningResponseGenerationResult
            {
                Prompt = "When is the quarterly report due?",
                Options = new List<ListeningResponseOptionGen>
                {
                    new() { Text = "By the end of this Friday.", IsCorrect = true, Explanation = "正確。" },
                    new() { Text = "In the third-floor conference room.", IsCorrect = false, Explanation = "錯誤。" },
                    new() { Text = "Yes, I reported it already.", IsCorrect = false, Explanation = "錯誤。" },
                },
            };
            _promptServiceMock.Setup(x => x.GetCompletionAsync<ListeningResponseGenerationResult>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IEnumerable<(string, string)>>(), It.IsAny<string>()))
                .ReturnsAsync((generated, (string?)null));

            var result = await _service.GenerateAsync();

            Assert.True(result.IsSuccess);
            var dto = Assert.IsType<ListeningResponseTestResponse>(result.Data);
            Assert.Equal(generated.Prompt, dto.Prompt);
            Assert.Equal(3, dto.Options.Count);
            Assert.Single(dto.Options, o => o.IsCorrect);

            var saved = await _dbContext.ListeningResponseTests.Include(t => t.Options).SingleAsync();
            Assert.Equal(generated.Prompt, saved.Prompt);
            Assert.Equal(3, saved.Options.Count);
        }

        [Fact]
        public async Task GenerateListeningResponse_MalformedOptions_ReturnsError()
        {
            var generated = new ListeningResponseGenerationResult
            {
                Prompt = "Bad shape",
                Options = new List<ListeningResponseOptionGen>
                {
                    new() { Text = "Only one option", IsCorrect = true, Explanation = "x" },
                },
            };
            _promptServiceMock.Setup(x => x.GetCompletionAsync<ListeningResponseGenerationResult>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IEnumerable<(string, string)>>(), It.IsAny<string>()))
                .ReturnsAsync((generated, (string?)null));

            var result = await _service.GenerateAsync();

            Assert.False(result.IsSuccess);
            Assert.Equal(ErrorCodes.QueryOpenAIFailed, result.Error);
            Assert.Empty(await _dbContext.ListeningResponseTests.ToListAsync());
        }

        [Fact]
        public async Task GenerateListeningResponse_PromptServiceFails_EmptyBank_ReturnsError()
        {
            _promptServiceMock.Setup(x => x.GetCompletionAsync<ListeningResponseGenerationResult>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IEnumerable<(string, string)>>(), It.IsAny<string>()))
                .ReturnsAsync(((ListeningResponseGenerationResult?)null, "OpenAI error"));

            var result = await _service.GenerateAsync();

            Assert.False(result.IsSuccess);
            Assert.Equal(ErrorCodes.QueryOpenAIFailed, result.Error);
        }

        [Fact]
        public async Task GenerateListeningResponse_PromptServiceFails_FallsBackToExistingBank()
        {
            var existing = await SeedListeningResponseTestAsync();
            _promptServiceMock.Setup(x => x.GetCompletionAsync<ListeningResponseGenerationResult>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IEnumerable<(string, string)>>(), It.IsAny<string>()))
                .ReturnsAsync(((ListeningResponseGenerationResult?)null, "OpenAI error"));

            var result = await _service.GenerateAsync();

            Assert.True(result.IsSuccess);
            var dto = Assert.IsType<ListeningResponseTestResponse>(result.Data);
            Assert.Equal(existing.Id, dto.Id);
        }

        [Fact]
        public async Task GetListeningResponsePromptText_NotFound_ReturnsError()
        {
            var result = await _service.GetPromptTextAsync("missing-id");

            Assert.False(result.IsSuccess);
            Assert.Equal(ErrorCodes.NotFound, result.Error);
        }

        [Fact]
        public async Task GetListeningResponsePromptText_Found_ReturnsText()
        {
            var test = await SeedListeningResponseTestAsync();

            var result = await _service.GetPromptTextAsync(test.Id);

            Assert.True(result.IsSuccess);
            Assert.Equal(test.Prompt, result.Data);
        }

        [Fact]
        public async Task GetListeningResponseOptionText_Found_ReturnsText()
        {
            var test = await SeedListeningResponseTestAsync();
            var option = test.Options.First();

            var result = await _service.GetOptionTextAsync(option.Id);

            Assert.True(result.IsSuccess);
            Assert.Equal(option.Text, result.Data);
        }

        [Fact]
        public async Task GenerateListeningResponse_ExcludesRecentlyAttemptedQuestion()
        {
            var seen = await SeedListeningResponseTestAsync();
            var unseen = await SeedListeningResponseTestAsync();
            await SeedUserAsync();
            _dbContext.ExamAttempts.Add(new ExamAttempt
            {
                UserId = _testUserId,
                ListeningResponseTestId = seen.Id,
                ExamType = "ListeningResponse",
                IsCorrect = true,
                CreatedAt = DateTime.UtcNow,
            });
            await _dbContext.SaveChangesAsync();
            // Bank stays below the growth threshold, so generation is always attempted first;
            // failing it forces the fallback-to-existing-bank path deterministically.
            _promptServiceMock.Setup(x => x.GetCompletionAsync<ListeningResponseGenerationResult>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IEnumerable<(string, string)>>(), It.IsAny<string>()))
                .ReturnsAsync(((ListeningResponseGenerationResult?)null, "OpenAI error"));

            var result = await _service.GenerateAsync();

            Assert.True(result.IsSuccess);
            var dto = Assert.IsType<ListeningResponseTestResponse>(result.Data);
            Assert.Equal(unseen.Id, dto.Id);
        }

        [Fact]
        public async Task GenerateListeningResponse_LargeBankButSmallUnseenPool_ForcesGeneration()
        {
            // 22 total questions is well past the growth threshold (20), but the user has just
            // seen 10 of them — only 12 unseen, which is still below threshold and must force a
            // fresh AI generation rather than reusing from the (mostly-seen) bank.
            var seeded = new List<ListeningResponseTest>();
            for (var i = 0; i < 22; i++)
            {
                seeded.Add(await SeedListeningResponseTestAsync());
            }
            await SeedUserAsync();
            foreach (var test in seeded.Take(10))
            {
                _dbContext.ExamAttempts.Add(new ExamAttempt
                {
                    UserId = _testUserId,
                    ListeningResponseTestId = test.Id,
                    ExamType = "ListeningResponse",
                    IsCorrect = true,
                    CreatedAt = DateTime.UtcNow,
                });
            }
            await _dbContext.SaveChangesAsync();

            var generated = new ListeningResponseGenerationResult
            {
                Prompt = "Freshly generated prompt",
                Options = new List<ListeningResponseOptionGen>
                {
                    new() { Text = "Correct reply.", IsCorrect = true, Explanation = "正確。" },
                    new() { Text = "Wrong reply A.", IsCorrect = false, Explanation = "錯誤。" },
                    new() { Text = "Wrong reply B.", IsCorrect = false, Explanation = "錯誤。" },
                },
            };
            _promptServiceMock.Setup(x => x.GetCompletionAsync<ListeningResponseGenerationResult>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IEnumerable<(string, string)>>(), It.IsAny<string>()))
                .ReturnsAsync((generated, (string?)null));

            var result = await _service.GenerateAsync();

            Assert.True(result.IsSuccess);
            var dto = Assert.IsType<ListeningResponseTestResponse>(result.Data);
            Assert.Equal(generated.Prompt, dto.Prompt);
            Assert.Equal(23, await _dbContext.ListeningResponseTests.CountAsync());
        }

        private async Task<ListeningResponseTest> SeedListeningResponseTestAsync()
        {
            var testId = Guid.NewGuid().ToString();
            var test = new ListeningResponseTest
            {
                Id = testId,
                Prompt = "When is the quarterly report due?",
                CreatedAt = DateTime.UtcNow,
                Options = new List<ListeningResponseOption>
                {
                    new() { Id = Guid.NewGuid().ToString(), Text = "By the end of this Friday.", IsCorrect = true, Explanation = "正確。", ListeningResponseTestId = testId },
                    new() { Id = Guid.NewGuid().ToString(), Text = "In the third-floor conference room.", IsCorrect = false, Explanation = "錯誤。", ListeningResponseTestId = testId },
                    new() { Id = Guid.NewGuid().ToString(), Text = "Yes, I reported it already.", IsCorrect = false, Explanation = "錯誤。", ListeningResponseTestId = testId },
                },
            };
            _dbContext.ListeningResponseTests.Add(test);
            await _dbContext.SaveChangesAsync();
            return test;
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
