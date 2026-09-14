namespace JackyAIApp.Server.Common
{
    /// <summary>
    /// User-Agent markers for known non-JS-executing link-preview crawlers (LINE/Facebook/etc.),
    /// used by <see cref="BotPreviewMiddleware"/> to decide when to serve the server-rendered
    /// preview shell instead of the SPA. Each platform's UA is officially documented and rarely
    /// changes — unlike adversarial bot detection, this needs no ongoing tuning. Add a platform
    /// here (with a comment linking its UA documentation) rather than anywhere else in the
    /// pipeline; see .agent/architecture/ARCHITECTURE.md ("Phase 2").
    /// </summary>
    internal static class LinkPreviewBotUserAgents
    {
        internal static readonly string[] Markers =
        [
            "facebookexternalhit", // https://developers.facebook.com/docs/sharing/webmasters/crawler
            "Facebot",             // https://developers.facebook.com/docs/sharing/webmasters/crawler
            "Line/",               // https://developers.line.biz/en/docs/messaging-api/ (LINE's link-preview fetcher UA)
            "Twitterbot",          // https://developer.x.com/en/docs/x-for-websites/cards/guides/troubleshooting-cards
            "Slackbot",            // https://api.slack.com/robots
            "Discordbot",          // https://discord.com/developers/docs/reference (Discordbot link-preview UA)
            "WhatsApp",            // https://developers.facebook.com/docs/whatsapp (WhatsApp link-preview UA)
        ];

        internal static bool IsMatch(string? userAgent)
        {
            if (string.IsNullOrEmpty(userAgent))
            {
                return false;
            }

            foreach (var marker in Markers)
            {
                if (userAgent.Contains(marker, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
