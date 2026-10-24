using Microsoft.AspNetCore.Http.Features;
using System.Text.Json;

namespace JackyAIApp.Server.Services.Chatbot
{
    /// <summary>
    /// The event stream a chat turn answers with: each event one JSON object on a <c>data:</c> line,
    /// flushed as soon as it is written so the reader sees the answer as it comes.
    /// </summary>
    internal sealed class SseStream
    {
        private readonly HttpResponse _response;

        private SseStream(HttpResponse response) => _response = response;

        /// <summary>Sets the response up as an unbuffered event stream.</summary>
        public static SseStream Open(HttpResponse response)
        {
            response.ContentType = "text/event-stream";
            response.Headers.Append("Cache-Control", "no-cache");
            response.Headers.Append("Connection", "keep-alive");
            response.Headers.Append("Access-Control-Allow-Origin", "*");
            response.HttpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();
            return new SseStream(response);
        }

        public async Task WriteAsync(object payload, CancellationToken ct)
        {
            var json = JsonSerializer.Serialize(payload);
            await _response.WriteAsync($"data: {json}\n\n", ct);
            await _response.Body.FlushAsync(ct);
        }

        /// <summary>
        /// A comment line, which readers skip: keeps a stream that is waiting (for the reader's decision)
        /// from being closed as idle on the way (Cloudflare drops one after 100s).
        /// </summary>
        public async Task WriteCommentAsync(string comment, CancellationToken ct)
        {
            await _response.WriteAsync($": {comment}\n\n", ct);
            await _response.Body.FlushAsync(ct);
        }
    }
}
