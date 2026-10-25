using System.Text.Json;
using JackyAIApp.Server.Common;
using JackyAIApp.Server.DTO;
using JackyAIApp.Server.Services.Connectors;
using JackyAIApp.Server.Services.DailyChallenge;
using JackyAIApp.Server.Services.Dictionary.Assistant;
using Moq;

namespace JackyAIApp.Server.Tests.Services.Dictionary
{
    /// <summary>Studio AI's read of where the reader went wrong in their exams lately (「找出我的弱點」).</summary>
    public class WeakSpotsToolTests
    {
        private static readonly AiToolContext Context = new("u1", "c1");
        private static readonly DateTime At = new(2026, 10, 20, 3, 0, 0, DateTimeKind.Utc);

        private readonly Mock<IStatsService> _stats = new();

        private Task<AiToolResult> RunAsync(object input) =>
            new WeakSpotsTool(_stats.Object).RunAsync(JsonSerializer.SerializeToElement(input), Context, default);

        private void Spots(int days, List<ExamTypeResult> byType, List<WeakWord> words) =>
            _stats.Setup(s => s.GetWeakSpotsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(ServiceResult.Ok(new WeakSpotsResponse
            {
                Days = days, TotalAttempts = byType.Sum(t => t.Attempts), ByType = byType, Words = words,
            }));

        private static JsonElement Card(AiToolResult result) => JsonSerializer.SerializeToElement(result.Card);

        [Fact]
        public async Task WordsAnsweredWrong_AreNamed_WithEachTypesRate_AsATableOfTheWords()
        {
            Spots(30,
                [new() { ExamType = "Cloze", Attempts = 8, Correct = 6 }, new() { ExamType = "Dictation", Attempts = 4, Correct = 1 }],
                [
                    new() { Word = "ephemeral", Attempts = 3, Wrong = 3, LatestCorrect = false, LastWrongAt = At },
                    new() { Word = "candid", Attempts = 2, Wrong = 1, LatestCorrect = true, LastWrongAt = At },
                ]);

            var result = await RunAsync(new { });

            Assert.False(result.IsError);
            Assert.Contains("最近 30 天共作答 12 次，答對 7 次（58%）", result.Content);
            Assert.Contains("克漏字 6/8（75%）；聽寫 1/4（25%）", result.Content);
            Assert.Contains("- ephemeral：3/3；最近一次還是錯", result.Content);
            Assert.Contains("- candid：1/2；最近一次答對了", result.Content);
            Assert.Contains(ReviewQuizTool.Name, result.Content);

            var card = Card(result);
            Assert.Equal("table", card.GetProperty("kind").GetString());
            var rows = card.GetProperty("rows");
            Assert.Equal("ephemeral", rows[0].GetProperty("word").GetString());
            Assert.Equal("答錯", rows[0].GetProperty("latest").GetString());
            Assert.Equal("答對", rows[1].GetProperty("latest").GetString());
            // Every row has a value for every column the card names, so the client can draw it.
            var keys = card.GetProperty("columns").EnumerateArray().Select(c => c.GetProperty("key").GetString()).ToList();
            Assert.All(rows.EnumerateArray(), r => Assert.Equal(keys, r.EnumerateObject().Select(p => p.Name)));
        }

        [Fact]
        public async Task NoWordWrong_ShowsHowEachTypeWent()
        {
            Spots(14, [new() { ExamType = "Reading", Attempts = 3, Correct = 2 }, new() { ExamType = "Mystery", Attempts = 1, Correct = 1 }], []);

            var result = await RunAsync(new { days = 14 });

            Assert.Contains("沒有答錯的字", result.Content);
            var rows = Card(result).GetProperty("rows");
            Assert.Equal("閱讀測驗", rows[0].GetProperty("type").GetString());
            Assert.Equal(67, rows[0].GetProperty("rate").GetDouble());
            Assert.Equal("Mystery", rows[1].GetProperty("type").GetString()); // an unknown type as recorded
        }

        [Fact]
        public async Task NoAttempts_PointsToTheExams()
        {
            Spots(30, [], []);

            var result = await RunAsync(new { });

            Assert.Contains("沒有做過測驗", result.Content);
            var card = Card(result);
            Assert.Equal("link", card.GetProperty("kind").GetString());
            Assert.Equal("/english/exam", card.GetProperty("href").GetString());
        }

        [Fact]
        public async Task TheDaysAsked_GoToTheStats_ElseThirty()
        {
            Spots(30, [], []);

            await RunAsync(new { days = 60 });
            await RunAsync(new { days = "a lot" });

            _stats.Verify(s => s.GetWeakSpotsAsync(60, It.IsAny<CancellationToken>()), Times.Once);
            _stats.Verify(s => s.GetWeakSpotsAsync(WeakSpotsTool.DefaultDays, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task Unreadable_IsAnError()
        {
            _stats.Setup(s => s.GetWeakSpotsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(ServiceResult.Fail(ErrorCodes.NotFound, "x"));

            Assert.True((await RunAsync(new { })).IsError);
        }

        [Fact]
        public void IsARead()
        {
            Assert.Equal(ToolEffect.Read, WeakSpotsTool.Definition.Effect);
        }
    }
}
