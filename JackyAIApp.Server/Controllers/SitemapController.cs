using JackyAIApp.Server.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text;

namespace JackyAIApp.Server.Controllers
{
    /// <summary>
    /// Crawl-discovery endpoints for the public Dictionary word pages (Growth &amp; Retention
    /// SEO — see .agent/architecture/ARCHITECTURE.md). Anonymous by design, same audience as
    /// <see cref="DictionaryController.Get"/>.
    /// </summary>
    [ApiController]
    public class SitemapController(AzureSQLDBContext dbContext) : ControllerBase
    {
        private readonly AzureSQLDBContext _dbContext = dbContext;

        /// <summary>Path prefix of the frontend's public word-detail route (TASK-002).</summary>
        internal const string DictionaryPathPrefix = "/english/dictionary";

        [HttpGet("/sitemap.xml")]
        public async Task<IActionResult> GetSitemap()
        {
            // Cheap by design: a single projection over the indexed WordText column, no per-word
            // AI calls. Word count is small enough today for one file — revisit with a sitemap
            // index + chunked sub-sitemaps only once this approaches Google's ~50k URL/file cap.
            var wordTexts = await _dbContext.Words
                .Where(w => !w.DataInvalid.HasValue || !w.DataInvalid.Value)
                .Select(w => w.WordText)
                .ToListAsync();

            var xml = BuildSitemapXml(wordTexts, BaseUrl());
            return Content(xml, "application/xml", Encoding.UTF8);
        }

        [HttpGet("/robots.txt")]
        public IActionResult GetRobotsTxt()
        {
            return Content(BuildRobotsTxt(BaseUrl()), "text/plain", Encoding.UTF8);
        }

        private string BaseUrl() => $"{Request.Scheme}://{Request.Host}";

        /// <summary>Pure so it's testable without an <see cref="HttpContext"/>.</summary>
        internal static string BuildSitemapXml(IEnumerable<string> wordTexts, string baseUrl)
        {
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n");
            sb.Append("<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">\n");
            foreach (var wordText in wordTexts)
            {
                var loc = $"{baseUrl}{DictionaryPathPrefix}/{Uri.EscapeDataString(wordText)}";
                sb.Append($"  <url><loc>{System.Security.SecurityElement.Escape(loc)}</loc></url>\n");
            }
            sb.Append("</urlset>");
            return sb.ToString();
        }

        /// <summary>Pure so it's testable without an <see cref="HttpContext"/>.</summary>
        internal static string BuildRobotsTxt(string baseUrl)
        {
            return "User-agent: *\n" +
                   "Allow: /\n" +
                   $"Sitemap: {baseUrl}/sitemap.xml\n";
        }
    }
}
