using JackyAIApp.Server.Common;
using JackyAIApp.Server.DTO;
using JackyAIApp.Server.Services.Dictionary;
using Microsoft.AspNetCore.Http;
using Moq;

namespace JackyAIApp.Server.Tests.Common
{
    public class BotPreviewMiddlewareTests
    {
        private readonly Mock<IDictionaryService> _dictionaryServiceMock = new();

        [Theory]
        [InlineData("/english/dictionary/apple", "apple")]
        [InlineData("/english/dictionary/apple/", "apple")]
        [InlineData("/english/dictionary/ice%20cream", "ice cream")]
        public void TryExtractWord_ReturnsDecodedWord_ForSingleSegmentPath(string path, string expectedWord)
        {
            Assert.Equal(expectedWord, BotPreviewMiddleware.TryExtractWord(new PathString(path)));
        }

        [Theory]
        [InlineData("/english/dictionary")]
        [InlineData("/english/dictionary/")]
        [InlineData("/english/dictionary/apple/extra")]
        [InlineData("/api/dictionary/apple")]
        [InlineData("/")]
        public void TryExtractWord_ReturnsNull_ForNonWordDetailPaths(string path)
        {
            Assert.Null(BotPreviewMiddleware.TryExtractWord(new PathString(path)));
        }

        [Fact]
        public void BuildHtml_RendersSeoFieldsVerbatimIntoTitleAndOgTags()
        {
            var word = MakeWord(seoTitle: "apple - EngAI Learn Dictionary", seoDescription: "apple (noun): a fruit");

            var html = BotPreviewMiddleware.BuildHtml(word, "https://example.com/english/dictionary/apple", "https://example.com");

            Assert.Contains("<title>apple - EngAI Learn Dictionary</title>", html);
            Assert.Contains("content=\"apple (noun): a fruit\"", html);
            Assert.Contains("property=\"og:title\" content=\"apple - EngAI Learn Dictionary\"", html);
            Assert.Contains("property=\"og:url\" content=\"https://example.com/english/dictionary/apple\"", html);
            Assert.Contains($"property=\"og:image\" content=\"https://example.com{BotPreviewMiddleware.GenericOgImagePath}\"", html);
        }

        [Fact]
        public void BuildHtml_HtmlEncodesSeoText()
        {
            var word = MakeWord(seoTitle: "A & B", seoDescription: "5 < 10");

            var html = BotPreviewMiddleware.BuildHtml(word, "https://example.com/english/dictionary/a", "https://example.com");

            Assert.Contains("<title>A &amp; B</title>", html);
            Assert.Contains("content=\"5 &lt; 10\"", html);
        }

        [Fact]
        public async Task InvokeAsync_FallsThrough_WhenUserAgentIsNotABot()
        {
            var nextCalled = false;
            var middleware = new BotPreviewMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
            var context = MakeContext("/english/dictionary/apple", "Mozilla/5.0 (real browser)");

            await middleware.InvokeAsync(context, _dictionaryServiceMock.Object);

            Assert.True(nextCalled);
            _dictionaryServiceMock.Verify(s => s.GetWordAsync(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task InvokeAsync_FallsThrough_ForGooglebot()
        {
            var nextCalled = false;
            var middleware = new BotPreviewMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
            var context = MakeContext("/english/dictionary/apple", "Googlebot/2.1 (+http://www.google.com/bot.html)");

            await middleware.InvokeAsync(context, _dictionaryServiceMock.Object);

            Assert.True(nextCalled);
        }

        [Fact]
        public async Task InvokeAsync_FallsThrough_WhenPathIsNotAWordDetailPath()
        {
            var nextCalled = false;
            var middleware = new BotPreviewMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
            var context = MakeContext("/english/dictionary", "facebookexternalhit/1.1");

            await middleware.InvokeAsync(context, _dictionaryServiceMock.Object);

            Assert.True(nextCalled);
        }

        [Fact]
        public async Task InvokeAsync_FallsThrough_WhenWordLookupFails()
        {
            _dictionaryServiceMock.Setup(s => s.GetWordAsync("apple"))
                .ReturnsAsync(ServiceResult.Fail(ErrorCodes.TheWordCannotBeFound, "not found"));
            var nextCalled = false;
            var middleware = new BotPreviewMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
            var context = MakeContext("/english/dictionary/apple", "facebookexternalhit/1.1");

            await middleware.InvokeAsync(context, _dictionaryServiceMock.Object);

            Assert.True(nextCalled);
            Assert.Equal(200, context.Response.StatusCode);
        }

        [Theory]
        [InlineData("facebookexternalhit/1.1")]
        [InlineData("Facebot")]
        [InlineData("Line/11.0")]
        [InlineData("Twitterbot/1.0")]
        [InlineData("Slackbot 1.0 (+https://api.slack.com/robots)")]
        [InlineData("Mozilla/5.0 (compatible; Discordbot/2.0; +https://discordapp.com)")]
        [InlineData("WhatsApp/2.23")]
        public async Task InvokeAsync_ServesPreviewHtml_ForKnownBotUserAgents(string userAgent)
        {
            _dictionaryServiceMock.Setup(s => s.GetWordAsync("apple"))
                .ReturnsAsync(ServiceResult.Ok(MakeWord("apple - EngAI Learn Dictionary", "apple (noun): a fruit")));
            var nextCalled = false;
            var middleware = new BotPreviewMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
            var context = MakeContext("/english/dictionary/apple", userAgent);
            var responseBody = new MemoryStream();
            context.Response.Body = responseBody;

            await middleware.InvokeAsync(context, _dictionaryServiceMock.Object);

            Assert.False(nextCalled);
            Assert.Equal("text/html; charset=utf-8", context.Response.ContentType);
            responseBody.Seek(0, SeekOrigin.Begin);
            var body = new StreamReader(responseBody).ReadToEnd();
            Assert.Contains("<title>apple - EngAI Learn Dictionary</title>", body);
            Assert.Contains("og:url", body);
        }

        private static DefaultHttpContext MakeContext(string path, string userAgent)
        {
            var context = new DefaultHttpContext();
            context.Request.Path = new PathString(path);
            context.Request.Scheme = "https";
            context.Request.Host = new HostString("example.com");
            context.Request.Headers.UserAgent = userAgent;
            return context;
        }

        private static Word MakeWord(string seoTitle, string seoDescription) => new()
        {
            Id = Guid.NewGuid().ToString(),
            PartitionKey = "word",
            Word = "apple",
            KKPhonics = "/ˈæpəl/",
            Meanings = [],
            DateAdded = DateTime.UtcNow,
            SeoTitle = seoTitle,
            SeoDescription = seoDescription,
        };
    }
}
