namespace JackyAIApp.Server.Common
{
    /// <summary>
    /// Pages that must never be shown inside another site's frame: the consent page of the site's own OAuth server
    /// (<c>/oauth/authorize</c>), where one press gives another AI a token to the reader's account. Framed under a decoy, that
    /// press could be the reader's without them knowing (clickjacking). Today the sign-in cookie (SameSite=Lax) is not sent
    /// to a cross-site frame, so the framed page would only ask to sign in; this keeps it so whatever the cookie becomes
    /// (D-190). Only these pages: nothing else on the site is known to be framed by nobody.
    /// </summary>
    public class FrameGuardMiddleware(RequestDelegate next)
    {
        internal static readonly PathString[] GuardedPaths = [new("/oauth/authorize")];

        public Task InvokeAsync(HttpContext context)
        {
            if (Array.Exists(GuardedPaths, p => context.Request.Path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase)))
            {
                context.Response.Headers.XFrameOptions = "DENY";
                context.Response.Headers.ContentSecurityPolicy = "frame-ancestors 'none'";
            }
            return next(context);
        }
    }
}
