namespace JackyAIApp.Server.Services.AiUsage
{
    /// <summary>
    /// Fire-and-forget recorder of billable AI API calls into the AiUsageLog table. Every method
    /// swallows its own errors — usage logging must never break or slow down the actual feature.
    /// </summary>
    public interface IAiUsageLogger
    {
        /// <param name="promptTokens">All input tokens, cached or not.</param>
        /// <param name="cacheWriteTokens">Of those, written to the prompt cache; null when not caching.</param>
        /// <param name="cacheReadTokens">Of those, read from the prompt cache; null when not caching.</param>
        Task LogChatAsync(string feature, string model, int promptTokens, int completionTokens,
            bool success, string? userId, int? cacheWriteTokens = null, int? cacheReadTokens = null);

        Task LogTtsAsync(string model, int characterCount, bool success, string? userId);

        Task LogWhisperAsync(string model, bool success, string? userId);

        Task LogImageGenerationAsync(string feature, string model, int imageCount, bool success, string? userId);
    }
}
