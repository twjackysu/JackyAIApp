using System.Net;
using System.Text.RegularExpressions;
using JackyAIApp.Server.Controllers;
using JackyAIApp.Server.Services.Dictionary;
using Microsoft.Extensions.Caching.Memory;

namespace JackyAIApp.Server.Common
{
    /// <summary>
    /// Server-side injects word-specific &lt;title&gt;/meta description/canonical into the SPA's
    /// built <c>index.html</c> for GET/HEAD requests to a word-detail path
    /// (<see cref="SitemapController.DictionaryPathPrefix"/>/{word}), for every user agent — real
    /// users and JS-executing crawlers (e.g. Googlebot) alike. Link-preview bots never reach this
    /// middleware: <see cref="BotPreviewMiddleware"/>, registered earlier in the pipeline, already
    /// serves them a dedicated shell and returns. SPA scripts are left untouched, so client-side
    /// rendering/routing still works normally after injection (TASK-020 "Phase B" — see
    /// .agent/architecture/ARCHITECTURE.md).
    ///
    /// Never breaks page load: an unknown word, a lookup error/timeout, a missing template, or any
    /// other exception all fall through to the unmodified SPA fallback. Registered in Program.cs
    /// before <c>UseStaticFiles</c>/<c>MapFallbackToFile</c>.
    /// </summary>
    public partial class WordMetadataInjectionMiddleware(
        RequestDelegate next,
        IWebHostEnvironment env,
        IMemoryCache memoryCache,
        ILogger<WordMetadataInjectionMiddleware> logger)
    {
        private readonly RequestDelegate _next = next;
        private readonly IWebHostEnvironment _env = env;
        private readonly IMemoryCache _memoryCache = memoryCache;
        private readonly ILogger<WordMetadataInjectionMiddleware> _logger = logger;

        /// <summary>
        /// Upper bound on the word lookup so a slow/hanging dependency never stalls page load.
        /// Mutable (rather than <c>const</c>/<c>readonly</c>) only so tests can shrink it instead
        /// of sleeping multiple seconds to exercise the timeout fallback.
        /// </summary>
        internal static TimeSpan LookupTimeout = TimeSpan.FromSeconds(3);

        /// <summary>
        /// No expiration: the built index.html template never changes for the life of the process,
        /// and a deploy restarts the process (wiping <see cref="IMemoryCache"/>), so there's no
        /// stale-after-deploy risk — see <c>Program.cs</c>'s startup EF migration comment for the
        /// same restart-per-deploy assumption elsewhere in this app.
        /// </summary>
        private const string TemplateCacheKey = "WordMetadataInjection_IndexHtmlTemplate";

        public async Task InvokeAsync(HttpContext context, IDictionaryService dictionaryService)
        {
            if (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method))
            {
                await _next(context);
                return;
            }

            var word = BotPreviewMiddleware.TryExtractWord(context.Request.Path);
            if (word == null)
            {
                await _next(context);
                return;
            }

            string? html;
            try
            {
                html = await TryBuildInjectedHtmlAsync(context, dictionaryService, word);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Word metadata injection failed for {Word}; falling back to the unmodified SPA shell.", word);
                html = null;
            }

            if (html == null)
            {
                await _next(context);
                return;
            }

            context.Response.StatusCode = StatusCodes.Status200OK;
            context.Response.ContentType = "text/html; charset=utf-8";
            // Per-word dynamic content — never let a browser/CDN keep serving a stale word (or a
            // stale template from before a deploy) once this response has been cached.
            context.Response.Headers["Cache-Control"] = "no-store";

            if (!HttpMethods.IsHead(context.Request.Method))
            {
                await context.Response.WriteAsync(html);
            }
        }

        private async Task<string?> TryBuildInjectedHtmlAsync(HttpContext context, IDictionaryService dictionaryService, string word)
        {
            var template = await GetTemplateAsync();
            if (template == null)
            {
                return null;
            }

            var lookupTask = dictionaryService.GetWordAsync(word);
            var timeoutTask = Task.Delay(LookupTimeout, context.RequestAborted);
            var completed = await Task.WhenAny(lookupTask, timeoutTask);
            if (completed == timeoutTask)
            {
                _logger.LogWarning("Word metadata lookup timed out for {Word}.", word);
                return null;
            }

            var result = await lookupTask;
            if (!result.IsSuccess || result.Data is not DTO.Word dto)
            {
                return null;
            }

            var baseUrl = $"{context.Request.Scheme}://{context.Request.Host}";
            var pageUrl = $"{baseUrl}{SitemapController.DictionaryPathPrefix}/{Uri.EscapeDataString(word)}";
            return Inject(template, dto, pageUrl);
        }

        private async Task<string?> GetTemplateAsync()
        {
            if (_memoryCache.TryGetValue(TemplateCacheKey, out string? cached))
            {
                return cached;
            }

            var webRootPath = _env.WebRootPath;
            if (string.IsNullOrEmpty(webRootPath))
            {
                return null;
            }

            var path = Path.Combine(webRootPath, "index.html");
            if (!File.Exists(path))
            {
                return null;
            }

            var template = await File.ReadAllTextAsync(path);
            // A missing template on one request and a present one on the next (e.g. Development,
            // where wwwroot may not exist at all) are both legitimate — only cache a hit, so a
            // miss doesn't get pinned in permanently.
            _memoryCache.Set(TemplateCacheKey, template);
            return template;
        }

        /// <summary>
        /// Replaces the template's &lt;title&gt; and description &lt;meta&gt; with
        /// <paramref name="word"/>'s SEO text (HTML-encoded) and inserts a canonical &lt;link&gt;
        /// pointing at <paramref name="pageUrl"/>, matching the shape the frontend's own
        /// react-helmet-async tags render at (<c>Dictionary/index.tsx</c>). Pure so it's testable
        /// without an <see cref="HttpContext"/>.
        /// </summary>
        internal static string Inject(string template, DTO.Word word, string pageUrl)
        {
            var title = WebUtility.HtmlEncode(word.SeoTitle);
            var description = WebUtility.HtmlEncode(word.SeoDescription);
            var url = WebUtility.HtmlEncode(pageUrl);

            var html = TitleTagRegex().Replace(template, _ => $"<title>{title}</title>", 1);

            var descriptionMetaTag = $"<meta name=\"description\" content=\"{description}\" />";
            html = DescriptionMetaRegex().IsMatch(html)
                ? DescriptionMetaRegex().Replace(html, _ => descriptionMetaTag, 1)
                : InsertBeforeHeadClose(html, descriptionMetaTag);

            var canonicalLinkTag = $"<link rel=\"canonical\" href=\"{url}\" />";
            html = InsertBeforeHeadClose(html, canonicalLinkTag);

            return html;
        }

        private static string InsertBeforeHeadClose(string html, string fragment)
        {
            var index = html.IndexOf("</head>", StringComparison.OrdinalIgnoreCase);
            return index < 0 ? html : html.Insert(index, fragment + "\n");
        }

        [GeneratedRegex("<title>.*?</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
        private static partial Regex TitleTagRegex();

        [GeneratedRegex("<meta\\s+name=\"description\"[^>]*/?>", RegexOptions.IgnoreCase)]
        private static partial Regex DescriptionMetaRegex();
    }
}
