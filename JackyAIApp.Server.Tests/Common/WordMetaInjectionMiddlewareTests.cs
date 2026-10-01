using JackyAIApp.Server.Common;
using JackyAIApp.Server.DTO;
using JackyAIApp.Server.Services.Dictionary;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Moq;
using System.Text;

namespace JackyAIApp.Server.Tests.Common
{
    public class WordMetaInjectionMiddlewareTests
    {
        private const string Template =
            "<!doctype html>\n" +
            "<html>\n" +
            "<head>\n" +
            "<meta charset=\"utf-8\" />\n" +
            "<meta name=\"description\" content=\"default description\" />\n" +
            "<title>default title</title>\n" +
            "</head>\n" +
            "<body><div id=\"root\"></div></body>\n" +
            "</html>\n";

        private readonly Mock<IWebHostEnvironment> _envMock = new();
        private readonly Mock<IDictionaryService> _dictionaryServiceMock = new();

        public WordMetaInjectionMiddlewareTests()
        {
            _envMock.Setup(e => e.WebRootFileProvider).Returns(MakeFileProvider(Template));
        }

        [Fact]
        public void InjectMetadata_ReplacesTitleDescriptionAndInsertsCanonical()
        {
            var word = MakeWord(seoTitle: "English Coach - apple", seoDescription: "apple (noun): a fruit");

            var html = WordMetaInjectionMiddleware.InjectMetadata(Template, word, "https://example.com/english/dictionary/apple");

            Assert.Contains("<title>English Coach - apple</title>", html);
            Assert.DoesNotContain("default title", html);
            Assert.Contains("content=\"apple (noun): a fruit\"", html);
            Assert.DoesNotContain("default description", html);
            Assert.Contains("<link rel=\"canonical\" href=\"https://example.com/english/dictionary/apple\" />", html);
        }

        [Fact]
        public void InjectMetadata_HtmlEncodesHostileWordData()
        {
            var word = MakeWord(seoTitle: "A & B <script>", seoDescription: "5 < 10 \"quoted\"");

            var html = WordMetaInjectionMiddleware.InjectMetadata(Template, word, "https://example.com/english/dictionary/a");

            Assert.Contains("<title>A &amp; B &lt;script&gt;</title>", html);
            Assert.Contains("content=\"5 &lt; 10 &quot;quoted&quot;\"", html);
            Assert.DoesNotContain("<script>", html);
        }

        [Fact]
        public void InjectMetadata_PreservesLiteralDollarSigns()
        {
            // Regex.Replace(string, string) treats "$" in the replacement as a backreference —
            // must not corrupt a title/description that legitimately contains one.
            var word = MakeWord(seoTitle: "$100 word", seoDescription: "costs $1 $$ twice");

            var html = WordMetaInjectionMiddleware.InjectMetadata(Template, word, "https://example.com/english/dictionary/a");

            Assert.Contains("<title>$100 word</title>", html);
            Assert.Contains("content=\"costs $1 $$ twice\"", html);
        }

        [Fact]
        public void InjectMetadata_KeepsDefaultDescription_WhenSeoDescriptionIsEmpty()
        {
            var word = MakeWord(seoTitle: "English Coach - apple", seoDescription: "");

            var html = WordMetaInjectionMiddleware.InjectMetadata(Template, word, "https://example.com/english/dictionary/apple");

            Assert.Contains("content=\"default description\"", html);
        }

