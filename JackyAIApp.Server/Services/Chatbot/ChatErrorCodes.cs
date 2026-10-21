using Anthropic.Exceptions;

namespace JackyAIApp.Server.Services.Chatbot
{
    /// <summary>
    /// Why a chat turn failed, as sent to the browser in the SSE <c>error</c> event. Only the code
    /// goes out: the wording lives in the client (<c>chatErrors.ts</c>), and what actually happened
    /// — the Anthropic error, its message, the request — is in the server log.
    /// </summary>
    public static class ChatErrorCodes
    {
        /// <summary>The conversation no longer fits the model's context window: start a new one.</summary>
        public const string ContextTooLong = "context_too_long";

        /// <summary>The request was over the API's size limit (an oversized message or attachment).</summary>
        public const string RequestTooLarge = "request_too_large";

        /// <summary>Rate-limited, overloaded, a 5xx or a network failure: worth trying again later.</summary>
        public const string Busy = "busy";

        /// <summary>Nothing the user can do: no key, bad key, no credit, or a rejected model or beta.</summary>
        public const string Unavailable = "unavailable";

        /// <summary>The model's safety classifiers declined the request.</summary>
        public const string Refused = "refused";

        /// <summary>The turn finished without any answer text.</summary>
        public const string NoAnswer = "no_answer";

        /// <summary>Anything else.</summary>
        public const string Failed = "failed";

        /// <summary>The code for an exception thrown while calling the Anthropic API.</summary>
        public static string For(Exception ex) => ex switch
        {
            AnthropicRateLimitException or Anthropic5xxException or AnthropicIOException => Busy,
            AnthropicSseException sse when Mentions(sse.Message, "overloaded") => Busy,
            AnthropicUnauthorizedException or AnthropicForbiddenException or AnthropicNotFoundException => Unavailable,
            // The API's own wording ("prompt is too long", "credit balance ...") is in the response
            // body; whether the SDK also copies it into Message is not something to rely on.
            AnthropicApiException api => ForStatus((int)api.StatusCode, $"{api.Message} {api.ResponseBody}"),
            HttpRequestException or TaskCanceledException => Busy,
            _ => Failed,
        };

        /// <summary>An API error the type alone does not settle, by its HTTP status and message.</summary>
        internal static string ForStatus(int status, string? message) => status switch
        {
            400 when Mentions(message, "prompt is too long") => ContextTooLong,
            400 when Mentions(message, "credit balance") => Unavailable,
            402 => Unavailable,
            413 => RequestTooLarge,
            529 => Busy,
            _ => Failed,
        };

        /// <summary>
        /// The request referred to a file the Files API no longer has (deleted there, or the key now
        /// belongs to another workspace) — something the caller can put right by uploading it again.
        /// </summary>
        public static bool IsMissingFile(Exception ex) =>
            ex is AnthropicApiException api && IsMissingFile((int)api.StatusCode, $"{api.Message} {api.ResponseBody}");

        internal static bool IsMissingFile(int status, string? message) =>
            status is 400 or 404 && Mentions(message, "file") && Mentions(message, "not found");

        private static bool Mentions(string? text, string phrase) =>
            text?.Contains(phrase, StringComparison.OrdinalIgnoreCase) == true;
    }
}
