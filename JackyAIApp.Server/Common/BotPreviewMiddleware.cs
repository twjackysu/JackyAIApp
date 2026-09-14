using JackyAIApp.Server.Controllers;
using JackyAIApp.Server.Services.Dictionary;

namespace JackyAIApp.Server.Common
{
    /// <summary>
    /// Serves a minimal server-rendered HTML shell (title/meta description/OG tags) to known
    /// link-preview crawlers (LINE/Facebook/Twitter/Slack/Discord/WhatsApp — see
    /// <see cref="LinkPreviewBotUserAgents"/>) requesting a word-detail page. Those crawlers don't
    /// execute JavaScript, so without this they'd only ever see the SPA's near-empty initial HTML.
    /// Real users and Googlebot (which does execute JS, and already gets correct metadata via
    /// TASK-002's client-rendered tags) are untouched — every other request falls through
    /// unchanged. Registered early in Program.cs, before the SPA static-file/fallback pipeline.
    /// See .agent/architecture/ARCHITECTURE.md ("Phase 2").
    /// </summary>
    public class BotPreviewMiddleware(RequestDelegate next)
    {
        private readonly RequestDelegate _next = next;

        /// <summary>Generic site image used as og:image — no per-word image generation (out of scope).</summary>
        internal const string GenericOgImagePath = "/JackyAIIcon.png";

        public async Task InvokeAsync(HttpContext context, IDictionaryService dictionaryService)
        {
            var word = TryExtractWord(context.Request.Path);
            if (word == null || !LinkPreviewBotUserAgents.IsMatch(context.Request.Headers.UserAgent))
            {
                await _next(context);
                return;
            }

            var result = await dictionaryService.GetWordAsync(word);
            if (!result.IsSuccess || result.Data is not DTO.Word dto)
            {
                // Unknown/invalid word — nothing word-specific to show; fall through to the SPA
                // like any other unmatched case.
                await _next(context);
                return;
            }

            var baseUrl = $"{context.Request.Scheme}://{context.Request.Host}";
            var pageUrl = $"{baseUrl}{SitemapController.DictionaryPathPrefix}/{Uri.EscapeDataString(word)}";

            context.Response.StatusCode = StatusCodes.Status200OK;
            context.Response.ContentType = "text/html; charset=utf-8";
            await context.Response.WriteAsync(BuildHtml(dto, pageUrl, baseUrl));
        }

        /// <summary>
        /// Returns the word segment for a request path shaped like
        /// <see cref="SitemapController.DictionaryPathPrefix"/>/{word}, or null if the path isn't a
        /// single-segment word-detail path under that prefix.
        /// </summary>
        internal static string? TryExtractWord(PathString path)
        {
            if (!path.StartsWithSegments(SitemapController.DictionaryPathPrefix, out var remaining))
            {
                return null;
            }

            var segment = remaining.Value?.Trim('/');
            if (string.IsNullOrEmpty(segment) || segment.Contains('/'))
            {
                return null;
            }

            return Uri.UnescapeDataString(segment);
        }

        /// <summary>Pure so it's testable without an <see cref="HttpContext"/>.</summary>
        internal static string BuildHtml(DTO.Word word, string pageUrl, string baseUrl)
        {
            var title = System.Net.WebUtility.HtmlEncode(word.SeoTitle);
            var description = System.Net.WebUtility.HtmlEncode(word.SeoDescription);
            var url = System.Net.WebUtility.HtmlEncode(pageUrl);
            var image = System.Net.WebUtility.HtmlEncode($"{baseUrl}{GenericOgImagePath}");

            return "<!DOCTYPE html>\n" +
                   "<html>\n" +
                   "<head>\n" +
                   "<meta charset=\"utf-8\" />\n" +
                   $"<title>{title}</title>\n" +
                   $"<meta name=\"description\" content=\"{description}\" />\n" +
                   $"<meta property=\"og:title\" content=\"{title}\" />\n" +
                   $"<meta property=\"og:description\" content=\"{description}\" />\n" +
                   $"<meta property=\"og:url\" content=\"{url}\" />\n" +
                   $"<meta property=\"og:image\" content=\"{image}\" />\n" +
                   "</head>\n" +
                   "<body></body>\n" +
                   "</html>\n";
        }
    }
}
