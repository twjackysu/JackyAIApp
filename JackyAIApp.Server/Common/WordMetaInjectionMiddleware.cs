using JackyAIApp.Server.Controllers;
using JackyAIApp.Server.Services.Dictionary;
using System.Text.RegularExpressions;

namespace JackyAIApp.Server.Common
{
    /// <summary>
    /// Serves the SPA's <c>index.html</c> with word-specific <c>&lt;title&gt;</c>, meta
    /// description and canonical link injected, for <em>every</em> requester (real users,
    /// Googlebot, anyone) hitting a word-detail path — see TASK-020 ("SEO Phase B") in
    /// .agent/architecture/ARCHITECTURE.md. This is distinct from <see cref="BotPreviewMiddleware"/>,
    /// which only serves a minimal non-JS shell to known link-preview crawlers (TASK-003, kept
    /// as-is); this middleware instead patches the real SPA shell so JS still boots normally.
    /// Registered after <see cref="BotPreviewMiddleware"/> so its bot-preview behavior is
    /// untouched, and before the static-file/fallback pipeline in Program.cs.
    /// </summary>
    public class WordMetaInjectionMiddleware(
        RequestDelegate next,
        IWebHostEnvironment env,
        ILogger<WordMetaInjectionMiddleware> logger)
    {
        private readonly RequestDelegate _next = next;
        private readonly IWebHostEnvironment _env = env;
        private readonly ILogger<WordMetaInjectionMiddleware> _logger = logger;

        /// <summary>
        /// How long to wait on the word lookup before giving up and serving the unmodified
        /// template. The lookup can fall through to an OpenAI generation call for a word never
        /// looked up before, which must never block a page load indefinitely.
        /// </summary>
        internal TimeSpan LookupTimeout { get; init; } = TimeSpan.FromSeconds(3);

        private string? _cachedTemplate;

        public async Task InvokeAsync(HttpContext context, IDictionaryService dictionaryService)
        {
            var request = context.Request;
            if (!HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method))
            {
                await _next(context);
                return;
            }

            var word = BotPreviewMiddleware.TryExtractWord(request.Path);
            if (word == null)
            {
                await _next(context);
                return;
            }

            try
            {
                var template = GetTemplate();
                if (template == null)
                {
                    await _next(context);
                    return;
                }

                var lookupTask = dictionaryService.GetWordAsync(word);
                var winner = await Task.WhenAny(lookupTask, Task.Delay(LookupTimeout));
                if (winner != lookupTask)
                {
                    _logger.LogWarning(
                        "Word lookup for {Word} exceeded {Timeout}; serving unmodified index.html",
                        word, LookupTimeout);
                    await _next(context);
                    return;
                }

                var result = await lookupTask;
                if (!result.IsSuccess || result.Data is not DTO.Word dto)
                {
                    await _next(context);
                    return;
                }

                var baseUrl = $"{request.Scheme}://{request.Host}";
                var pageUrl = $"{baseUrl}{SitemapController.DictionaryPathPrefix}/{Uri.EscapeDataString(word)}";
                var html = InjectMetadata(template, dto, pageUrl);

                context.Response.StatusCode = StatusCodes.Status200OK;
                context.Response.ContentType = "text/html; charset=utf-8";
                // Per-word content on a shared path — never let an intermediary cache serve one
                // word's page (or a template from before a deploy) for another request.
                context.Response.Headers["Cache-Control"] = "no-store";
                if (!HttpMethods.IsHead(request.Method))
                {
                    await context.Response.WriteAsync(html);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to inject word metadata for {Word}; falling back to default index.html", word);
                await _next(context);
            }
        }

        /// <summary>Reads and caches <c>index.html</c> from the webroot on first use.</summary>
        private string? GetTemplate()
        {
            if (_cachedTemplate != null)
            {
                return _cachedTemplate;
            }

            var file = _env.WebRootFileProvider.GetFileInfo("index.html");
            if (!file.Exists)
            {
                return null;
            }

            using var stream = file.CreateReadStream();
            using var reader = new StreamReader(stream);
            _cachedTemplate = reader.ReadToEnd();
            return _cachedTemplate;
        }

        private static readonly Regex TitleRegex = new("<title>.*?</title>", RegexOptions.Singleline | RegexOptions.Compiled);
        private static readonly Regex DescriptionMetaRegex = new(
            "<meta\\s+name=\"description\"\\s+content=\"[^\"]*\"\\s*/?>",
            RegexOptions.Compiled);

        /// <summary>Pure so it's testable without an <see cref="HttpContext"/> or a real template file.</summary>
        internal static string InjectMetadata(string template, DTO.Word word, string pageUrl)
        {
            var title = System.Net.WebUtility.HtmlEncode(word.SeoTitle);
            // MatchEvaluator (not a replacement string) — a word's title/description can contain
            // "$", which Regex.Replace(string, string) would otherwise parse as a backreference.
            var html = TitleRegex.Replace(template, _ => $"<title>{title}</title>");

            if (!string.IsNullOrEmpty(word.SeoDescription))
            {
                var description = System.Net.WebUtility.HtmlEncode(word.SeoDescription);
                html = DescriptionMetaRegex.Replace(html, _ => $"<meta name=\"description\" content=\"{description}\" />");
            }

            var canonical = System.Net.WebUtility.HtmlEncode(pageUrl);
            html = html.Replace("</head>", $"<link rel=\"canonical\" href=\"{canonical}\" />\n</head>");

            return html;
        }
    }
}
