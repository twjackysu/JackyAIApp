using JackyAIApp.Server.Controllers;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.Services.Dictionary;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SQLWord = JackyAIApp.Server.Data.Models.SQL.Word;

namespace JackyAIApp.Server.Tests.Controllers
{
    public class SitemapControllerTests : IDisposable
    {
        private readonly AzureSQLDBContext _dbContext;
        private readonly SitemapController _controller;

        public SitemapControllerTests()
        {
            var options = new DbContextOptionsBuilder<AzureSQLDBContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;
            _dbContext = new AzureSQLDBContext(options);

            _controller = new SitemapController(new PublicWordList(_dbContext))
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext
                    {
                        Request = { Scheme = "https", Host = new HostString("example.com") }
                    }
                }
            };
        }

        public void Dispose() => _dbContext.Dispose();

        [Fact]
        public void BuildSitemapXml_EscapesAndPrefixesWordPath()
        {
            var xml = SitemapController.BuildSitemapXml(new[] { "apple", "ice cream" }, "https://example.com");

            Assert.Contains("<loc>https://example.com/english/dictionary/apple</loc>", xml);
            Assert.Contains("<loc>https://example.com/english/dictionary/ice%20cream</loc>", xml);
            Assert.StartsWith("<?xml", xml);
            Assert.Contains("<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">", xml);
        }

        [Fact]
        public void BuildSitemapXml_AddsIsoLastmod_OnlyWhenKnown()
        {
            var xml = SitemapController.BuildSitemapXml(new (string, DateTime?)[]
            {
                ("apple", new DateTime(2026, 3, 5, 23, 59, 0)),
                ("pear", null),
            }, "https://example.com");

            Assert.Contains("apple</loc><lastmod>2026-03-05</lastmod></url>", xml);
            Assert.Contains("pear</loc></url>", xml);
        }

        [Fact]
        public void BuildRobotsTxt_AllowsAllAndPointsToSitemap()
        {
            var robots = SitemapController.BuildRobotsTxt("https://example.com");

            Assert.Contains("Allow: /", robots);
            Assert.Contains("Sitemap: https://example.com/sitemap.xml", robots);
        }

        [Fact]
        public async Task GetSitemap_ExcludesInvalidWords()
        {
            await SeedWordAsync("apple", isInvalid: false);
            await SeedWordAsync("stale", isInvalid: true);

            var result = Assert.IsType<ContentResult>(await _controller.GetSitemap());

            Assert.Contains("english/dictionary/apple", result.Content);
            Assert.DoesNotContain("english/dictionary/stale", result.Content);
            Assert.Equal("application/xml", result.ContentType?.Split(';')[0]);
        }

        [Fact]
        public async Task GetRobotsTxt_ReturnsPlainTextPointingAtSitemap()
        {
            var result = Assert.IsType<ContentResult>(_controller.GetRobotsTxt());

            Assert.Contains("Sitemap: https://example.com/sitemap.xml", result.Content);
        }

        private async Task SeedWordAsync(string wordText, bool isInvalid)
        {
            _dbContext.Words.Add(new SQLWord
            {
                Id = Guid.NewGuid().ToString(),
                WordText = wordText,
                KKPhonics = $"/{wordText}/",
                DateAdded = DateTime.UtcNow,
                LastUpdated = DateTime.UtcNow,
                DataInvalid = isInvalid ? true : null
            });
            await _dbContext.SaveChangesAsync();
        }
    }
}
