using System.Text.Json;
using JackyAIApp.Server.Services.Chatbot;

namespace JackyAIApp.Server.Tests.Services.Chatbot
{
    /// <summary>A tool's card is held to what the client draws before the reader is sent it.</summary>
    public class ToolCardGuardTests
    {
        private static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value);

        [Fact]
        public void ATableWithinTheLimits_IsSentAsItIs()
        {
            var card = Json(new { kind = "table", title = "單字", columns = new[] { new { key = "w" } }, rows = new[] { new { w = "a" } } });

            var sent = ToolCardGuard.Check(card);

            Assert.Equal(card.GetRawText(), sent!.Value.GetRawText());
        }

        [Fact]
        public void ABigTable_IsCut_AndCountsTheRowsItHad()
        {
            var columns = Enumerable.Range(0, 20).Select(i => new { key = $"c{i}" }).ToArray();
            var rows = Enumerable.Range(0, 250).Select(i => new { c0 = i }).ToArray();

            var sent = ToolCardGuard.Check(Json(new { kind = "table", columns, rows }))!.Value;

            Assert.Equal(ToolCardGuard.MaxColumns, sent.GetProperty("columns").GetArrayLength());
            Assert.Equal(ToolCardGuard.MaxRows, sent.GetProperty("rows").GetArrayLength());
            Assert.Equal(250, sent.GetProperty("totalRows").GetInt32());
        }

        [Fact]
        public void ATableThatSaysItHadMore_KeepsItsCount()
        {
            var rows = Enumerable.Range(0, 250).Select(i => new { c0 = i }).ToArray();

            var sent = ToolCardGuard.Check(Json(new { kind = "table", columns = new[] { new { key = "c0" } }, rows, totalRows = 9000 }))!.Value;

            Assert.Equal(9000, sent.GetProperty("totalRows").GetInt32());
        }

        [Theory]
        [InlineData("/english/dictionary/serendipity", true)]
        [InlineData("https://example.com/a", true)]
        [InlineData("http://example.com/a", false)]
        [InlineData("javascript:alert(1)", false)]
        [InlineData("//evil.example/a", false)]
        [InlineData("/\t/evil.example/a", false)]
        [InlineData("/\n/evil.example/a", false)]
        [InlineData("/\\evil.example/a", false)]
        [InlineData("data:text/html,x", false)]
        public void ALink_GoesOnlyToThisSiteOrHttps(string href, bool sent)
        {
            Assert.Equal(sent, ToolCardGuard.Check(Json(new { kind = "link", title = "t", href })) != null);
        }

        [Theory]
        [InlineData("chart", true)]
        [InlineData("Chart", false)]
        [InlineData("<script>", false)]
        [InlineData("", false)]
        public void AnAppsOwnKind_IsANamedObjectOfData(string kind, bool sent)
        {
            Assert.Equal(sent, ToolCardGuard.Check(Json(new { kind, data = new { viz = "bar" } })) != null);
        }

        [Fact]
        public void ACardThatIsNotAnObject_OrTooBig_IsNotSent()
        {
            Assert.Null(ToolCardGuard.Check(Json(new[] { 1 })));
            Assert.Null(ToolCardGuard.Check(Json(new { kind = "chart" })));   // no data
            Assert.Null(ToolCardGuard.Check(Json(new { kind = "chart", data = new { blob = new string('a', ToolCardGuard.MaxChars) } })));
        }
    }
}
