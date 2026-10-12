using System.Text.RegularExpressions;
using JackyAIApp.Server.Common;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Moq;

namespace JackyAIApp.Server.Tests.Common
{
    /// <summary>
    /// Each app's link preview: LINE/Teams read only the raw HTML, which used to carry one title
    /// for the whole site ("English Coach"), so /pdf and /portfolio previewed as English Coach too.
    /// </summary>
    public class AppPageMetadataMiddlewareTests
    {
        // The shape of the built index.html, including the site-wide defaults it now ships with.
        private const string Template =
            "<!doctype html>\n<html lang=\"en\">\n<head>\n" +
            "<meta charset=\"UTF-8\" />\n" +
            "<meta name=\"description\" content=\"site description\" />\n" +
            "<meta property=\"og:type\" content=\"website\" />\n" +
            "<meta property=\"og:site_name\" content=\"Jacky Studio\" />\n" +
            "<meta property=\"og:title\" content=\"site title\" />\n" +
            "<meta property=\"og:description\" content=\"site description\" />\n" +
            "<meta property=\"og:image\" content=\"/JackyAIIcon.png\" />\n" +
            "<meta name=\"twitter:card\" content=\"summary\" />\n" +
            "<title>site title</title>\n" +
            "</head>\n<body><div id=\"root\"></div></body>\n</html>\n";

        // ── which metadata a path gets ───────────────────────────────────────

        [Theory]
        [InlineData("/pdf", "PDF Unlocker & Compressor | Jacky Studio", "解除 PDF 密碼保護並壓縮檔案大小。")]
        [InlineData("/portfolio", "Wealth Portfolio | Jacky Studio", "投資組合與資產追蹤")]
        [InlineData("/finance/stock/2330", "Stock Intelligence | Jacky Studio", "AI 股票分析與市場情報")]
        [InlineData("/english/exam", "English Coach | Jacky Studio", "AI 英文學習與單字訓練")]
        [InlineData("/jobs", "Job Finder | Jacky Studio", "職缺搜尋與工作媒合")]
        [InlineData("/PDF", "PDF Unlocker & Compressor | Jacky Studio", "解除 PDF 密碼保護並壓縮檔案大小。")]
        public void ForPath_GivesEachAppItsOwnTitleAndDescription(string path, string title, string description)
        {
            var meta = SitePages.ForPath(new PathString(path));

            Assert.Equal(title, meta.Title);
            Assert.Equal(description, meta.Description);
        }

        [Theory]
        [InlineData("/")]
        [InlineData("/login-error")]
        [InlineData("/portfolio-x")] // whole segments only: not the portfolio
        public void ForPath_AnythingElseGetsTheSiteHome(string path)
        {
            Assert.Equal(SitePages.Home, SitePages.ForPath(new PathString(path)));
            Assert.Equal("Jacky Studio — AI 學習、投資與實用工具", SitePages.Home.Title);
        }

        // ── rewriting the HTML ───────────────────────────────────────────────

        [Fact]
        public void Apply_ReplacesTitleDescriptionAndOpenGraphTagsWithoutDuplicating()
        {
            var html = HtmlMetadata.Apply(Template, "PDF tool", "Unlock PDFs", "https://example.com/pdf", "https://example.com/JackyAIIcon.png");

            Assert.Contains("<title>PDF tool</title>", html);
            Assert.Contains("<meta name=\"description\" content=\"Unlock PDFs\" />", html);
            Assert.Contains("<meta property=\"og:title\" content=\"PDF tool\" />", html);
            Assert.Contains("<meta property=\"og:description\" content=\"Unlock PDFs\" />", html);
            Assert.Contains("<meta property=\"og:url\" content=\"https://example.com/pdf\" />", html);
            Assert.Contains("<meta property=\"og:image\" content=\"https://example.com/JackyAIIcon.png\" />", html);
            Assert.DoesNotContain("site title", html);
            Assert.DoesNotContain("site description", html);
            foreach (var tag in new[] { "og:title", "og:description", "og:url", "og:image", "og:type", "og:site_name", "twitter:card" })
                Assert.Single(Regex.Matches(html, $"\"{tag}\""));
        }

        [Fact]
        public void Apply_AddsTheTagsATemplateIsMissing()
        {
            const string bare = "<html><head><title>x</title><meta name=\"description\" content=\"y\" /></head><body></body></html>";

            var html = HtmlMetadata.Apply(bare, "T", "D", "https://example.com/", "https://example.com/i.png");

            Assert.Contains("<meta property=\"og:title\" content=\"T\" />", html);
            Assert.Contains("<meta property=\"og:image\" content=\"https://example.com/i.png\" />", html);
            Assert.True(html.IndexOf("og:title", StringComparison.Ordinal) < html.IndexOf("</head>", StringComparison.Ordinal));
        }

