using JackyAIApp.Server.Common;
using JackyAIApp.Server.DTO;
using JackyAIApp.Server.Services.Dictionary;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Moq;

namespace JackyAIApp.Server.Tests.Common
{
    public class WordMetadataInjectionMiddlewareTests : IDisposable
    {
        private const string Template =
            "<!doctype html>\n<html>\n<head>\n<meta charset=\"UTF-8\" />\n" +
            "<meta name=\"description\" content=\"AI 英文學習與單字訓練\" />\n" +
            "<title>English Coach - AI 英文學習與單字訓練</title>\n</head>\n" +
            "<body><div id=\"root\"></div></body>\n</html>\n";

        private readonly string _wwwroot;
        private readonly Mock<IDictionaryService> _dictionaryServiceMock = new();
        private readonly Mock<IWebHostEnvironment> _envMock = new();
        private readonly IMemoryCache _cache = new MemoryCache(new MemoryCacheOptions());

        public WordMetadataInjectionMiddlewareTests()
        {
            _wwwroot = Path.Combine(Path.GetTempPath(), "wmim-tests-" + Guid.NewGuid());
            Directory.CreateDirectory(_wwwroot);
            File.WriteAllText(Path.Combine(_wwwroot, "index.html"), Template);
            _envMock.SetupGet(e => e.WebRootPath).Returns(_wwwroot);
        }

        public void Dispose()
        {
            if (Directory.Exists(_wwwroot))
            {
                Directory.Delete(_wwwroot, recursive: true);
            }
        }

        [Fact]
        public void Inject_ReplacesTitleAndDescription_AndInsertsCanonical()
        {
            var word = MakeWord("English Coach - apple", "apple (noun): a fruit");

            var html = WordMetadataInjectionMiddleware.Inject(Template, word, "https://example.com/english/dictionary/apple");

            Assert.Contains("<title>English Coach - apple</title>", html);
            Assert.Contains("<meta name=\"description\" content=\"apple (noun): a fruit\" />", html);
            Assert.Contains("<link rel=\"canonical\" href=\"https://example.com/english/dictionary/apple\" />", html);
            Assert.DoesNotContain("AI 英文學習與單字訓練", html);
            Assert.Contains("<div id=\"root\"></div>", html);
        }

        [Fact]
        public void Inject_HtmlEncodesHostileWordData()
        {
            var word = MakeWord("<script>alert(1)</script>", "5 < 10 & \"quoted\"");

            var html = WordMetadataInjectionMiddleware.Inject(Template, word, "https://example.com/english/dictionary/x");

            Assert.DoesNotContain("<script>alert(1)</script>", html);
            Assert.Contains("&lt;script&gt;", html);
            Assert.Contains("5 &lt; 10 &amp; &quot;quoted&quot;", html);
        }

        [Fact]
        public void Inject_InsertsDescriptionMeta_WhenTemplateHasNone()
        {
            const string templateWithoutDescription =
                "<!doctype html>\n<html>\n<head>\n<title>Old</title>\n</head>\n<body></body>\n</html>\n";
            var word = MakeWord("English Coach - apple", "apple (noun): a fruit");

            var html = WordMetadataInjectionMiddleware.Inject(templateWithoutDescription, word, "https://example.com/english/dictionary/apple");

            Assert.Contains("<meta name=\"description\" content=\"apple (noun): a fruit\" />", html);
            Assert.Contains("</head>", html);
        }

