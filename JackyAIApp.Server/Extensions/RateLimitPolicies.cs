using System.Globalization;
using System.Net;
using System.Net.Sockets;
using JackyAIApp.Server.Common;
using System.Security.Claims;
using System.Threading.RateLimiting;
using JackyAIApp.Server.Services.Connectors.SiteMcp;
using JackyAIApp.Server.Services.DataAnalysis;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.RateLimiting;

namespace JackyAIApp.Server.Extensions
{
    /// <summary>
    /// ASP.NET Core rate-limiter policies, applied per action with <c>[EnableRateLimiting(name)]</c>.
    /// Rejections get the app's usual error envelope (429 + <see cref="ErrorCodes.TooManyRequests"/>).
    /// </summary>
    public static class RateLimitPolicies
    {
        /// <summary>The anonymous session upload (POST /api/dataanalysis/session).</summary>
        public const string SessionUpload = "dataanalysis-session-upload";

        /// <summary>The site's own MCP server (POST /api/mcp), per token: another AI's client may loop.</summary>
        public const string SiteMcp = "site-mcp";

        internal const int SiteMcpPermitsPerMinute = 120;

        /// <summary>
        /// The OAuth endpoints another AI's client calls without signing in (POST /api/oauth/register, /api/oauth/token),
        /// per address: a sign-in takes two calls, so only a loop or a flood comes near the limit.
        /// </summary>
        public const string SiteOAuth = "site-oauth";

        internal const int SiteOAuthPermitsPerMinute = 30;

        /// <summary>
        /// The reader's answer on the consent page (POST /api/oauth/consent), per reader: an approval holds a code in
        /// memory for five minutes, so a loop would fill it (D-190). One sign-in is one press.
        /// </summary>
        public const string SiteOAuthConsent = "site-oauth-consent";

        internal const int SiteOAuthConsentPermitsPerMinute = 20;

        /// <summary>
        /// What makes the server reach an MCP server a reader named (probe, add, test a connector), per reader: each is a
        /// press in the add dialog or on a connector, so only a loop comes near the limit, which keeps the server from
        /// being anyone's free client of other hosts (D-155). Saving a Data Analysis source (a SQL Server or cluster the
        /// server will connect to) is held to it too (D-164).
        /// </summary>
        public const string ConnectorProbe = "connector-probe";

        internal const int ConnectorProbePermitsPerMinute = 20;

        /// <summary>
        /// A chat attachment's upload, per reader: free in itself, it holds the file in memory a few times over while
        /// it is read and puts images and PDFs in Anthropic's Files API, and a message takes at most five (D-158).
        /// </summary>
        public const string ChatAttachmentUpload = "chat-attachment-upload";

        internal const int ChatAttachmentUploadPermitsPerMinute = 20;

        /// <summary>
        /// Searching one's own conversations by their messages, per reader (D-191): free, and each search scans the
        /// reader's stored messages; the history column asks once typing pauses, so a reader looking for one stays within.
        /// </summary>
        public const string ChatSearch = "chat-search";

        internal const int ChatSearchPermitsPerMinute = 20;

        /// <summary>
        /// Naming a conversation from its first answer (POST conversations/{id}/title), per reader: uncharged, as part of the
        /// first message's cost, and each conversation is asked once; the client asks once after a first answer, so only a
        /// loop comes near the limit.
        /// </summary>
        public const string ChatTitle = "chat-title";

        internal const int ChatTitlePermitsPerMinute = 20;

        /// <summary>
        /// A connector's MCP App, per reader (design/09, D-202): reading its HTML and the calls it asks of its server each reach
        /// the reader's MCP server, with nothing cached; a page of a few app cards and an app refreshing now and then stay within.
        /// </summary>
        public const string McpApp = "mcp-app";

        internal const int McpAppPermitsPerMinute = 60;

        /// <summary>
        /// The calls a connector's MCP App asks of its server, per reader (D-207): apart from reading apps, so an app that polls
        /// (the official system-monitor one asks every 2 seconds, 30 a minute) leaves the other cards' apps loading; a few such
        /// apps at once stay within.
        /// </summary>
        public const string McpAppCall = "mcp-app-call";

        internal const int McpAppCallPermitsPerMinute = 120;

        /// <summary>
        /// The PDF unlocker, per address: it takes no sign-in and reads and rewrites a whole document in memory, so a
        /// few requests at once from one place could take the server's memory (D-158).
        /// </summary>
        public const string PdfUnlock = "pdf-unlock";

        internal const int PdfUnlockPermitsPerMinute = 10;

