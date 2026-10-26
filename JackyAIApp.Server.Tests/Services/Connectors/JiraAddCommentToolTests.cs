using System.Net;
using System.Text;
using System.Text.Json;
using JackyAIApp.Server.Common;
using JackyAIApp.Server.Services;
using JackyAIApp.Server.Services.Connectors;
using JackyAIApp.Server.Services.Connectors.Atlassian;
using JackyAIApp.Server.Tests.TestHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace JackyAIApp.Server.Tests.Services.Connectors
{
    /// <summary>A comment on a Jira issue, against Atlassian's accessible resources and Jira's comment endpoint played by handlers.</summary>
    public class JiraAddCommentToolTests
    {
        private const string User = "u1";
        private const string CloudId = "11111111-2222-3333-4444-555555555555";

        private readonly Mock<IConnectorService> _connections = new();
        private readonly List<(string Client, HttpMethod Method, string Url, string? Body)> _asked = [];
        private Func<HttpResponseMessage> _resources = () => Json(Sites);
        private Func<HttpResponseMessage> _post = () => Json("""{ "id": "10042", "self": "x" }""", HttpStatusCode.Created);

        private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
            new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

        private const string Sites = $$"""
            [
              { "id": "aaaaaaaa-0000-0000-0000-000000000000", "url": "https://readonly.atlassian.net", "name": "readonly",
                "scopes": ["read:jira-work"] },
              { "id": "{{CloudId}}", "url": "https://acme.atlassian.net", "name": "acme",
                "scopes": ["read:jira-user", "read:jira-work", "write:jira-work"] }
            ]
            """;

        private JiraAddCommentTool Tool()
        {
            var factory = new Mock<IHttpClientFactory>();
            factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns((string name) => new HttpClient(new StubHttpMessageHandler(request =>
            {
                _asked.Add((name, request.Method, request.RequestUri!.AbsoluteUri, request.Content is { } content ? new StreamReader(content.ReadAsStream()).ReadToEnd() : null));
                return request.RequestUri!.AbsoluteUri == JiraIssuesTool.ResourcesUrl ? _resources() : _post();
            })));
            return new JiraAddCommentTool(_connections.Object, factory.Object, NullLogger<JiraAddCommentTool>.Instance);
        }

        private void Connected() => _connections.Setup(c => c.GetAccessTokenAsync(User, ConnectorProviders.Atlassian)).ReturnsAsync("a-token");

        private static JsonElement Input(object input) => JsonSerializer.SerializeToElement(input);

        private Task<AiToolResult> Run(object input) =>
            Tool().RunAsync(Input(input), new AiToolContext(User, "c1"), default);

        private static JsonElement CardJson(AiToolResult result) => JsonSerializer.SerializeToElement(result.Card);

        [Fact]
        public void ItIsAWrite_OfTheSite_SoTheReaderIsAskedFirst()
        {
            Assert.Equal(ToolEffect.Write, JiraAddCommentTool.Definition.Effect);
            Assert.Equal(AiToolDescriptor.SiteConnector, JiraAddCommentTool.Definition.Connector);
            Assert.False(JiraAddCommentTool.Definition.ReadsOutsideText);
        }

        [Fact]
        public void TheCard_SaysTheIssueAndTheWholeComment()
        {
            var said = Tool().Describe(Input(new { issueKey = " web-12 ", comment = "今天會處理\n明天給 PR" }), new AiToolContext(User, "c1"));

            Assert.Equal("用你的名義在 Jira 的 WEB-12 留言：「今天會處理 明天給 PR」", said);
        }

        [Theory]
        [InlineData("not a key", "hi")]
        [InlineData("WEB-12/../x", "hi")]
        [InlineData("WEB-12", "   ")]
        public async Task AMissingOrBadIssueOrComment_IsRefused_AndJiraIsNotAsked(string key, string comment)
        {
            Connected();

            var result = await Run(new { issueKey = key, comment });

            Assert.True(result.IsError);
            Assert.Null(Tool().Describe(Input(new { issueKey = key, comment }), new AiToolContext(User, "c1")));
            Assert.Empty(_asked);
        }

        [Fact]
        public async Task ACommentTooLong_IsRefused_NeverCut()
        {
            Connected();

            var result = await Run(new { issueKey = "WEB-12", comment = new string('a', JiraAddCommentTool.MaxComment + 1) });

            Assert.True(result.IsError);
            Assert.Contains("不要自己截斷", result.Content);
            Assert.Empty(_asked);
        }

        [Fact]
        public async Task WithoutAnAtlassianConnection_NothingIsPosted_AndTheReaderIsPointedToTheConnectorsPage()
        {
            var result = await Run(new { issueKey = "WEB-12", comment = "hi" });

            Assert.True(result.IsError);
            Assert.Equal("/connectors", CardJson(result).GetProperty("href").GetString());
            Assert.Empty(_asked);
        }

        [Fact]
        public async Task TheComment_IsPosted_OnTheFirstSiteGrantedWrites_AsParagraphs_WithoutRetries()
        {
            Connected();

            var result = await Run(new { issueKey = "WEB-12", comment = "今天會處理\n\n明天給 PR  " });

            Assert.False(result.IsError);
            Assert.Equal(2, _asked.Count);
            var post = _asked[1];
            Assert.Equal(HttpMethod.Post, post.Method);
            Assert.Equal($"{JiraIssuesTool.ApiBase}{CloudId}/rest/api/3/issue/WEB-12/comment", post.Url);
            // The default client: the read-only one retries, which could post a comment twice.
            Assert.Equal(JiraAddCommentTool.WriteHttpClientName, post.Client);
            Assert.NotEqual(JiraIssuesTool.HttpClientName, post.Client);

            using var body = JsonDocument.Parse(post.Body!);
            var doc = body.RootElement.GetProperty("body");
            Assert.Equal("doc", doc.GetProperty("type").GetString());
            var texts = doc.GetProperty("content").EnumerateArray()
                .Select(p => p.GetProperty("content")[0].GetProperty("text").GetString())
                .ToList();
            Assert.Equal(new[] { "今天會處理", "明天給 PR" }, texts);

            var card = CardJson(result);
            Assert.Equal("link", card.GetProperty("kind").GetString());
            Assert.Equal("https://acme.atlassian.net/browse/WEB-12?focusedCommentId=10042", card.GetProperty("href").GetString());
            Assert.Contains("https://acme.atlassian.net/browse/WEB-12?focusedCommentId=10042", result.Content);
        }

        [Fact]
        public async Task ASiteNotGrantedWrites_IsNotPostedTo()
        {
            Connected();
            _resources = () => Json("""[ { "id": "abc", "url": "https://acme.atlassian.net", "name": "acme", "scopes": ["read:jira-work"] } ]""");

            var result = await Run(new { issueKey = "WEB-12", comment = "hi" });

            Assert.True(result.IsError);
            Assert.Single(_asked);
            Assert.Equal("/connectors", CardJson(result).GetProperty("href").GetString());
        }

        [Fact]
        public async Task AnIssueJiraDoesNotHave_IsSaid_SoTheModelChecksTheKey()
        {
            Connected();
            _post = () => Json("""{ "errorMessages": ["Issue does not exist"] }""", HttpStatusCode.NotFound);

            var result = await Run(new { issueKey = "WEB-999", comment = "hi" });

            Assert.True(result.IsError);
            Assert.Contains("WEB-999", result.Content);
            Assert.Contains("jira_issues", result.Content);
        }

        [Theory]
        [InlineData(HttpStatusCode.Unauthorized)]
        [InlineData(HttpStatusCode.Forbidden)]
        public async Task ARefusedConnection_PointsToReconnecting(HttpStatusCode status)
        {
            Connected();
            _post = () => Json("{}", status);

            var result = await Run(new { issueKey = "WEB-12", comment = "hi" });

            Assert.True(result.IsError);
            Assert.Equal("/connectors", CardJson(result).GetProperty("href").GetString());
        }

        [Fact]
        public async Task AServerError_IsNotSaidToBeNotPosted_SinceJiraMayHaveWrittenIt()
        {
            Connected();
            _post = () => Json("{}", HttpStatusCode.BadGateway);

            var result = await Run(new { issueKey = "WEB-12", comment = "hi" });

            Assert.True(result.IsError);
            Assert.Contains("可能沒有留上", result.Content);
            Assert.Contains("https://acme.atlassian.net/browse/WEB-12", result.Content);
        }

        [Fact]
        public void ACommentId_GoesIntoTheLink_OnlyAsDigits()
        {
            Assert.Equal("10042", JiraAddCommentTool.CommentIdOf(JsonDocument.Parse("""{ "id": "10042" }""").RootElement));
            Assert.Null(JiraAddCommentTool.CommentIdOf(JsonDocument.Parse("""{ "id": "1&x=2" }""").RootElement));
            Assert.Null(JiraAddCommentTool.CommentIdOf(JsonDocument.Parse("""{ "id": 10042 }""").RootElement));
        }

        [Fact]
        public void ThePreview_IsOneLine_AndCut()
        {
            Assert.Equal("a b", JiraAddCommentTool.Preview("a\nb"));
            Assert.Equal(new string('a', 120) + "…", JiraAddCommentTool.Preview(new string('a', 300)));
        }
    }
}
