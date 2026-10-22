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
    /// <summary>Reading comprehension passages (<see cref="ReadingExamService"/>).</summary>
    public class ReadingExamServiceTests : IDisposable
    {
        private readonly Mock<ICurrentUser> _currentUserMock = new();
        private readonly Mock<IOpenAIPromptService> _promptServiceMock = new();
        private readonly AzureSQLDBContext _dbContext = new(new DbContextOptionsBuilder<AzureSQLDBContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options);
        private readonly ReadingExamService _service;
        private readonly string _testUserId = "test-user-123";

        public ReadingExamServiceTests()
        {
            _currentUserMock.Setup(x => x.Id).Returns(_testUserId);
            _service = new ReadingExamService(NullLogger<ReadingExamService>.Instance, _dbContext, _promptServiceMock.Object, new ExamBankPicker(_dbContext, _currentUserMock.Object));
        }

        public void Dispose() => _dbContext.Dispose();

        [Fact]
        public async Task GenerateReading_BankBelowThreshold_GeneratesAndPersists()
        {
            var generated = BuildReadingGenerationResult();
            _promptServiceMock.Setup(x => x.GetCompletionAsync<ReadingGenerationResult>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IEnumerable<(string, string)>>(), It.IsAny<string>()))
                .ReturnsAsync((generated, (string?)null));

            var result = await _service.GenerateAsync();

            Assert.True(result.IsSuccess);
            var dto = Assert.IsType<ReadingPassageResponse>(result.Data);
            Assert.Equal(generated.Title, dto.Title);
            Assert.Equal(generated.PassageText, dto.PassageText);
            Assert.Equal(3, dto.Questions.Count);
            Assert.All(dto.Questions, q => Assert.Single(q.Options, o => o.IsCorrect));

            var saved = await _dbContext.ReadingPassages
                .Include(p => p.Questions).ThenInclude(q => q.Options)
                .SingleAsync();
            Assert.Equal(generated.Title, saved.Title);
            Assert.Equal(3, saved.Questions.Count);
        }

        [Fact]
        public async Task GenerateReading_MalformedQuestions_ReturnsError()
        {
            var generated = new ReadingGenerationResult
            {
                Title = "Bad shape",
                PassageText = "Some passage text.",
                Questions = new List<ReadingQuestionGen>
                {
                    new()
                    {
                        QuestionText = "Only one option?",
                        Options = new List<ReadingQuestionOptionGen>
                        {
                            new() { Text = "Only option", IsCorrect = true, Explanation = "x" },
                        },
                    },
                },
            };
            _promptServiceMock.Setup(x => x.GetCompletionAsync<ReadingGenerationResult>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IEnumerable<(string, string)>>(), It.IsAny<string>()))
                .ReturnsAsync((generated, (string?)null));

            var result = await _service.GenerateAsync();

            Assert.False(result.IsSuccess);
            Assert.Equal(ErrorCodes.QueryOpenAIFailed, result.Error);
            Assert.Empty(await _dbContext.ReadingPassages.ToListAsync());
        }

        [Fact]
        public async Task GenerateReading_ChinesePassage_IsNeverPersisted()
        {
            // The reported bug. The bank is shared and mostly read back rather than regenerated,
            // so persisting one Chinese passage serves it to every user until it is deleted by
            // hand — the write is the part that turns a bad answer into a lasting one.
            var chinese = BuildReadingGenerationResult();
            chinese.PassageText = "上週末，Maria 決定離開城市的喧囂，和兩個朋友開車上山。";
            chinese.Questions[0].QuestionText = "這篇文章主要在講什麼？";
            _promptServiceMock.Setup(x => x.GetCompletionAsync<ReadingGenerationResult>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IEnumerable<(string, string)>>(), It.IsAny<string>()))
                .ReturnsAsync((chinese, (string?)null));

            var result = await _service.GenerateAsync();

            Assert.False(result.IsSuccess);
            Assert.Empty(await _dbContext.ReadingPassages.ToListAsync());
        }

        [Fact]
        public async Task GenerateReading_ChinesePassage_FallsBackToTheBankRatherThanFailing()
        {
            // With something already in the bank the user still gets a usable passage; only the
            // rejected one is thrown away.
            var existing = await SeedReadingPassageAsync();
            var chinese = BuildReadingGenerationResult();
            chinese.PassageText = "整段都是中文的文章。";
            _promptServiceMock.Setup(x => x.GetCompletionAsync<ReadingGenerationResult>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IEnumerable<(string, string)>>(), It.IsAny<string>()))
                .ReturnsAsync((chinese, (string?)null));

            var result = await _service.GenerateAsync();

            Assert.True(result.IsSuccess);
            Assert.Equal(existing.Id, Assert.IsType<ReadingPassageResponse>(result.Data).Id);
            Assert.Single(await _dbContext.ReadingPassages.ToListAsync()); // nothing new saved
        }

        [Fact]
        public async Task GenerateReading_RetriesOnceWhenTheFirstAttemptIsInTheWrongLanguage()
        {
            // Answering in the wrong language is a sampling slip, not a refusal, so one more roll
            // is usually enough and is cheaper than giving up on generating at all.
            var chinese = BuildReadingGenerationResult();
            chinese.PassageText = "整段都是中文的文章。";
            var good = BuildReadingGenerationResult();
            _promptServiceMock.SetupSequence(x => x.GetCompletionAsync<ReadingGenerationResult>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IEnumerable<(string, string)>>(), It.IsAny<string>()))
                .ReturnsAsync((chinese, (string?)null))
                .ReturnsAsync((good, (string?)null));

            var result = await _service.GenerateAsync();

            Assert.True(result.IsSuccess);
            Assert.Equal(good.PassageText, Assert.IsType<ReadingPassageResponse>(result.Data).PassageText);
            Assert.Equal(good.Title, (await _dbContext.ReadingPassages.SingleAsync()).Title);
        }

        [Fact]
        public async Task GenerateReading_DoesNotRetryForever()
        {
            // Two attempts and no more: a model that is going to comply does so on the retry, and
            // past that the credit spend buys nothing the fallback does not already give.
            var chinese = BuildReadingGenerationResult();
            chinese.PassageText = "整段都是中文的文章。";
            _promptServiceMock.Setup(x => x.GetCompletionAsync<ReadingGenerationResult>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IEnumerable<(string, string)>>(), It.IsAny<string>()))
                .ReturnsAsync((chinese, (string?)null));

            await _service.GenerateAsync();

            _promptServiceMock.Verify(x => x.GetCompletionAsync<ReadingGenerationResult>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IEnumerable<(string, string)>>(), It.IsAny<string>()),
                Times.Exactly(2));
        }

        [Fact]
        public async Task GenerateReading_PromptServiceFails_EmptyBank_ReturnsError()
        {
            _promptServiceMock.Setup(x => x.GetCompletionAsync<ReadingGenerationResult>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IEnumerable<(string, string)>>(), It.IsAny<string>()))
                .ReturnsAsync(((ReadingGenerationResult?)null, "OpenAI error"));

            var result = await _service.GenerateAsync();

            Assert.False(result.IsSuccess);
            Assert.Equal(ErrorCodes.QueryOpenAIFailed, result.Error);
        }

        [Fact]
        public async Task GenerateReading_PromptServiceFails_FallsBackToExistingBank()
        {
            var existing = await SeedReadingPassageAsync();
            _promptServiceMock.Setup(x => x.GetCompletionAsync<ReadingGenerationResult>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IEnumerable<(string, string)>>(), It.IsAny<string>()))
                .ReturnsAsync(((ReadingGenerationResult?)null, "OpenAI error"));

            var result = await _service.GenerateAsync();

            Assert.True(result.IsSuccess);
            var dto = Assert.IsType<ReadingPassageResponse>(result.Data);
            Assert.Equal(existing.Id, dto.Id);
        }

        [Fact]
        public async Task GenerateReading_ExcludesRecentlyAttemptedPassage()
        {
            var seen = await SeedReadingPassageAsync();
            var unseen = await SeedReadingPassageAsync();
            await SeedUserAsync();
            _dbContext.ExamAttempts.Add(new ExamAttempt
            {
                UserId = _testUserId,
                ReadingPassageId = seen.Id,
                ExamType = "Reading",
                IsCorrect = true,
                CreatedAt = DateTime.UtcNow,
            });
            await _dbContext.SaveChangesAsync();
            // Bank stays below the growth threshold, so generation is always attempted first;
            // failing it forces the fallback-to-existing-bank path deterministically.
            _promptServiceMock.Setup(x => x.GetCompletionAsync<ReadingGenerationResult>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IEnumerable<(string, string)>>(), It.IsAny<string>()))
                .ReturnsAsync(((ReadingGenerationResult?)null, "OpenAI error"));

            var result = await _service.GenerateAsync();

            Assert.True(result.IsSuccess);
            var dto = Assert.IsType<ReadingPassageResponse>(result.Data);
            Assert.Equal(unseen.Id, dto.Id);
        }

        [Fact]
        public async Task GenerateReading_LargeBankButSmallUnseenPool_ForcesGeneration()
        {
            // 22 total passages is well past the growth threshold (20), but the user has just
            // seen 10 of them — only 12 unseen, which is still below threshold and must force a
            // fresh AI generation rather than reusing from the (mostly-seen) bank.
            var seeded = new List<ReadingPassage>();
            for (var i = 0; i < 22; i++)
            {
                seeded.Add(await SeedReadingPassageAsync());
            }
            await SeedUserAsync();
            foreach (var passage in seeded.Take(10))
            {
                _dbContext.ExamAttempts.Add(new ExamAttempt
                {
                    UserId = _testUserId,
                    ReadingPassageId = passage.Id,
                    ExamType = "Reading",
                    IsCorrect = true,
                    CreatedAt = DateTime.UtcNow,
                });
            }
            await _dbContext.SaveChangesAsync();

            var generated = BuildReadingGenerationResult();
            _promptServiceMock.Setup(x => x.GetCompletionAsync<ReadingGenerationResult>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IEnumerable<(string, string)>>(), It.IsAny<string>()))
                .ReturnsAsync((generated, (string?)null));

            var result = await _service.GenerateAsync();

            Assert.True(result.IsSuccess);
            var dto = Assert.IsType<ReadingPassageResponse>(result.Data);
            Assert.Equal(generated.Title, dto.Title);
            Assert.Equal(23, await _dbContext.ReadingPassages.CountAsync());
        }

        private static ReadingGenerationResult BuildReadingGenerationResult() => new()
        {
            Title = "A Weekend Trip",
            PassageText = "Maria drove to the mountains with two friends for a short weekend hike.",
            Questions = new List<ReadingQuestionGen>
            {
                new()
                {
                    QuestionText = "What is the passage mainly about?",
                    Options = new List<ReadingQuestionOptionGen>
                    {
                        new() { Text = "A hiking trip.", IsCorrect = true, Explanation = "正確。" },
                        new() { Text = "A cooking class.", IsCorrect = false, Explanation = "錯誤。" },
                        new() { Text = "A job interview.", IsCorrect = false, Explanation = "錯誤。" },
                    },
                },
                new()
                {
                    QuestionText = "Who did Maria travel with?",
                    Options = new List<ReadingQuestionOptionGen>
                    {
                        new() { Text = "Two friends.", IsCorrect = true, Explanation = "正確。" },
                        new() { Text = "Her parents.", IsCorrect = false, Explanation = "錯誤。" },
                        new() { Text = "Her coworkers.", IsCorrect = false, Explanation = "錯誤。" },
                    },
                },
                new()
                {
                    QuestionText = "How long was the trip?",
                    Options = new List<ReadingQuestionOptionGen>
                    {
                        new() { Text = "A weekend.", IsCorrect = true, Explanation = "正確。" },
                        new() { Text = "A whole month.", IsCorrect = false, Explanation = "錯誤。" },
                        new() { Text = "A single afternoon.", IsCorrect = false, Explanation = "錯誤。" },
                    },
                },
            },
        };

        private async Task<ReadingPassage> SeedReadingPassageAsync()
        {
            var passageId = Guid.NewGuid().ToString();
            var questionId = Guid.NewGuid().ToString();
            var passage = new ReadingPassage
            {
                Id = passageId,
                Title = "A Weekend Trip",
                PassageText = "Maria drove to the mountains with two friends for a short weekend hike.",
                CreatedAt = DateTime.UtcNow,
                Questions = new List<ReadingQuestion>
                {
                    new()
                    {
                        Id = questionId,
                        QuestionText = "What is the passage mainly about?",
                        PassageId = passageId,
                        Options = new List<ReadingQuestionOption>
                        {
                            new() { Id = Guid.NewGuid().ToString(), Text = "A hiking trip.", IsCorrect = true, Explanation = "正確。", QuestionId = questionId },
                            new() { Id = Guid.NewGuid().ToString(), Text = "A cooking class.", IsCorrect = false, Explanation = "錯誤。", QuestionId = questionId },
                            new() { Id = Guid.NewGuid().ToString(), Text = "A job interview.", IsCorrect = false, Explanation = "錯誤。", QuestionId = questionId },
                        },
                    },
                },
            };
            _dbContext.ReadingPassages.Add(passage);
            await _dbContext.SaveChangesAsync();
            return passage;
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
