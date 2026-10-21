namespace JackyAIApp.Server.Services.Chatbot
{
    /// <summary>Outcome of a streamed chat turn, for the controller's usage refund decision.</summary>
    /// <param name="StreamingStarted">
    /// True once at least one content delta was written to the response — the point past which a
    /// failure should not be refunded (the model already did billable work).
    /// </param>
    /// <param name="ConversationId">The resolved (existing or newly created) conversation id.</param>
    /// <param name="ErrorCode">A <see cref="ChatErrorCodes"/> value when the turn failed (already sent
    /// to the client as an SSE <c>error</c> event); null on success or when the client went away.</param>
    public record ChatStreamResult(bool StreamingStarted, string ConversationId, string? ErrorCode);

    public interface IAnthropicChatService
    {
        /// <summary>
        /// Streams a chat turn to <paramref name="response"/> as newline-delimited SSE JSON events
        /// (<c>delta</c>, <c>tool_use</c>, <c>done</c> with <c>truncated</c>, <c>error</c> with a
        /// <see cref="ChatErrorCodes"/> <c>code</c>), persisting the exchange to the caller's
        /// conversation history. Every failure, including one before any text, is reported as an
        /// <c>error</c> event.
        /// </summary>
        Task<ChatStreamResult> StreamChatAsync(
            string userId, string message, string? conversationId, HttpResponse response, CancellationToken ct);
    }
}
