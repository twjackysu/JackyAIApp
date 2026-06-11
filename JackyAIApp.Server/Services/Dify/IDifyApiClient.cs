namespace JackyAIApp.Server.Services.Dify
{
    public interface IDifyApiClient
    {
        /// <summary>
        /// Sends a chat message to Dify and returns the streaming response (caller must dispose).
        /// Uses ResponseHeadersRead so the body is streamed, not buffered.
        /// </summary>
        Task<HttpResponseMessage> SendChatStreamAsync(string message, string? conversationId, string userId, CancellationToken ct = default);

        Task<(bool ok, string body)> GetConversationsAsync(string userId, CancellationToken ct = default);

        Task<(bool ok, string body)> GetMessagesAsync(string conversationId, string userId, CancellationToken ct = default);

        Task<bool> DeleteConversationAsync(string conversationId, string userId, CancellationToken ct = default);
    }
}