        /// <summary>
        /// The job search (GET /api/job/listings), per address: it takes no sign-in, and each new search is a round of calls
        /// to 104 and Cake (five pages) from the App Service's one address, which those sites may block for everyone if a
        /// loop asks too often, and a cache entry for 30 minutes. A person searches a few times a minute at most (D-165).
        /// </summary>
        public const string JobSearch = "job-search";

        internal const int JobSearchPermitsPerMinute = 20;

        /// <summary>
        /// A dashboard widget's rows (GET /api/dashboard/{id}/widgets/{widgetId}/data), per reader, or per address for a
        /// visitor of an "Anyone" dashboard: each call runs the owner's saved query again on the owner's source (their own
        /// database for a custom one), with no sign-in and no usage charge for that visitor, so a loop on a shared link
        /// could keep the owner's database busy. A page asks once per widget when it opens, so readers stay far inside (D-163).
        /// </summary>
        public const string DashboardWidgetData = "dashboard-widget-data";

        internal const int DashboardWidgetDataPermitsPerMinuteAnonymous = 120;
        internal const int DashboardWidgetDataPermitsPerMinuteSignedIn = 600;

        /// <summary>
        /// The Data Analysis page's queries (schema, latest date, distinct values, summarize, details, export), per reader, or per
        /// address without a sign-in: uncharged, each runs a query on the site's database or on the reader's own source, which
        /// for a custom one is a connection from the App Service to a host its owner named, so a loop could keep a database busy
        /// or use the server as its relay. A page asks a handful when it opens and one or two per change, far inside.
        /// </summary>
        public const string DataQuery = "data-query";

        internal const int DataQueryPermitsPerMinuteAnonymous = 120;
        internal const int DataQueryPermitsPerMinuteSignedIn = 300;

        /// <summary>
        /// Text to speech (GET /api/audio/normal, /slow), per reader, or per address without a sign-in: a visitor's clip is
        /// free (only a signed-in reader's new clip is charged), and every new text is a call to the speech service, whose free
        /// tier is a monthly character allowance shared by everyone. A dictionary page plays a clip on each press, so a person
        /// stays far inside (D-173).
        /// </summary>
        public const string TextToSpeech = "text-to-speech";

        internal const int TextToSpeechPermitsPerMinuteAnonymous = 30;
        internal const int TextToSpeechPermitsPerMinuteSignedIn = 120;

        /// <summary>
        /// The visitor's address as Cloudflare saw it. The site only takes requests from Cloudflare's ranges (the App
        /// Service's access restrictions, root CLAUDE.md "Deployment"), so the header is Cloudflare's own; without it (a
        /// developer's machine) the connection's address.
        /// </summary>
        internal const string CloudflareClientIpHeader = "CF-Connecting-IP";

        internal const string AnonymousPartition = "anonymous";
        internal const int SessionUploadPermitsPerUser = 5;
        internal const int SessionUploadPermitsAnonymous = 20;
        internal static readonly TimeSpan SessionUploadWindow = TimeSpan.FromMinutes(10);

        /// <summary>
        /// A signed-in caller gets their own window; every anonymous caller shares one, larger window —
        /// they are one category, whatever address they come from — so a flood without an account can't
        /// outgrow it by spreading across addresses.
        /// </summary>
        internal static RateLimitPartition<string> SessionUploadPartition(HttpContext http)
        {
            var userId = http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return userId is null
                ? RateLimitPartition.GetFixedWindowLimiter(AnonymousPartition, _ => Window(SessionUploadPermitsAnonymous))
                : RateLimitPartition.GetFixedWindowLimiter("user:" + userId, _ => Window(SessionUploadPermitsPerUser));
        }

        /// <summary>Each token its own window (the endpoint signs in by token only, so there is always one).</summary>
        internal static RateLimitPartition<string> SiteMcpPartition(HttpContext http) =>
            RateLimitPartition.GetFixedWindowLimiter(
                "token:" + (http.User.FindFirst(SiteTokenAuthenticationHandler.TokenIdClaim)?.Value ?? AnonymousPartition),
                _ => new FixedWindowRateLimiterOptions { PermitLimit = SiteMcpPermitsPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 });

        internal static RateLimitPartition<string> SiteOAuthPartition(HttpContext http) =>
            RateLimitPartition.GetFixedWindowLimiter(
                "address:" + ClientAddress(http),
                _ => new FixedWindowRateLimiterOptions { PermitLimit = SiteOAuthPermitsPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 });

        /// <summary>Each reader their own window (the endpoint takes a sign-in, so there is always one).</summary>
        internal static RateLimitPartition<string> SiteOAuthConsentPartition(HttpContext http) =>
            RateLimitPartition.GetFixedWindowLimiter(
                "user:" + (http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? AnonymousPartition),
                _ => new FixedWindowRateLimiterOptions { PermitLimit = SiteOAuthConsentPermitsPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 });

