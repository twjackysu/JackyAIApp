using System.Net;
using System.Text;
using JackyAIApp.Server.Services.Connectors;
using JackyAIApp.Server.Tests.TestHelpers;

namespace JackyAIApp.Server.Tests.Services.Connectors
{
    /// <summary>A reader's server cannot make the app hold more of one answer than <see cref="ResponseSizeLimit"/> lets through.</summary>
    public class ResponseSizeLimitTests
    {
        private static HttpClient Client(Func<HttpContent> body) => new(new ResponseSizeLimit(
            new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = body() }), maxBytes: 10));

        [Fact]
        public async Task AnAnswerWithinTheLimit_IsReadWhole_WithItsHeaders()
        {
            using var response = await Client(() => new StringContent("0123456789", Encoding.UTF8, "text/event-stream"))
                .GetAsync("https://mcp.example.com/mcp");

            Assert.Equal("0123456789", await response.Content.ReadAsStringAsync());
            Assert.Equal("text/event-stream", response.Content.Headers.ContentType!.MediaType);
        }

        [Fact]
        public async Task AnAnswerThatSaysItIsLonger_IsRefusedAtOnce()
        {
            await Assert.ThrowsAsync<ResponseTooLargeException>(() =>
                Client(() => new StringContent("0123456789A")).GetAsync("https://mcp.example.com/mcp", HttpCompletionOption.ResponseHeadersRead));
        }

        [Fact]
        public async Task AnAnswerThatDoesNotSay_IsCutOffOnceItGoesPastTheLimit()
        {
            // No Content-Length, as a stream that goes on has none.
            var client = Client(() =>
            {
                var content = new StreamContent(new MemoryStream(new byte[1000]));
                content.Headers.ContentLength = null;
                return content;
            });

            using var response = await client.GetAsync("https://mcp.example.com/mcp", HttpCompletionOption.ResponseHeadersRead);
            await using var stream = await response.Content.ReadAsStreamAsync();

            await Assert.ThrowsAsync<ResponseTooLargeException>(async () =>
            {
                var buffer = new byte[4];
                while (await stream.ReadAsync(buffer) > 0) { }
            });
        }
    }
}
