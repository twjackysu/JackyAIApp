using System.Text.RegularExpressions;

namespace JackyAIApp.Server.Common
{
    /// <summary>
    /// Rewrites the page metadata in the SPA's index.html: &lt;title&gt;, meta description, and the
    /// Open Graph / Twitter tags link previews read (LINE and Teams use og:title/og:description/
    /// og:image; without them they fall back to the title and whatever icon they find).
    /// </summary>
    internal static class HtmlMetadata
    {
        /// <summary>Pure, so it is testable without an HttpContext. Every value is HTML-encoded, and
        /// existing tags are replaced rather than duplicated.</summary>
        internal static string Apply(string html, string title, string description, string pageUrl, string imageUrl)
        {
            var t = System.Net.WebUtility.HtmlEncode(title);
            var d = System.Net.WebUtility.HtmlEncode(description);

            // MatchEvaluator lambdas, not replacement strings — a replacement string would let a
            // literal "$" in the text (a price, say) be misread by Regex.Replace as a backreference
            // token ($1, $&, ...) and silently corrupt the output.
            html = Regex.Replace(html, "<title>[\\s\\S]*?</title>", _ => $"<title>{t}</title>", RegexOptions.IgnoreCase);
            html = Upsert(html, "name", "description", d);

            html = Upsert(html, "property", "og:type", "website");
            html = Upsert(html, "property", "og:site_name", System.Net.WebUtility.HtmlEncode(SitePages.SiteName));
            html = Upsert(html, "property", "og:title", t);
            html = Upsert(html, "property", "og:description", d);
            html = Upsert(html, "property", "og:url", System.Net.WebUtility.HtmlEncode(pageUrl));
            html = Upsert(html, "property", "og:image", System.Net.WebUtility.HtmlEncode(imageUrl));
            html = Upsert(html, "name", "twitter:card", "summary");
            return html;
        }

        /// <summary>Sets &lt;meta {attribute}="{key}" content="..."&gt;: replaces the tag if the
        /// template has it, otherwise adds it before &lt;/head&gt;.</summary>
        private static string Upsert(string html, string attribute, string key, string encodedContent)
        {
            var tag = $"<meta {attribute}=\"{key}\" content=\"{encodedContent}\" />";
            var pattern = $"<meta\\s[^>]*{attribute}=\"{Regex.Escape(key)}\"[^>]*/?>";
            return Regex.IsMatch(html, pattern, RegexOptions.IgnoreCase)
                ? Regex.Replace(html, pattern, _ => tag, RegexOptions.IgnoreCase)
                : Regex.Replace(html, "</head>", _ => $"{tag}\n</head>", RegexOptions.IgnoreCase);
        }
    }
}
