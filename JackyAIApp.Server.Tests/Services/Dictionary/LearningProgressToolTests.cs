using System.Text.Json;
using JackyAIApp.Server.Common;
using JackyAIApp.Server.DTO;
using JackyAIApp.Server.Services.Connectors;
using JackyAIApp.Server.Services.DailyChallenge;
using JackyAIApp.Server.Services.Dictionary.Assistant;
using JackyAIApp.Server.Services.SpacedRepetition;
using Moq;

namespace JackyAIApp.Server.Tests.Services.Dictionary
{
    /// <summary>Studio AI's read of where the reader stands in their English learning today.</summary>
    public class LearningProgressToolTests
    {
        private static readonly AiToolContext Context = new("u1", "c1");
        private static readonly JsonElement NoInput = JsonSerializer.SerializeToElement(new { });

        private readonly Mock<IReviewService> _reviews = new();
        private readonly Mock<IDailyChallengeService> _challenge = new();

        private void Due(int count, params string[] words) =>
            _reviews.Setup(r => r.GetDueAsync(It.IsAny<CancellationToken>())).ReturnsAsync(ServiceResult.Ok(new DueReviewsResponse
            {
                TotalDueCount = count,
                DueWords = words.Select(w => new ReviewWordItem { WordText = w, KKPhonics = "", Meanings = [] }).ToList(),
            }));

        private void Stats(bool todayCompleted, int streak = 4) =>
            _challenge.Setup(c => c.GetStatsAsync()).ReturnsAsync(ServiceResult.Ok(new DailyChallengeStatsResponse
            {
                CurrentStreak = streak, LongestStreak = 9, TotalXP = 120, Level = "Lv.3", TodayCompleted = todayCompleted, TodayScore = todayCompleted ? 80 : null,
            }));

        private Task<AiToolResult> RunAsync() => new LearningProgressTool(_reviews.Object, _challenge.Object).RunAsync(NoInput, Context, default);

        private static string Href(AiToolResult result) => JsonSerializer.SerializeToElement(result.Card).GetProperty("href").GetString()!;

        [Fact]
        public async Task WordsDue_AreCountedAndNamed_AndTheCardStartsTheReview()
        {
            Due(12, "serendipity", "ephemeral");
            Stats(todayCompleted: false);

            var result = await RunAsync();

            Assert.Contains("到期該複習的單字：12 個", result.Content);
            Assert.Contains("serendipity, ephemeral", result.Content);
            Assert.Contains("還沒做", result.Content);
            Assert.Contains("連續 4 天", result.Content);
            Assert.Equal("/english/review", Href(result));
        }

        [Fact]
        public async Task NothingDue_PointsToTheDailyChallenge_ThenToTheStats()
        {
            Due(0);
            Stats(todayCompleted: false);
            Assert.Equal("/english/daily", Href(await RunAsync()));

            Stats(todayCompleted: true);
            var done = await RunAsync();
            Assert.Contains("已完成（80 分）", done.Content);
            Assert.Equal("/english/stats", Href(done));
        }

        [Fact]
        public async Task NeitherReadable_IsAnError()
        {
            _reviews.Setup(r => r.GetDueAsync(It.IsAny<CancellationToken>())).ReturnsAsync(ServiceResult.Fail(ErrorCodes.NotFound, "x"));
            _challenge.Setup(c => c.GetStatsAsync()).ReturnsAsync(ServiceResult.Fail(ErrorCodes.NotFound, "x"));

            Assert.True((await RunAsync()).IsError);
        }
    }
}
