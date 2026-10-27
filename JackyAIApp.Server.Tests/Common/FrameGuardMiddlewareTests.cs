using JackyAIApp.Server.Common;
using Microsoft.AspNetCore.Http;

namespace JackyAIApp.Server.Tests.Common
{
    /// <summary>The OAuth consent page refuses to be framed by another site; other pages are left as they are (D-190).</summary>
    public class FrameGuardMiddlewareTests
    {
        private static async Task<HttpContext> RunAsync(string path)
        {
            var context = new DefaultHttpContext();
            context.Request.Path = path;
            var called = false;
            await new FrameGuardMiddleware(_ =>
            {
                called = true;
                return Task.CompletedTask;
            }).InvokeAsync(context);
            Assert.True(called);
            return context;
        }

        [Theory]
        [InlineData("/oauth/authorize")]
        [InlineData("/OAuth/Authorize")]
        public async Task TheConsentPage_MayNotBeFramed(string path)
        {
            var context = await RunAsync(path);

            Assert.Equal("DENY", context.Response.Headers.XFrameOptions.ToString());
            Assert.Equal("frame-ancestors 'none'", context.Response.Headers.ContentSecurityPolicy.ToString());
        }

        [Theory]
        [InlineData("/")]
        [InlineData("/dashboards/view/abc")]
        [InlineData("/oauth/authorized-elsewhere")]
        [InlineData("/api/oauth/consent")]
        public async Task OtherPages_AreLeftAlone(string path)
        {
            var context = await RunAsync(path);

            Assert.False(context.Response.Headers.ContainsKey("X-Frame-Options"));
            Assert.False(context.Response.Headers.ContainsKey("Content-Security-Policy"));
        }
    }
}