        [Fact]
        public async Task InvokeAsync_FallsThrough_WhenPathIsNotAWordDetailPath()
        {
            var middleware = MakeMiddleware(out var nextCalled);
            var context = MakeContext("GET", "/english/dictionary");

            await middleware.InvokeAsync(context, _dictionaryServiceMock.Object);

            Assert.True(nextCalled());
            _dictionaryServiceMock.Verify(s => s.GetWordAsync(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task InvokeAsync_FallsThrough_ForOtherRoutes()
        {
            var middleware = MakeMiddleware(out var nextCalled);
            var context = MakeContext("GET", "/api/dictionary/apple");

            await middleware.InvokeAsync(context, _dictionaryServiceMock.Object);

            Assert.True(nextCalled());
            _dictionaryServiceMock.Verify(s => s.GetWordAsync(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task InvokeAsync_FallsThrough_ForNonGetOrHeadMethods()
        {
            var middleware = MakeMiddleware(out var nextCalled);
            var context = MakeContext("POST", "/english/dictionary/apple");

            await middleware.InvokeAsync(context, _dictionaryServiceMock.Object);

            Assert.True(nextCalled());
            _dictionaryServiceMock.Verify(s => s.GetWordAsync(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task InvokeAsync_FallsThrough_WhenWordLookupFails()
        {
            _dictionaryServiceMock.Setup(s => s.GetWordAsync("apple"))
                .ReturnsAsync(ServiceResult.Fail(ErrorCodes.TheWordCannotBeFound, "not found"));
            var middleware = MakeMiddleware(out var nextCalled);
            var context = MakeContext("GET", "/english/dictionary/apple");

            await middleware.InvokeAsync(context, _dictionaryServiceMock.Object);

            Assert.True(nextCalled());
        }

        [Fact]
        public async Task InvokeAsync_FallsThrough_WhenDictionaryServiceThrows()
        {
            _dictionaryServiceMock.Setup(s => s.GetWordAsync("apple")).ThrowsAsync(new InvalidOperationException("boom"));
            var middleware = MakeMiddleware(out var nextCalled);
            var context = MakeContext("GET", "/english/dictionary/apple");

            await middleware.InvokeAsync(context, _dictionaryServiceMock.Object);

            Assert.True(nextCalled());
        }

        [Fact]
        public async Task InvokeAsync_FallsThrough_WhenTemplateFileIsMissing()
        {
            _envMock.SetupGet(e => e.WebRootPath).Returns(Path.Combine(Path.GetTempPath(), "does-not-exist-" + Guid.NewGuid()));
            _dictionaryServiceMock.Setup(s => s.GetWordAsync("apple"))
                .ReturnsAsync(ServiceResult.Ok(MakeWord("English Coach - apple", "apple (noun): a fruit")));
            var middleware = MakeMiddleware(out var nextCalled);
            var context = MakeContext("GET", "/english/dictionary/apple");

            await middleware.InvokeAsync(context, _dictionaryServiceMock.Object);

            Assert.True(nextCalled());
        }

        [Fact]
        public async Task InvokeAsync_FallsThrough_WhenLookupTimesOut()
        {
            var original = WordMetadataInjectionMiddleware.LookupTimeout;
            WordMetadataInjectionMiddleware.LookupTimeout = TimeSpan.FromMilliseconds(20);
            try
            {
                _dictionaryServiceMock.Setup(s => s.GetWordAsync("apple"))
                    .Returns(async () =>
                    {
                        await Task.Delay(500);
                        return ServiceResult.Ok(MakeWord("English Coach - apple", "apple (noun): a fruit"));
                    });
                var middleware = MakeMiddleware(out var nextCalled);
                var context = MakeContext("GET", "/english/dictionary/apple");

                await middleware.InvokeAsync(context, _dictionaryServiceMock.Object);

                Assert.True(nextCalled());
            }
            finally
            {
                WordMetadataInjectionMiddleware.LookupTimeout = original;
            }
        }

        [Fact]
        public async Task InvokeAsync_InjectsHtml_ForKnownWord()
        {
            _dictionaryServiceMock.Setup(s => s.GetWordAsync("apple"))
                .ReturnsAsync(ServiceResult.Ok(MakeWord("English Coach - apple", "apple (noun): a fruit")));
            var middleware = MakeMiddleware(out var nextCalled);
            var context = MakeContext("GET", "/english/dictionary/apple");
            var body = new MemoryStream();
            context.Response.Body = body;

            await middleware.InvokeAsync(context, _dictionaryServiceMock.Object);

            Assert.False(nextCalled());
            Assert.Equal("text/html; charset=utf-8", context.Response.ContentType);
            Assert.Equal("no-store", context.Response.Headers["Cache-Control"].ToString());
            body.Seek(0, SeekOrigin.Begin);
            var html = new StreamReader(body).ReadToEnd();
            Assert.Contains("<title>English Coach - apple</title>", html);
            Assert.Contains("rel=\"canonical\"", html);
        }

        [Fact]
        public async Task InvokeAsync_OmitsBody_ForHeadRequest()
        {
            _dictionaryServiceMock.Setup(s => s.GetWordAsync("apple"))
                .ReturnsAsync(ServiceResult.Ok(MakeWord("English Coach - apple", "apple (noun): a fruit")));
            var middleware = MakeMiddleware(out var nextCalled);
            var context = MakeContext("HEAD", "/english/dictionary/apple");
            var body = new MemoryStream();
            context.Response.Body = body;

            await middleware.InvokeAsync(context, _dictionaryServiceMock.Object);

            Assert.False(nextCalled());
            Assert.Equal(0, body.Length);
        }

        private WordMetadataInjectionMiddleware MakeMiddleware(out Func<bool> nextCalled)
        {
            var called = false;
            RequestDelegate next = _ => { called = true; return Task.CompletedTask; };
            nextCalled = () => called;
            return new WordMetadataInjectionMiddleware(next, _envMock.Object, _cache, Mock.Of<ILogger<WordMetadataInjectionMiddleware>>());
        }

        private static DefaultHttpContext MakeContext(string method, string path)
        {
            var context = new DefaultHttpContext();
            context.Request.Method = method;
            context.Request.Path = new PathString(path);
            context.Request.Scheme = "https";
            context.Request.Host = new HostString("example.com");
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
