using System.Net;
using System.Text;

namespace JackyAIApp.Server.Tests.TestHelpers
{
    /// <summary>
    /// Routes HttpClient calls to a canned responder instead of the network — used by offline
    /// parser/regression tests that assert a provider correctly parses a fixed response shape
    /// (as opposed to live contract tests, which hit the real third-party API).
    /// </summary>
    public sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));

        public static HttpResponseMessage JsonResponse(string json, HttpStatusCode status = HttpStatusCode.OK) =>
            new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

        /// <summary>Builds an <see cref="IHttpClientFactory"/> mock whose CreateClient() always returns
        /// an HttpClient wired to <paramref name="responder"/>. Import Moq in the caller.</summary>
        public static HttpClient CreateClient(Func<HttpRequestMessage, HttpResponseMessage> responder) =>
            new(new StubHttpMessageHandler(responder));
    }
}
