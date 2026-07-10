namespace JackyAIApp.Server.Services.Chatbot
{
    /// <summary>Outcome of a streamed chat turn, for the controller's credit refund decision.</summary>
    /// <param name="StreamingStarted">
    /// True once at least one content delta was written to the response — the point past which a
    /// failure should not be refunded (the model already did billable work).
    /// </param>
    /// <param name="ConversationId">The resolved (existing or newly created) conversation id.</param>
    /// <param name="ErrorMessage">Set when the turn failed; null on success.</param>
    public record ChatStreamResult(bool StreamingStarted, string ConversationId, string? ErrorMessage);

    public interface IAnthropicChatService
    {
        /// <summary>
        /// Streams a chat turn to <paramref name="response"/> as newline-delimited SSE JSON events
        /// (<c>delta</c>, <c>tool_use</c>, <c>done</c>, <c>error</c>), persisting the exchange to
        /// the caller's conversation history.
        /// </summary>
        Task<ChatStreamResult> StreamChatAsync(
            string userId, string message, string? conversationId, HttpResponse response, CancellationToken ct);
    }
}
