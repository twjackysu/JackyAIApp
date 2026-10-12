using JackyAIApp.Server.Common;
using JackyAIApp.Server.DTO;
using JackyAIApp.Server.Services.Dictionary;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Moq;

namespace JackyAIApp.Server.Tests.Common
{
    public class WordPageMetadataMiddlewareTests
    {
        private const string Template =
            "<!doctype html>\n" +
            "<html lang=\"en\">\n" +
            "<head>\n" +
            "<meta charset=\"UTF-8\" />\n" +
            "<meta name=\"description\" content=\"AI 英文學習與單字訓練\" />\n" +
            "<title>English Coach - AI 英文學習與單字訓練</title>\n" +
            "</head>\n" +
            "<body><div id=\"root\"></div></body>\n" +
            "</html>\n";

        private readonly Mock<IDictionaryService> _dictionaryServiceMock = new();

        [Fact]
        public void InjectMetadata_ReplacesTitleDescriptionAndInsertsCanonical()
        {
            var word = MakeWord("apple 英文意思、發音、例句 | English Coach", "apple (noun): a fruit");

            var html = WordPageMetadataMiddleware.InjectMetadata(Template, word, "https://example.com/english/dictionary/apple");

            Assert.Contains("<title>apple 英文意思、發音、例句 | English Coach</title>", html);
            Assert.Contains("<meta name=\"description\" content=\"apple (noun): a fruit\" />", html);
            Assert.Contains("<link rel=\"canonical\" href=\"https://example.com/english/dictionary/apple\" />", html);
            Assert.DoesNotContain("AI 英文學習與單字訓練", html);
        }

        [Fact]
        public void InjectMetadata_GivesLinkPreviewsTheWordsOwnText()
        {
            var word = MakeWord("apple 英文意思、發音、例句 | English Coach", "apple (noun): a fruit");

            var html = WordPageMetadataMiddleware.InjectMetadata(Template, word, "https://example.com/english/dictionary/apple");

            Assert.Contains("<meta property=\"og:title\" content=\"apple 英文意思、發音、例句 | English Coach\" />", html);
            Assert.Contains("<meta property=\"og:description\" content=\"apple (noun): a fruit\" />", html);
            Assert.Contains("<meta property=\"og:url\" content=\"https://example.com/english/dictionary/apple\" />", html);
            Assert.Contains("<meta property=\"og:image\" content=\"https://example.com/JackyAIIcon.png\" />", html);
        }

        [Fact]
        public void InjectMetadata_ReplacesExistingCanonicalInsteadOfDuplicating()
        {
            var templateWithCanonical = Template.Replace("</head>", "<link rel=\"canonical\" href=\"https://example.com/old\" />\n</head>");
            var word = MakeWord("apple 英文意思、發音、例句 | English Coach", "apple (noun): a fruit");

            var html = WordPageMetadataMiddleware.InjectMetadata(templateWithCanonical, word, "https://example.com/english/dictionary/apple");

            Assert.Single(System.Text.RegularExpressions.Regex.Matches(html, "rel=\"canonical\""));
            Assert.Contains("href=\"https://example.com/english/dictionary/apple\"", html);
            Assert.DoesNotContain("https://example.com/old", html);
        }

        [Fact]
        public void InjectMetadata_HtmlEncodesHostileWordData()
        {
            var word = MakeWord("</title><script>alert(1)</script>", "5 < 10 & \"quoted\"");

            var html = WordPageMetadataMiddleware.InjectMetadata(Template, word, "https://example.com/english/dictionary/a");

            Assert.DoesNotContain("<script>", html);
            Assert.Contains("&lt;/title&gt;&lt;script&gt;alert(1)&lt;/script&gt;", html);
            Assert.Contains("content=\"5 &lt; 10 &amp; &quot;quoted&quot;\"", html);
        }

        [Fact]
        public void InjectMetadata_DoesNotTreatDollarSignsInWordDataAsRegexBackreferences()
        {
            var word = MakeWord("$1 title $&", "costs $2.50, see $` and $'");

            var html = WordPageMetadataMiddleware.InjectMetadata(Template, word, "https://example.com/english/dictionary/a");

            Assert.Contains("<title>$1 title $&amp;</title>", html);
            Assert.Contains("content=\"costs $2.50, see $` and $&#39;\"", html);
        }

