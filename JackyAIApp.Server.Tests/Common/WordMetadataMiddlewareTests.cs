using JackyAIApp.Server.Common;
using JackyAIApp.Server.DTO;
using JackyAIApp.Server.Services.Dictionary;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace JackyAIApp.Server.Tests.Common
{
    public class WordMetadataMiddlewareTests : IDisposable
    {
        private const string Template =
            "<!doctype html>\n" +
            "<html lang=\"en\">\n" +
            "  <head>\n" +
            "    <meta charset=\"UTF-8\" />\n" +
            "    <meta name=\"description\" content=\"AI 英文學習與單字訓練\" />\n" +
            "    <title>English Coach - AI 英文學習與單字訓練</title>\n" +
            "  </head>\n" +
            "  <body>\n" +
            "    <div id=\"root\"></div>\n" +
            "    <script type=\"module\" src=\"/src/main.tsx\"></script>\n" +
            "  </body>\n" +
            "</html>\n";

        private readonly Mock<IDictionaryService> _dictionaryServiceMock = new();
        private readonly string _webRoot;

        public WordMetadataMiddlewareTests()
        {
            _webRoot = Directory.CreateTempSubdirectory().FullName;
            File.WriteAllText(Path.Combine(_webRoot, "index.html"), Template);
        }

        public void Dispose()
        {
            Directory.Delete(_webRoot, recursive: true);
            GC.SuppressFinalize(this);
        }

        [Fact]
        public void InjectMetadata_ReplacesTitleDescriptionAndInsertsCanonical()
        {
            var word = MakeWord("English Coach - apple", "apple (noun): a fruit");

            var html = WordMetadataMiddleware.InjectMetadata(Template, word, "https://example.com/english/dictionary/apple");

            Assert.Contains("<title>English Coach - apple</title>", html);
            Assert.Contains("<meta name=\"description\" content=\"apple (noun): a fruit\" />", html);
            Assert.Contains("<link rel=\"canonical\" href=\"https://example.com/english/dictionary/apple\" />", html);
            Assert.DoesNotContain("AI 英文學習與單字訓練", html);
            // Scripts/body untouched.
            Assert.Contains("<script type=\"module\" src=\"/src/main.tsx\"></script>", html);
        }

        [Fact]
        public void InjectMetadata_HtmlEncodesHostileWordData()
        {
            var word = MakeWord("<script>alert(1)</script>", "5 < 10 & \"quoted\"");

            var html = WordMetadataMiddleware.InjectMetadata(Template, word, "https://example.com/english/dictionary/apple\"><script>");

            Assert.DoesNotContain("<script>alert(1)</script>", html);
            Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", html);
            Assert.Contains("5 &lt; 10 &amp; &quot;quoted&quot;", html);
            Assert.DoesNotContain("\"><script>", html);
        }

        [Fact]
        public void InjectMetadata_TreatsDollarSignsInWordDataAsLiteral()
        {
            // Regression: Regex.Replace(string, string) parses "$1"/"$$" as backreferences in the
            // replacement text — must not leak into a literal "$" appearing in SEO text.
            var word = MakeWord("Cost: $1 per item", "Prices from $100 - $$ off today");

            var html = WordMetadataMiddleware.InjectMetadata(Template, word, "https://example.com/english/dictionary/apple");

            Assert.Contains("<title>Cost: $1 per item</title>", html);
            Assert.Contains("content=\"Prices from $100 - $$ off today\"", html);
        }

        [Fact]
        public async Task InvokeAsync_InjectsMetadata_ForKnownWord()
        {
            _dictionaryServiceMock.Setup(s => s.GetWordAsync("apple"))
                .ReturnsAsync(ServiceResult.Ok(MakeWord("English Coach - apple", "apple (noun): a fruit")));
            var nextCalled = false;
            var middleware = new WordMetadataMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
            var context = MakeContext("/english/dictionary/apple", "GET");
            var responseBody = new MemoryStream();
            context.Response.Body = responseBody;

            await middleware.InvokeAsync(context, _dictionaryServiceMock.Object, MakeEnv());

            Assert.False(nextCalled);
            Assert.Equal(200, context.Response.StatusCode);
            Assert.Equal("text/html; charset=utf-8", context.Response.ContentType);
            Assert.Equal("no-store", context.Response.Headers.CacheControl.ToString());
            responseBody.Seek(0, SeekOrigin.Begin);
            var body = new StreamReader(responseBody).ReadToEnd();
            Assert.Contains("<title>English Coach - apple</title>", body);
        }

        [Fact]
        public async Task InvokeAsync_FallsThrough_ForUnknownWord()
        {
            _dictionaryServiceMock.Setup(s => s.GetWordAsync("zzzzz"))
                .ReturnsAsync(ServiceResult.Fail(ErrorCodes.TheWordCannotBeFound, "not found"));
            var nextCalled = false;
            var middleware = new WordMetadataMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
            var context = MakeContext("/english/dictionary/zzzzz", "GET");

            await middleware.InvokeAsync(context, _dictionaryServiceMock.Object, MakeEnv());

            Assert.True(nextCalled);
        }

        [Fact]
        public async Task InvokeAsync_FallsThrough_WhenLookupThrows()
        {
            _dictionaryServiceMock.Setup(s => s.GetWordAsync("apple"))
                .ThrowsAsync(new TimeoutException("simulated timeout"));
            var nextCalled = false;
            var middleware = new WordMetadataMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
            var context = MakeContext("/english/dictionary/apple", "GET");

            await middleware.InvokeAsync(context, _dictionaryServiceMock.Object, MakeEnv());

            Assert.True(nextCalled);
        }

        [Theory]
        [InlineData("/english/dictionary")]
        [InlineData("/english/dictionary/")]
        [InlineData("/api/dictionary/apple")]
        [InlineData("/")]
        public async Task InvokeAsync_FallsThrough_ForNonWordDetailPaths(string path)
        {
            var nextCalled = false;
            var middleware = new WordMetadataMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
            var context = MakeContext(path, "GET");

            await middleware.InvokeAsync(context, _dictionaryServiceMock.Object, MakeEnv());

            Assert.True(nextCalled);
            _dictionaryServiceMock.Verify(s => s.GetWordAsync(It.IsAny<string>()), Times.Never);
        }

        [Theory]
        [InlineData("POST")]
        [InlineData("PUT")]
        [InlineData("DELETE")]
        public async Task InvokeAsync_FallsThrough_ForNonGetHeadMethods(string method)
        {
            var nextCalled = false;
            var middleware = new WordMetadataMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
            var context = MakeContext("/english/dictionary/apple", method);

            await middleware.InvokeAsync(context, _dictionaryServiceMock.Object, MakeEnv());

            Assert.True(nextCalled);
            _dictionaryServiceMock.Verify(s => s.GetWordAsync(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task InvokeAsync_FallsThrough_WhenTemplateFileMissing()
        {
            var emptyWebRoot = Directory.CreateTempSubdirectory().FullName;
            try
            {
                var nextCalled = false;
                var middleware = new WordMetadataMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
                var context = MakeContext("/english/dictionary/apple", "GET");
                var envMock = new Mock<IWebHostEnvironment>();
                envMock.Setup(e => e.WebRootPath).Returns(emptyWebRoot);

                await middleware.InvokeAsync(context, _dictionaryServiceMock.Object, envMock.Object);

                Assert.True(nextCalled);
                _dictionaryServiceMock.Verify(s => s.GetWordAsync(It.IsAny<string>()), Times.Never);
            }
            finally
            {
                Directory.Delete(emptyWebRoot, recursive: true);
            }
        }

        private IWebHostEnvironment MakeEnv()
        {
            var envMock = new Mock<IWebHostEnvironment>();
            envMock.Setup(e => e.WebRootPath).Returns(_webRoot);
            return envMock.Object;
        }

        private static DefaultHttpContext MakeContext(string path, string method)
        {
            var services = new ServiceCollection();
            var context = new DefaultHttpContext
            {
                RequestServices = services.BuildServiceProvider(),
            };
            context.Request.Path = new PathString(path);
            context.Request.Method = method;
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