        /// <summary>Each reader their own window (the endpoints take a sign-in, so there is always one).</summary>
        internal static RateLimitPartition<string> ConnectorProbePartition(HttpContext http) =>
            RateLimitPartition.GetFixedWindowLimiter(
                "user:" + (http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? AnonymousPartition),
                _ => new FixedWindowRateLimiterOptions { PermitLimit = ConnectorProbePermitsPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 });

        /// <summary>Each reader their own window (the endpoint takes a sign-in, so there is always one).</summary>
        internal static RateLimitPartition<string> ChatAttachmentUploadPartition(HttpContext http) =>
            RateLimitPartition.GetFixedWindowLimiter(
                "user:" + (http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? AnonymousPartition),
                _ => new FixedWindowRateLimiterOptions { PermitLimit = ChatAttachmentUploadPermitsPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 });

        /// <summary>Each reader their own window (the endpoint takes a sign-in, so there is always one).</summary>
        internal static RateLimitPartition<string> ChatSearchPartition(HttpContext http) =>
            RateLimitPartition.GetFixedWindowLimiter(
                "user:" + (http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? AnonymousPartition),
                _ => new FixedWindowRateLimiterOptions { PermitLimit = ChatSearchPermitsPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 });

        /// <summary>Each reader their own window (the endpoint takes a sign-in, so there is always one).</summary>
        internal static RateLimitPartition<string> ChatTitlePartition(HttpContext http) =>
            RateLimitPartition.GetFixedWindowLimiter(
                "user:" + (http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? AnonymousPartition),
                _ => new FixedWindowRateLimiterOptions { PermitLimit = ChatTitlePermitsPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 });

        /// <summary>Each reader their own window (the endpoints take a sign-in).</summary>
        internal static RateLimitPartition<string> McpAppPartition(HttpContext http) =>
            RateLimitPartition.GetFixedWindowLimiter(
                "user:" + (http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? AnonymousPartition),
                _ => new FixedWindowRateLimiterOptions { PermitLimit = McpAppPermitsPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 });

        /// <summary>Each reader their own window (the endpoint takes a sign-in).</summary>
        internal static RateLimitPartition<string> McpAppCallPartition(HttpContext http) =>
            RateLimitPartition.GetFixedWindowLimiter(
                "user:" + (http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? AnonymousPartition),
                _ => new FixedWindowRateLimiterOptions { PermitLimit = McpAppCallPermitsPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 });

        /// <summary>Each visitor's address its own window: the unlocker takes no sign-in.</summary>
        internal static RateLimitPartition<string> PdfUnlockPartition(HttpContext http) =>
            RateLimitPartition.GetFixedWindowLimiter(
                "address:" + ClientAddress(http),
                _ => new FixedWindowRateLimiterOptions { PermitLimit = PdfUnlockPermitsPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 });

        /// <summary>Each visitor's address its own window: the search takes no sign-in.</summary>
        internal static RateLimitPartition<string> JobSearchPartition(HttpContext http) =>
            RateLimitPartition.GetFixedWindowLimiter(
                "address:" + ClientAddress(http),
                _ => new FixedWindowRateLimiterOptions { PermitLimit = JobSearchPermitsPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 });

        /// <summary>A signed-in reader their own window; a visitor without a sign-in, their address's.</summary>
        internal static RateLimitPartition<string> DashboardWidgetDataPartition(HttpContext http)
        {
            var userId = http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return userId is null
                ? RateLimitPartition.GetFixedWindowLimiter("address:" + ClientAddress(http), _ => new FixedWindowRateLimiterOptions
                    { PermitLimit = DashboardWidgetDataPermitsPerMinuteAnonymous, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 })
                : RateLimitPartition.GetFixedWindowLimiter("user:" + userId, _ => new FixedWindowRateLimiterOptions
                    { PermitLimit = DashboardWidgetDataPermitsPerMinuteSignedIn, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 });
        }

        /// <summary>A signed-in reader their own window; a visitor without a sign-in, their address's.</summary>
        internal static RateLimitPartition<string> DataQueryPartition(HttpContext http)
        {
            var userId = http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return userId is null
                ? RateLimitPartition.GetFixedWindowLimiter("address:" + ClientAddress(http), _ => new FixedWindowRateLimiterOptions
                    { PermitLimit = DataQueryPermitsPerMinuteAnonymous, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 })
                : RateLimitPartition.GetFixedWindowLimiter("user:" + userId, _ => new FixedWindowRateLimiterOptions
                    { PermitLimit = DataQueryPermitsPerMinuteSignedIn, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 });
        }