        [Fact]
        public void Apply_HtmlEncodesAndKeepsDollarSignsLiteral()
        {
            var html = HtmlMetadata.Apply(Template, "A & B </title><script>", "costs $1 $&", "https://example.com/a?b=\"c\"", "https://example.com/i.png");

            Assert.DoesNotContain("<script>", html);
            Assert.Contains("<title>A &amp; B &lt;/title&gt;&lt;script&gt;</title>", html);
            Assert.Contains("content=\"costs $1 $&amp;\"", html);
            Assert.Contains("content=\"https://example.com/a?b=&quot;c&quot;\"", html);
        }

        // ── the middleware ───────────────────────────────────────────────────

        [Fact]
        public async Task InvokeAsync_ServesThePagesOwnMetadata()
        {
            var nextCalled = false;
            var middleware = new AppPageMetadataMiddleware(_ => { nextCalled = true; return Task.CompletedTask; }, MakeEnv(Template));
            var context = MakeContext("/pdf", "GET");

            await middleware.InvokeAsync(context);

            Assert.False(nextCalled);
            Assert.Equal(200, context.Response.StatusCode);
            Assert.Equal("text/html; charset=utf-8", context.Response.ContentType);
            Assert.Equal("no-store", context.Response.Headers.CacheControl.ToString());
            var body = ReadBody(context);
            Assert.Contains("<title>PDF Unlocker &amp; Compressor | Jacky Studio</title>", body);
            Assert.Contains("<meta property=\"og:url\" content=\"https://example.com/pdf\" />", body);
            Assert.Contains("<meta property=\"og:image\" content=\"https://example.com/JackyAIIcon.png\" />", body);
        }

        [Fact]
        public async Task InvokeAsync_HomeGetsTheSiteTitle()
        {
            var middleware = new AppPageMetadataMiddleware(_ => Task.CompletedTask, MakeEnv(Template));
            var context = MakeContext("/", "GET");

            await middleware.InvokeAsync(context);

            Assert.Contains("<title>Jacky Studio — AI 學習、投資與實用工具</title>", ReadBody(context));
        }

        [Theory]
        [InlineData("/api/portfolio/summary", "GET")]
        [InlineData("/assets/index-abc123.js", "GET")]
        [InlineData("/sitemap.xml", "GET")]
        [InlineData("/swagger", "GET")]
        [InlineData("/signin-google", "GET")]
        [InlineData("/pdf", "POST")]
        public async Task InvokeAsync_LeavesAnythingButPageNavigationsAlone(string path, string method)
        {
            var nextCalled = false;
            var middleware = new AppPageMetadataMiddleware(_ => { nextCalled = true; return Task.CompletedTask; }, MakeEnv(Template));
            var context = MakeContext(path, method);

            await middleware.InvokeAsync(context);

            Assert.True(nextCalled);
            Assert.Equal(0, context.Response.Body.Length);
        }

        [Fact]
        public async Task InvokeAsync_WithoutABuiltIndexHtml_FallsThrough()
        {
            // Local development: Vite serves the page and there is no wwwroot/index.html.
            var nextCalled = false;
            var middleware = new AppPageMetadataMiddleware(_ => { nextCalled = true; return Task.CompletedTask; }, MakeEnv(null));

            await middleware.InvokeAsync(MakeContext("/pdf", "GET"));

            Assert.True(nextCalled);
        }

        [Fact]
        public async Task InvokeAsync_Head_SetsHeadersButWritesNoBody()
        {
            var middleware = new AppPageMetadataMiddleware(_ => Task.CompletedTask, MakeEnv(Template));
            var context = MakeContext("/portfolio", "HEAD");

            await middleware.InvokeAsync(context);

            Assert.Equal(200, context.Response.StatusCode);
            Assert.True(context.Response.ContentLength > 0);
            Assert.Equal(0, context.Response.Body.Length);
        }

        private static DefaultHttpContext MakeContext(string path, string method)
        {
            var context = new DefaultHttpContext();
            context.Request.Path = new PathString(path);
            context.Request.Method = method;
            context.Request.Scheme = "https";
            context.Request.Host = new HostString("example.com");
            context.Response.Body = new MemoryStream();
            return context;
        }

        private static string ReadBody(HttpContext context)
        {
            context.Response.Body.Seek(0, SeekOrigin.Begin);
            return new StreamReader(context.Response.Body).ReadToEnd();
        }

        private static IWebHostEnvironment MakeEnv(string? template)
        {
            var fileInfo = new Mock<IFileInfo>();
            fileInfo.Setup(f => f.Exists).Returns(template != null);
            if (template != null)
            {
                fileInfo.Setup(f => f.CreateReadStream())
                    .Returns(() => new MemoryStream(System.Text.Encoding.UTF8.GetBytes(template)));
            }
            var fileProvider = new Mock<IFileProvider>();
            fileProvider.Setup(p => p.GetFileInfo("index.html")).Returns(fileInfo.Object);
            var env = new Mock<IWebHostEnvironment>();
            env.Setup(e => e.WebRootFileProvider).Returns(fileProvider.Object);
            return env.Object;
        }
    }
}
