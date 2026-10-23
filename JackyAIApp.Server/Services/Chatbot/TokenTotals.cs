namespace JackyAIApp.Server.Services.Chatbot
{
    /// <summary>
    /// One request's token usage as the stream reports it, the latest report winning: message_start
    /// gives the request's usage so far, the final message_delta its cumulative usage, which also
    /// counts the MCP connector's server-side tool calls.
    /// </summary>
    internal sealed class RequestUsage
    {
        public long Input, CacheWrite, CacheRead, Output;

        public void Update(long? input, long? cacheWrite, long? cacheRead, long? output)
        {
            if (input.HasValue) Input = input.Value;
            if (cacheWrite.HasValue) CacheWrite = cacheWrite.Value;
            if (cacheRead.HasValue) CacheRead = cacheRead.Value;
            if (output.HasValue) Output = output.Value;
        }
    }

    /// <summary>
    /// A turn's tokens over all its requests (a pause_turn retry or a re-upload is a request of its
    /// own). <see cref="Input"/> is all of it: uncached, written to the cache and read from it.
    /// </summary>
    internal readonly record struct TokenTotals(long Input, long Output, long CacheWrite, long CacheRead)
    {
        public TokenTotals Plus(RequestUsage usage) => new(
            Input + usage.Input + usage.CacheWrite + usage.CacheRead,
            Output + usage.Output,
            CacheWrite + usage.CacheWrite,
            CacheRead + usage.CacheRead);
    }
}
