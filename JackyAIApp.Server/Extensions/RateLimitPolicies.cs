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
            });

        /// <summary>The same envelope <see cref="IMyResponseFactory"/> gives every other API error.</summary>
        internal static async ValueTask WriteRejectionAsync(OnRejectedContext context, CancellationToken ct)
        {
            var http = context.HttpContext;
            if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                http.Response.Headers.RetryAfter =
                    ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);

            var policy = http.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName;
            var message = policy == SessionUpload ? SessionUploadOptions.BusyMessage : "請求太頻繁，請稍後再試。";

            var result = http.RequestServices.GetRequiredService<IMyResponseFactory>()
                .CreateErrorResponse(ErrorCodes.TooManyRequests, message);
            await result.ExecuteResultAsync(new ActionContext(http, http.GetRouteData(), new ActionDescriptor()));
        }
    }
}
