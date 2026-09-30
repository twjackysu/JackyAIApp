using JackyAIApp.Server.Controllers;
using JackyAIApp.Server.Services.Dictionary;
using Microsoft.Extensions.DependencyInjection;
using System.Text.RegularExpressions;

namespace JackyAIApp.Server.Common
{
    /// <summary>
    /// For a word-detail page (<see cref="SitemapController.DictionaryPathPrefix"/>/{word}), serves the
    /// SPA's built <c>index.html</c> with the word's title/meta-description/canonical link injected —
    /// for every requester, not just the known link-preview bots <see cref="BotPreviewMiddleware"/>
    /// already handles (which still short-circuits first and is unaffected by this middleware). Unlike
    /// <see cref="BotPreviewMiddleware"/>'s from-scratch shell, this keeps the real SPA markup/scripts so
    /// a normal browser still boots the app; only the &lt;head&gt; metadata differs from the generic
    /// static file. Any failure (unknown word, lookup error/timeout, missing template, ...) falls through
    /// to the unmodified file — this must never be the reason a page fails to load.
    /// Registered in Program.cs after <see cref="BotPreviewMiddleware"/> and before the static-file/SPA
    /// fallback pipeline. See .agent/architecture/ARCHITECTURE.md ("Phase 2") and TASK-020.
    /// </summary>
    public class WordMetadataMiddleware(RequestDelegate next)
    {
        private readonly RequestDelegate _next = next;
        private readonly object _templateLock = new();
        private string? _cachedTemplate;

        public async Task InvokeAsync(HttpContext context, IDictionaryService dictionaryService, IWebHostEnvironment env)
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

            var template = GetTemplate(env);
            if (template == null)
            {
                await _next(context);
                return;
            }

            DTO.Word? dto;
            try
            {
                var result = await dictionaryService.GetWordAsync(word);
                dto = result.IsSuccess ? result.Data as DTO.Word : null;
            }
            catch (Exception ex)
            {
                var logger = context.RequestServices.GetService<ILogger<WordMetadataMiddleware>>();
                logger?.LogWarning(ex, "Word metadata lookup failed for {Word}; serving unmodified index.html", word);
                dto = null;
            }

            if (dto == null)
            {
                await _next(context);
                return;
            }

            var baseUrl = $"{context.Request.Scheme}://{context.Request.Host}";
            var canonicalUrl = $"{baseUrl}{SitemapController.DictionaryPathPrefix}/{Uri.EscapeDataString(word)}";

            context.Response.StatusCode = StatusCodes.Status200OK;
            context.Response.ContentType = "text/html; charset=utf-8";
            // Response is built from live word data — never let a browser/CDN cache one word's
            // markup and serve it back for a different request, and never let a pre-deploy copy
            // outlive the deploy that changed the underlying template or SEO text.
            context.Response.Headers.CacheControl = "no-store";
            await context.Response.WriteAsync(InjectMetadata(template, dto, canonicalUrl));
        }

        private string? GetTemplate(IWebHostEnvironment env)
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

                try
                {
                    var path = Path.Combine(env.WebRootPath ?? string.Empty, "index.html");
                    if (!File.Exists(path))
                    {
                        return null;
                    }

                    _cachedTemplate = File.ReadAllText(path);
                }
                catch
                {
                    return null;
                }
            }

            return _cachedTemplate;
        }

        private static readonly Regex TitleTagRegex = new("<title>.*?</title>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        private static readonly Regex DescriptionMetaRegex = new(
            "<meta\\s+name=\"description\"\\s+content=\"[^\"]*\"\\s*/?>", RegexOptions.IgnoreCase);
        private static readonly Regex CanonicalLinkRegex = new(
            "<link\\s+rel=\"canonical\"[^>]*>\\s*", RegexOptions.IgnoreCase);

        /// <summary>Pure so it's testable without an <see cref="HttpContext"/> or a real file on disk.</summary>
        internal static string InjectMetadata(string template, DTO.Word word, string canonicalUrl)
        {
            var title = System.Net.WebUtility.HtmlEncode(word.SeoTitle);
            var description = System.Net.WebUtility.HtmlEncode(word.SeoDescription);
            var url = System.Net.WebUtility.HtmlEncode(canonicalUrl);

            // MatchEvaluator overload, not the string-replacement one: a "$"/"$1"-shaped
            // substring in AI-generated SEO text would otherwise be parsed as a backreference.
            var html = TitleTagRegex.Replace(template, _ => $"<title>{title}</title>");

            var descriptionTag = $"<meta name=\"description\" content=\"{description}\" />";
            html = DescriptionMetaRegex.IsMatch(html)
                ? DescriptionMetaRegex.Replace(html, _ => descriptionTag)
                : InsertBeforeHeadClose(html, descriptionTag);

            // The base template never ships a canonical link (each word's URL differs), but strip
            // one first so re-running this against already-injected HTML stays idempotent.
            html = CanonicalLinkRegex.Replace(html, string.Empty);
            html = InsertBeforeHeadClose(html, $"<link rel=\"canonical\" href=\"{url}\" />");

            return html;
        }

        private static string InsertBeforeHeadClose(string html, string tag)
        {
            const string headClose = "</head>";
            var index = html.IndexOf(headClose, StringComparison.OrdinalIgnoreCase);
            return index < 0 ? html : html.Insert(index, tag + "\n");
        }
    }
}