        [Fact]
        public void InjectMetadata_InsertsDefinedTermJsonLd()
        {
            var word = MakeWord("apple 英文意思、發音、例句 | English Coach", "apple (noun): a fruit");

            var html = WordPageMetadataMiddleware.InjectMetadata(Template, word, "https://example.com/english/dictionary/apple");

            var m = System.Text.RegularExpressions.Regex.Match(html, "<script type=\"application/ld\\+json\">(.*?)</script>");
            Assert.True(m.Success);
            using var doc = System.Text.Json.JsonDocument.Parse(m.Groups[1].Value);
            var root = doc.RootElement;
            Assert.Equal("DefinedTerm", root.GetProperty("@type").GetString());
            Assert.Equal("apple", root.GetProperty("name").GetString());
            Assert.Equal("apple (noun): a fruit", root.GetProperty("description").GetString());
            Assert.Equal("en", root.GetProperty("inLanguage").GetString());
            Assert.Equal("https://example.com/english/dictionary/apple", root.GetProperty("url").GetString());
        }

        [Fact]
        public void InjectMetadata_JsonLdIsSafeForHostileWordData()
        {
            var word = MakeWord("t", "</script><script>alert(1)</script> \"q\" $1 $&");
            word.Word = "a</script>\"b$1";

            var html = WordPageMetadataMiddleware.InjectMetadata(Template, word, "https://example.com/english/dictionary/a");

            Assert.DoesNotContain("<script>alert", html);
            Assert.Single(System.Text.RegularExpressions.Regex.Matches(html, "</script>"));
            var m = System.Text.RegularExpressions.Regex.Match(html, "<script type=\"application/ld\\+json\">(.*?)</script>");
            using var doc = System.Text.Json.JsonDocument.Parse(m.Groups[1].Value);
            Assert.Equal("a</script>\"b$1", doc.RootElement.GetProperty("name").GetString());
            Assert.Equal("</script><script>alert(1)</script> \"q\" $1 $&", doc.RootElement.GetProperty("description").GetString());
        }

