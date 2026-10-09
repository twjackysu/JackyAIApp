using System.Text.RegularExpressions;
using JackyAIApp.Server.Controllers;
using JackyAIApp.Server.Services.Dictionary;

namespace JackyAIApp.Server.Common
{
    /// <summary>
    /// Injects word-specific &lt;title&gt;/meta description/canonical link into the SPA's built
    /// index.html for GET/HEAD requests to <see cref="SitemapController.DictionaryPathPrefix"/>/{word},
    /// so the raw HTML — for every user agent, not just link-preview bots — carries per-word metadata
    /// instead of the generic site defaults. See TASK-020 / .agent/architecture/ARCHITECTURE.md ("SEO
    /// Phase B"). Reuses <see cref="BotPreviewMiddleware.TryExtractWord"/> for path matching and
    /// <see cref="DTO.Word.SeoTitle"/>/<see cref="DTO.Word.SeoDescription"/> — the same single source of
    /// SEO text used by <see cref="Services.Dictionary.WordSeoTextFormatter"/> — so nothing is
    /// reformatted a second time.
    /// <para>
    /// Any failure (unmatched path, non-GET/HEAD method, missing template, unknown word, lookup
    /// error/exception, or a slow lookup) falls through unchanged to the normal static-file/SPA-fallback
    /// pipeline — this must never break page load. Registered in Program.cs after
    /// <see cref="BotPreviewMiddleware"/> (which already fully handles known bot user agents) and before
    /// the static-file pipeline.
    /// </para>
    /// </summary>
    public class WordPageMetadataMiddleware(RequestDelegate next, IWebHostEnvironment env)
    {
        private readonly RequestDelegate _next = next;
        private readonly IWebHostEnvironment _env = env;
        private static readonly TimeSpan LookupTimeout = TimeSpan.FromSeconds(3);

        private readonly object _templateLock = new();
        private string? _cachedTemplate;

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

            var template = GetTemplate();
            if (template == null)
            {
                await _next(context);
                return;
            }

            var dto = await TryGetWordAsync(dictionaryService, word);
            if (dto == null)
            {
                await _next(context);
                return;
            }

            string html;
            try
            {
                var baseUrl = $"{context.Request.Scheme}://{context.Request.Host}";
                var pageUrl = $"{baseUrl}{SitemapController.DictionaryPathPrefix}/{Uri.EscapeDataString(word)}";
                html = InjectMetadata(template, dto, pageUrl);
            }
            catch
            {
                // Malformed template or unexpected injection failure — never break page load.
                await _next(context);
                return;
            }

            context.Response.StatusCode = StatusCodes.Status200OK;
            context.Response.ContentType = "text/html; charset=utf-8";
            // The template is cached in memory for this process's lifetime (see GetTemplate); prevent a
            // browser/CDN from caching a per-word response past a deploy that changes the underlying
            // build (new asset hashes, updated word data, etc.).
            context.Response.Headers["Cache-Control"] = "no-store";

            // HEAD gets the same headers but no body, per HTTP semantics.
            if (HttpMethods.IsHead(context.Request.Method))
            {
                context.Response.ContentLength = System.Text.Encoding.UTF8.GetByteCount(html);
                return;
            }

            await context.Response.WriteAsync(html);
        }

        private static async Task<DTO.Word?> TryGetWordAsync(IDictionaryService dictionaryService, string word)
        {
            try
            {
                var lookupTask = dictionaryService.GetWordAsync(word);
                var winner = await Task.WhenAny(lookupTask, Task.Delay(LookupTimeout));
                if (winner != lookupTask)
                {
                    return null;
                }

                var result = await lookupTask;
                return result.IsSuccess && result.Data is DTO.Word dto ? dto : null;
            }
            catch
            {
                return null;
            }
        }

        private string? GetTemplate()
        {
            if (_cachedTemplate != null)
            {
                return _cachedTemplate;
            }

            lock (_templateLock)
            {
                if (_cachedTemplate != null)
                {
                    return _cachedTemplate;
                }

                var fileInfo = _env.WebRootFileProvider.GetFileInfo("index.html");
                if (!fileInfo.Exists)
                {
                    return null;
                }

                using var stream = fileInfo.CreateReadStream();
                using var reader = new StreamReader(stream);
                _cachedTemplate = reader.ReadToEnd();
            }

            return _cachedTemplate;
        }

        /// <summary>Pure so it's testable without an <see cref="HttpContext"/> or a file provider.</summary>
        internal static string InjectMetadata(string template, DTO.Word word, string pageUrl)
        {
            var title = System.Net.WebUtility.HtmlEncode(word.SeoTitle);
            var description = System.Net.WebUtility.HtmlEncode(word.SeoDescription);
            var canonicalUrl = System.Net.WebUtility.HtmlEncode(pageUrl);

            // MatchEvaluator lambdas, not replacement strings — a replacement string would let a
            // literal "$" in word text (e.g. a definition mentioning a price) be misread by
            // Regex.Replace as a backreference token ($1, $&, ...) and silently corrupt the output.
            var html = Regex.Replace(template, "<title>[\\s\\S]*?</title>", _ => $"<title>{title}</title>", RegexOptions.IgnoreCase);
            html = Regex.Replace(html, "<meta[^>]*name=\"description\"[^>]*/?>", _ => $"<meta name=\"description\" content=\"{description}\" />", RegexOptions.IgnoreCase);

            var canonicalTag = $"<link rel=\"canonical\" href=\"{canonicalUrl}\" />";
            html = Regex.IsMatch(html, "<link[^>]*rel=\"canonical\"[^>]*/?>", RegexOptions.IgnoreCase)
                ? Regex.Replace(html, "<link[^>]*rel=\"canonical\"[^>]*/?>", _ => canonicalTag, RegexOptions.IgnoreCase)
                : Regex.Replace(html, "</head>", _ => $"{canonicalTag}\n</head>", RegexOptions.IgnoreCase);

            // JSON-LD (TASK-034): serialized, never string-built; "<" is \u003C-escaped so word data
            // can't close the script element. System.Text.Json's default encoder already escapes
            // < > & ' as \uXXXX, and the explicit replace is defence in depth.
            var jsonLd = BuildJsonLd(word, pageUrl).Replace("<", "\\u003C");
            var jsonLdTag = $"<script type=\"application/ld+json\">{jsonLd}</script>";
            return Regex.Replace(html, "</head>", _ => $"{jsonLdTag}\n</head>", RegexOptions.IgnoreCase);
        }

        /// <summary>schema.org DefinedTerm for the word page. Pure and serializer-built.</summary>
        internal static string BuildJsonLd(DTO.Word word, string pageUrl)
        {
            return System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["@context"] = "https://schema.org",
                ["@type"] = "DefinedTerm",
                ["name"] = word.Word,
                ["description"] = word.SeoDescription,
                ["inLanguage"] = "en",
                ["url"] = pageUrl,
            });
        }
    }
}