        [Fact]
        public async Task InvokeAsync_FallsThrough_WhenPathIsNotAWordDetailPath()
        {
            var nextCalled = false;
            var middleware = new WordMetaInjectionMiddleware(_ => { nextCalled = true; return Task.CompletedTask; }, _envMock.Object, NullLogger());
            var context = MakeContext("/english/dictionary");

            await middleware.InvokeAsync(context, _dictionaryServiceMock.Object);

            Assert.True(nextCalled);
            _dictionaryServiceMock.Verify(s => s.GetWordAsync(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task InvokeAsync_FallsThrough_WhenWordLookupFails()
        {
            _dictionaryServiceMock.Setup(s => s.GetWordAsync("unknownword"))
                .ReturnsAsync(ServiceResult.Fail(ErrorCodes.TheWordCannotBeFound, "not found"));
            var nextCalled = false;
            var middleware = new WordMetaInjectionMiddleware(_ => { nextCalled = true; return Task.CompletedTask; }, _envMock.Object, NullLogger());
            var context = MakeContext("/english/dictionary/unknownword");

            await middleware.InvokeAsync(context, _dictionaryServiceMock.Object);

            Assert.True(nextCalled);
            Assert.Equal(200, context.Response.StatusCode); // DefaultHttpContext's default, untouched by us
        }

        [Fact]
        public async Task InvokeAsync_FallsThrough_WhenDictionaryServiceThrows()
        {
            _dictionaryServiceMock.Setup(s => s.GetWordAsync("apple")).ThrowsAsync(new InvalidOperationException("boom"));
            var nextCalled = false;
            var middleware = new WordMetaInjectionMiddleware(_ => { nextCalled = true; return Task.CompletedTask; }, _envMock.Object, NullLogger());
            var context = MakeContext("/english/dictionary/apple");

            await middleware.InvokeAsync(context, _dictionaryServiceMock.Object);

            Assert.True(nextCalled);
        }

        [Fact]
        public async Task InvokeAsync_FallsThrough_WhenLookupExceedsTimeout()
        {
            _dictionaryServiceMock.Setup(s => s.GetWordAsync("apple"))
                .Returns(async () => { await Task.Delay(500); return ServiceResult.Fail(ErrorCodes.TheWordCannotBeFound, "too slow"); });
            var nextCalled = false;
            var middleware = new WordMetaInjectionMiddleware(_ => { nextCalled = true; return Task.CompletedTask; }, _envMock.Object, NullLogger())
            {
                LookupTimeout = TimeSpan.FromMilliseconds(20)
            };
            var context = MakeContext("/english/dictionary/apple");

            await middleware.InvokeAsync(context, _dictionaryServiceMock.Object);

            Assert.True(nextCalled);
        }

        [Fact]
        public async Task InvokeAsync_WritesInjectedHtml_ForMatchingWordPath()
        {
            _dictionaryServiceMock.Setup(s => s.GetWordAsync("apple"))
                .ReturnsAsync(ServiceResult.Ok(MakeWord("English Coach - apple", "apple (noun): a fruit")));
            var nextCalled = false;
            var middleware = new WordMetaInjectionMiddleware(_ => { nextCalled = true; return Task.CompletedTask; }, _envMock.Object, NullLogger());
            var context = MakeContext("/english/dictionary/apple");
            var responseBody = new MemoryStream();
            context.Response.Body = responseBody;

            await middleware.InvokeAsync(context, _dictionaryServiceMock.Object);

            Assert.False(nextCalled);
            Assert.Equal(200, context.Response.StatusCode);
            Assert.Equal("text/html; charset=utf-8", context.Response.ContentType);
            Assert.Equal("no-store", context.Response.Headers["Cache-Control"].ToString());
            responseBody.Seek(0, SeekOrigin.Begin);
            var body = new StreamReader(responseBody).ReadToEnd();
            Assert.Contains("<title>English Coach - apple</title>", body);
            Assert.Contains("rel=\"canonical\"", body);
        }

        [Fact]
        public async Task InvokeAsync_FallsThrough_ForNonGetHeadMethods()
        {
            var nextCalled = false;
            var middleware = new WordMetaInjectionMiddleware(_ => { nextCalled = true; return Task.CompletedTask; }, _envMock.Object, NullLogger());
            var context = MakeContext("/english/dictionary/apple");
            context.Request.Method = "POST";

            await middleware.InvokeAsync(context, _dictionaryServiceMock.Object);

            Assert.True(nextCalled);
            _dictionaryServiceMock.Verify(s => s.GetWordAsync(It.IsAny<string>()), Times.Never);
        }

        private static ILogger<WordMetaInjectionMiddleware> NullLogger() =>
            new Mock<ILogger<WordMetaInjectionMiddleware>>().Object;

        private static IFileProvider MakeFileProvider(string content)
        {
            var fileInfoMock = new Mock<IFileInfo>();
            fileInfoMock.Setup(f => f.Exists).Returns(true);
            fileInfoMock.Setup(f => f.CreateReadStream()).Returns(() => new MemoryStream(Encoding.UTF8.GetBytes(content)));

            var providerMock = new Mock<IFileProvider>();
            providerMock.Setup(p => p.GetFileInfo("index.html")).Returns(fileInfoMock.Object);
            return providerMock.Object;
        }

        private static DefaultHttpContext MakeContext(string path)
        {
            var context = new DefaultHttpContext();
            context.Request.Path = new PathString(path);
            context.Request.Method = "GET";
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