        [Fact]
        public async Task InvokeAsync_FallsThrough_WhenMethodIsNotGetOrHead()
        {
            var nextCalled = false;
            var middleware = new WordPageMetadataMiddleware(_ => { nextCalled = true; return Task.CompletedTask; }, MakeEnv(Template));
            var context = MakeContext("/english/dictionary/apple", "POST");

            await middleware.InvokeAsync(context, _dictionaryServiceMock.Object);

            Assert.True(nextCalled);
            _dictionaryServiceMock.Verify(s => s.GetWordAsync(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task InvokeAsync_FallsThrough_WhenPathIsNotAWordDetailPath()
        {
            var nextCalled = false;
            var middleware = new WordPageMetadataMiddleware(_ => { nextCalled = true; return Task.CompletedTask; }, MakeEnv(Template));
            var context = MakeContext("/english/dictionary", "GET");

            await middleware.InvokeAsync(context, _dictionaryServiceMock.Object);

            Assert.True(nextCalled);
            _dictionaryServiceMock.Verify(s => s.GetWordAsync(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task InvokeAsync_FallsThrough_WhenTemplateFileIsMissing()
        {
            var nextCalled = false;
            var middleware = new WordPageMetadataMiddleware(_ => { nextCalled = true; return Task.CompletedTask; }, MakeEnv(template: null));
            var context = MakeContext("/english/dictionary/apple", "GET");

            await middleware.InvokeAsync(context, _dictionaryServiceMock.Object);

            Assert.True(nextCalled);
            _dictionaryServiceMock.Verify(s => s.GetWordAsync(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task InvokeAsync_FallsThrough_WhenWordIsUnknown()
        {
            _dictionaryServiceMock.Setup(s => s.GetWordAsync("nosuchword"))
                .ReturnsAsync(ServiceResult.Fail(ErrorCodes.TheWordCannotBeFound, "not found"));
            var nextCalled = false;
            var middleware = new WordPageMetadataMiddleware(_ => { nextCalled = true; return Task.CompletedTask; }, MakeEnv(Template));
            var context = MakeContext("/english/dictionary/nosuchword", "GET");
            var responseBody = new MemoryStream();
            context.Response.Body = responseBody;

            await middleware.InvokeAsync(context, _dictionaryServiceMock.Object);

            Assert.True(nextCalled);
            Assert.Equal(0, responseBody.Length); // nothing written — response left untouched for `next`
        }

        [Fact]
        public async Task InvokeAsync_FallsThrough_WhenLookupThrows()
        {
            _dictionaryServiceMock.Setup(s => s.GetWordAsync("apple")).ThrowsAsync(new InvalidOperationException("boom"));
            var nextCalled = false;
            var middleware = new WordPageMetadataMiddleware(_ => { nextCalled = true; return Task.CompletedTask; }, MakeEnv(Template));
            var context = MakeContext("/english/dictionary/apple", "GET");

            await middleware.InvokeAsync(context, _dictionaryServiceMock.Object);

            Assert.True(nextCalled);
        }

        [Fact]
        public async Task InvokeAsync_ServesInjectedHtml_ForGet()
        {
            _dictionaryServiceMock.Setup(s => s.GetWordAsync("apple"))
                .ReturnsAsync(ServiceResult.Ok(MakeWord("apple 英文意思、發音、例句 | English Coach", "apple (noun): a fruit")));
            var nextCalled = false;
            var middleware = new WordPageMetadataMiddleware(_ => { nextCalled = true; return Task.CompletedTask; }, MakeEnv(Template));
            var context = MakeContext("/english/dictionary/apple", "GET");
            var responseBody = new MemoryStream();
            context.Response.Body = responseBody;

            await middleware.InvokeAsync(context, _dictionaryServiceMock.Object);

            Assert.False(nextCalled);
            Assert.Equal(200, context.Response.StatusCode);
            Assert.Equal("text/html; charset=utf-8", context.Response.ContentType);
            Assert.Equal("no-store", context.Response.Headers["Cache-Control"].ToString());
            responseBody.Seek(0, SeekOrigin.Begin);
            var body = new StreamReader(responseBody).ReadToEnd();
            Assert.Contains("<title>apple 英文意思、發音、例句 | English Coach</title>", body);
            Assert.Contains("rel=\"canonical\"", body);
        }

        [Fact]
        public async Task InvokeAsync_SetsHeadersButWritesNoBody_ForHead()
        {
            _dictionaryServiceMock.Setup(s => s.GetWordAsync("apple"))
                .ReturnsAsync(ServiceResult.Ok(MakeWord("apple 英文意思、發音、例句 | English Coach", "apple (noun): a fruit")));
            var nextCalled = false;
            var middleware = new WordPageMetadataMiddleware(_ => { nextCalled = true; return Task.CompletedTask; }, MakeEnv(Template));
            var context = MakeContext("/english/dictionary/apple", "HEAD");
            var responseBody = new MemoryStream();
            context.Response.Body = responseBody;

            await middleware.InvokeAsync(context, _dictionaryServiceMock.Object);

            Assert.False(nextCalled);
            Assert.Equal(200, context.Response.StatusCode);
            Assert.Equal("text/html; charset=utf-8", context.Response.ContentType);
            Assert.Equal("no-store", context.Response.Headers["Cache-Control"].ToString());
            Assert.True(context.Response.ContentLength > 0);
            Assert.Equal(0, responseBody.Length); // headers only, per HTTP HEAD semantics
        }

        private static DefaultHttpContext MakeContext(string path, string method)
        {
            var context = new DefaultHttpContext();
            context.Request.Path = new PathString(path);
            context.Request.Method = method;
            context.Request.Scheme = "https";
            context.Request.Host = new HostString("example.com");
            return context;
        }

        private static IWebHostEnvironment MakeEnv(string? template)
        {
            var fileInfoMock = new Mock<IFileInfo>();
            fileInfoMock.Setup(f => f.Exists).Returns(template != null);
            if (template != null)
            {
                fileInfoMock.Setup(f => f.CreateReadStream())
                    .Returns(() => new MemoryStream(System.Text.Encoding.UTF8.GetBytes(template)));
            }

            var fileProviderMock = new Mock<IFileProvider>();
            fileProviderMock.Setup(p => p.GetFileInfo("index.html")).Returns(fileInfoMock.Object);

            var envMock = new Mock<IWebHostEnvironment>();
            envMock.Setup(e => e.WebRootFileProvider).Returns(fileProviderMock.Object);
            return envMock.Object;
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
