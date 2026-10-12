namespace JackyAIApp.Server.Common
{
    /// <summary>
    /// Serves the SPA's index.html with the metadata of the app being opened (<see cref="SitePages"/>),
    /// so a shared /pdf link previews as the PDF tool and /portfolio as the portfolio, not the one
    /// title baked into the build. Link-preview crawlers read only this raw HTML; the SPA's own
    /// &lt;title&gt; (react-helmet) arrives too late for them.
    /// <para>
    /// Same approach as <see cref="WordPageMetadataMiddleware"/>, which runs first and keeps
    /// handling dictionary word pages with their per-word text. Only page navigations are
    /// touched: GET/HEAD, not under /api, /swagger or the OAuth callback, and not a file (any path
    /// whose last segment has an extension). Anything that goes wrong falls through to the normal
    /// static-file/SPA-fallback pipeline — this must never break page load.
    /// </para>
    /// </summary>
    public class AppPageMetadataMiddleware(RequestDelegate next, IWebHostEnvironment env)
    {
        private readonly IndexHtmlTemplate _template = new(env);

        public async Task InvokeAsync(HttpContext context)
        {
            if (!IsPageRequest(context.Request))
            {
                await next(context);
                return;
            }

            var template = _template.Get();
            if (template == null)
            {
                await next(context);
                return;
            }

            string html;
            try
            {
                var baseUrl = $"{context.Request.Scheme}://{context.Request.Host}";
                var meta = SitePages.ForPath(context.Request.Path);
                html = HtmlMetadata.Apply(
                    template, meta.Title, meta.Description,
                    pageUrl: baseUrl + context.Request.Path.ToUriComponent(),
                    imageUrl: baseUrl + BotPreviewMiddleware.GenericOgImagePath);
            }
            catch
            {
                await next(context);
                return;
            }

            context.Response.StatusCode = StatusCodes.Status200OK;
            context.Response.ContentType = "text/html; charset=utf-8";
            // The page references hashed asset names; a cached copy would outlive the next deploy.
            context.Response.Headers["Cache-Control"] = "no-store";

            if (HttpMethods.IsHead(context.Request.Method))
            {
                context.Response.ContentLength = System.Text.Encoding.UTF8.GetByteCount(html);
                return;
            }

            await context.Response.WriteAsync(html);
        }

        internal static bool IsPageRequest(HttpRequest request)
        {
            if (!HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method)) return false;

            var path = request.Path;
            if (path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase)
                || path.StartsWithSegments("/swagger", StringComparison.OrdinalIgnoreCase)
                || path.StartsWithSegments("/signin-google", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            // Files (index.html, assets/*.js, robots.txt, sitemap.xml, ...) go to their own handlers.
            return !Path.HasExtension(path.Value ?? "");
        }
    }
}