        /// <summary>A signed-in reader their own window; a visitor without a sign-in, their address's.</summary>
        internal static RateLimitPartition<string> TextToSpeechPartition(HttpContext http)
        {
            var userId = http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return userId is null
                ? RateLimitPartition.GetFixedWindowLimiter("address:" + ClientAddress(http), _ => new FixedWindowRateLimiterOptions
                    { PermitLimit = TextToSpeechPermitsPerMinuteAnonymous, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 })
                : RateLimitPartition.GetFixedWindowLimiter("user:" + userId, _ => new FixedWindowRateLimiterOptions
                    { PermitLimit = TextToSpeechPermitsPerMinuteSignedIn, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 });
        }

        internal static string ClientAddress(HttpContext http)
        {
            var forwarded = http.Request.Headers[CloudflareClientIpHeader].ToString().Trim();
            if (forwarded.Length > 0)
                return IPAddress.TryParse(forwarded, out var parsed) ? Subscriber(parsed) : forwarded;
            return http.Connection.RemoteIpAddress is { } remote ? Subscriber(remote) : AnonymousPartition;
        }

        /// <summary>
        /// The address one subscriber's limits count against (D-196): an IPv4 address as it is, an IPv6 one by its /64,
        /// which an ISP hands one household or phone whole, so it cannot step through 2^64 addresses to multiply its share.
        /// </summary>
        internal static string Subscriber(IPAddress address)
        {
            if (address.IsIPv4MappedToIPv6)
                return address.MapToIPv4().ToString();
            if (address.AddressFamily != AddressFamily.InterNetworkV6)
                return address.ToString();
            var bytes = address.GetAddressBytes();
            Array.Clear(bytes, 8, 8);
            return new IPAddress(bytes) + "/64";
        }

        private static FixedWindowRateLimiterOptions Window(int permits) => new()
        {
            PermitLimit = permits,
            Window = SessionUploadWindow,
            QueueLimit = 0,
        };

        public static IServiceCollection AddAppRateLimiting(this IServiceCollection services) =>
            services.AddRateLimiter(options =>
            {
                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                options.OnRejected = WriteRejectionAsync;
                options.AddPolicy(SessionUpload, SessionUploadPartition);
                options.AddPolicy(SiteMcp, SiteMcpPartition);
                options.AddPolicy(SiteOAuth, SiteOAuthPartition);
                options.AddPolicy(SiteOAuthConsent, SiteOAuthConsentPartition);
                options.AddPolicy(ConnectorProbe, ConnectorProbePartition);
                options.AddPolicy(ChatAttachmentUpload, ChatAttachmentUploadPartition);
                options.AddPolicy(ChatSearch, ChatSearchPartition);
                options.AddPolicy(ChatTitle, ChatTitlePartition);
                options.AddPolicy(McpApp, McpAppPartition);
                options.AddPolicy(McpAppCall, McpAppCallPartition);
                options.AddPolicy(PdfUnlock, PdfUnlockPartition);
                options.AddPolicy(DashboardWidgetData, DashboardWidgetDataPartition);
                options.AddPolicy(DataQuery, DataQueryPartition);
                options.AddPolicy(JobSearch, JobSearchPartition);
                options.AddPolicy(TextToSpeech, TextToSpeechPartition);
            });

        /// <summary>The same envelope <see cref="IMyResponseFactory"/> gives every other API error.</summary>
        internal static async ValueTask WriteRejectionAsync(OnRejectedContext context, CancellationToken ct)
        {
            var http = context.HttpContext;
            if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                http.Response.Headers.RetryAfter =
                    ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);

            var policy = http.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName;
            if (policy == SiteOAuth)
            {
                // An OAuth client reads OAuth's own error shape, not the app's envelope.
                var reply = SiteOAuthReply.Error("temporarily_unavailable", "Too many requests; try again shortly.",
                    StatusCodes.Status429TooManyRequests);
                http.Response.StatusCode = reply.StatusCode;
                http.Response.ContentType = "application/json";
                await http.Response.WriteAsync(reply.Body.ToJsonString(), ct);
                return;
            }
            var message = policy == SessionUpload ? SessionUploadOptions.BusyMessage : "請求太頻繁，請稍後再試。";

            var result = http.RequestServices.GetRequiredService<IMyResponseFactory>()
                .CreateErrorResponse(ErrorCodes.TooManyRequests, message);
            await result.ExecuteResultAsync(new ActionContext(http, http.GetRouteData(), new ActionDescriptor()));
        }
    }
}
