using System.Text.Json;
using JackyAIApp.Server.Common;
using JackyAIApp.Server.Services.Connectors;
using JackyAIApp.Server.Services.Dictionary.Assistant;
using JackyAIApp.Server.Services.SpacedRepetition;
using Moq;

namespace JackyAIApp.Server.Tests.Services.Dictionary
{
    /// <summary>Studio AI's quiz card: what the reader answers on, and what the model is (and is not) told.</summary>
    public class ReviewQuizToolTests
    {
        private static readonly AiToolContext Context = new("u1", "c1");

        private readonly Mock<IReviewQuizService> _quizzes = new();

        private static readonly ReviewQuiz TwoQuestions = new(
            [
                new ReviewQuizQuestion(11, "serendipity", "What is the meaning of \"serendipity\"?", ["luck in finding", "a fruit", "a river", "a tool"], 0, "serendipity: luck in finding (意外發現美好事物)"),
                new ReviewQuizQuestion(12, "ephemeral", "She said the joy was ___.", ["sturdy", "ephemeral", "loud", "green"], 1, "ephemeral: lasting a short time"),
            ],
            FromDue: true,
            TotalDueCount: 7);

        private Task<AiToolResult> RunAsync(object input) =>
            new ReviewQuizTool(_quizzes.Object).RunAsync(JsonSerializer.SerializeToElement(input), Context, default);

        private static JsonElement Card(AiToolResult result) => JsonSerializer.SerializeToElement(result.Card);

        [Fact]
        public async Task ShowsTheQuestions_AsAQuizCard_WithWhatTheAnswerIsRecordedAgainst()
        {
            _quizzes.Setup(q => q.CreateAsync(3, It.IsAny<CancellationToken>())).ReturnsAsync(ServiceResult.Ok(TwoQuestions));

            var result = await RunAsync(new { });

            Assert.False(result.IsError);
            var card = Card(result);
            Assert.Equal("quiz", card.GetProperty("kind").GetString());
            var data = card.GetProperty("data");
            Assert.True(data.GetProperty("fromDue").GetBoolean());
            Assert.Equal(7, data.GetProperty("totalDue").GetInt32());
            var first = data.GetProperty("questions")[0];
            Assert.Equal(11, first.GetProperty("userWordId").GetInt32());
            Assert.Equal("serendipity", first.GetProperty("word").GetString());
            Assert.Equal(4, first.GetProperty("options").GetArrayLength());
            Assert.Equal(0, first.GetProperty("correctIndex").GetInt32());
            Assert.Equal(2, data.GetProperty("questions").GetArrayLength());
        }

        [Fact]
        public async Task TellsTheModelTheWords_AndNotToGiveTheAnswersAway()
        {
            _quizzes.Setup(q => q.CreateAsync(2, It.IsAny<CancellationToken>())).ReturnsAsync(ServiceResult.Ok(TwoQuestions));

            var result = await RunAsync(new { count = 2 });

            Assert.Contains("serendipity、ephemeral", result.Content);
            Assert.Contains("共 7 個", result.Content);
            Assert.Contains("不要說出答案", result.Content);
            // For a client that cannot draw the card (another AI), the questions and answers, to ask one at a time.
            Assert.Contains("(B) ephemeral", result.Content);
            Assert.Contains("答案：1-A、2-B", result.Content);
        }

        [Fact]
        public async Task WordsNotYetDue_AreSaidToBePractice()
        {
            _quizzes.Setup(q => q.CreateAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(ServiceResult.Ok(TwoQuestions with { FromDue = false, TotalDueCount = 0 }));

            var result = await RunAsync(new { });

            Assert.Contains("這次是練習，不會記進複習排程", result.Content);
            Assert.False(Card(result).GetProperty("data").GetProperty("fromDue").GetBoolean());
        }

        [Fact]
        public async Task ACountThatIsNotANumber_IsTheDefault()
        {
            _quizzes.Setup(q => q.CreateAsync(ReviewQuizTool.DefaultCount, It.IsAny<CancellationToken>())).ReturnsAsync(ServiceResult.Ok(TwoQuestions));

            Assert.False((await RunAsync(new { count = "five" })).IsError);
        }

        [Fact]
        public async Task NoSavedWords_PointsToTheDictionary()
        {
            _quizzes.Setup(q => q.CreateAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(ServiceResult.Ok(new ReviewQuiz([], FromDue: false, TotalDueCount: 0)));

            var result = await RunAsync(new { });

            Assert.False(result.IsError);
            Assert.Equal("link", Card(result).GetProperty("kind").GetString());
            Assert.Equal("/english/dictionary", Card(result).GetProperty("href").GetString());
        }

        [Fact]
        public async Task WordsNamed_AreAskedAbout_AndThoseLeftOutAreSaid()
        {
            var mixed = TwoQuestions with
            {
                FromDue = false,
                Asked = ["serendipity", "ephemeral", "candid"],
                Questions = [TwoQuestions.Questions[0] with { Due = true }, TwoQuestions.Questions[1]],
            };
            _quizzes.Setup(q => q.CreateForAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(ServiceResult.Ok(mixed));

            var result = await RunAsync(new { words = new object[] { "serendipity", "ephemeral", 3, " ", "candid" }, count = 2 });

            _quizzes.Verify(q => q.CreateForAsync(
                It.Is<IReadOnlyList<string>>(w => w.SequenceEqual(new[] { "serendipity", "ephemeral", "candid" })),
                It.IsAny<CancellationToken>()), Times.Once);
            _quizzes.Verify(q => q.CreateAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
            Assert.Contains("使用者指定的字", result.Content);
            Assert.Contains("沒有出題的字（不在他的單字庫裡，或字典裡沒有可出題的釋義）：candid", result.Content);
            Assert.Contains("到期的字的答案會記進複習排程，還沒到期的字是練習", result.Content);
            Assert.Equal("quiz", Card(result).GetProperty("kind").GetString());
            // Each question says whether its answer may go to the review schedule.
            var questions = Card(result).GetProperty("data").GetProperty("questions");
            Assert.True(questions[0].GetProperty("due").GetBoolean());
            Assert.False(questions[1].GetProperty("due").GetBoolean());
        }

        [Fact]
        public async Task WordsNamed_NoneSaved_PointsToTheRepository()
        {
            _quizzes.Setup(q => q.CreateForAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(ServiceResult.Ok(new ReviewQuiz([], FromDue: false, TotalDueCount: 4, Asked: ["zeitgeist"])));

            var result = await RunAsync(new { words = new[] { "zeitgeist" } });

            Assert.False(result.IsError);
            Assert.Contains("zeitgeist", result.Content);
            Assert.Contains("add_word_to_repository", result.Content);
            Assert.Equal("/english/repository", Card(result).GetProperty("href").GetString());
        }

        [Fact]
        public async Task AnEmptyListOfWords_IsTheUsualQuiz()
        {
            _quizzes.Setup(q => q.CreateAsync(ReviewQuizTool.DefaultCount, It.IsAny<CancellationToken>())).ReturnsAsync(ServiceResult.Ok(TwoQuestions));

            Assert.False((await RunAsync(new { words = Array.Empty<string>() })).IsError);
            Assert.False((await RunAsync(new { words = new[] { "", "  " } })).IsError);
            _quizzes.Verify(q => q.CreateForAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task AFailure_IsReportedWithoutItsDetail()
        {
            _quizzes.Setup(q => q.CreateAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(ServiceResult.Fail(ErrorCodes.BadRequest, "db detail"));

            var result = await RunAsync(new { });

            Assert.True(result.IsError);
            Assert.DoesNotContain("db detail", result.Content);
        }
    }
}
