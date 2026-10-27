using System.Globalization;
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
        /// What makes the server reach an MCP server a reader named (probe, add, test a connector), per reader: each is a
        /// press in the add dialog or on a connector, so only a loop comes near the limit, which keeps the server from
        /// being anyone's free client of other hosts (D-155).
        /// </summary>
        public const string ConnectorProbe = "connector-probe";

        internal const int ConnectorProbePermitsPerMinute = 20;

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

        /// <summary>Each reader their own window (the endpoints take a sign-in, so there is always one).</summary>
        internal static RateLimitPartition<string> ConnectorProbePartition(HttpContext http) =>
            RateLimitPartition.GetFixedWindowLimiter(
                "user:" + (http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? AnonymousPartition),
                _ => new FixedWindowRateLimiterOptions { PermitLimit = ConnectorProbePermitsPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 });

        internal static string ClientAddress(HttpContext http)
        {
            var forwarded = http.Request.Headers[CloudflareClientIpHeader].ToString().Trim();
            return forwarded.Length > 0 ? forwarded : http.Connection.RemoteIpAddress?.ToString() ?? AnonymousPartition;
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
                options.AddPolicy(ConnectorProbe, ConnectorProbePartition);
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
