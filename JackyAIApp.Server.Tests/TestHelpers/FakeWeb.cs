using System.Net;
using System.Text;
using JackyAIApp.Server.Services.Connectors;

namespace JackyAIApp.Server.Tests.TestHelpers
{
    /// <summary>
    /// A few outside URLs, each with its answer (404 for any other), as the handler of the connectors'
    /// outbound requests (<see cref="IMcpHttpHandlerSource"/>, which is <see cref="SafeHttp"/> in the app).
    /// Records what was asked and every request body. For one endpoint's answer; a whole MCP server or a
    /// whole OAuth sign-in is <see cref="FakeMcpServer"/> / <see cref="FakeAuthorizationServer"/>.
    /// </summary>
    public sealed class FakeWeb : HttpMessageHandler, IMcpHttpHandlerSource
    {
        private readonly Dictionary<string, Func<HttpRequestMessage, HttpResponseMessage>> _routes = new();
        public List<string> Asked { get; } = [];
        public List<(HttpRequestMessage Request, string Body)> Posts { get; } = [];

        public FakeWeb Json(string url, string json, HttpStatusCode status = HttpStatusCode.OK)
        {
            _routes[url] = _ => new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
            return this;
        }

        public FakeWeb Answer(string url, Func<HttpRequestMessage, HttpResponseMessage> answer)
        {
            _routes[url] = answer;
            return this;
        }

        public HttpMessageHandler Create() => new KeptAlive(this);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var url = request.RequestUri!.ToString();
            Asked.Add(url);
            if (request.Content != null) Posts.Add((request, await request.Content.ReadAsStringAsync(ct)));
            return _routes.TryGetValue(url, out var answer) ? answer(request) : new HttpResponseMessage(HttpStatusCode.NotFound);
        }
    }

    /// <summary>
    /// The client disposes the handler it was given; a fake handed out through it lives on for the test
    /// to read what it was asked.
    /// </summary>
    public sealed class KeptAlive(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        protected override void Dispose(bool disposing) { }
    }
}
