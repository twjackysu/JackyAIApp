namespace JackyAIApp.Server.Services.AiUsage
{
    /// <summary>
    /// Fire-and-forget recorder of billable AI API calls into the AiUsageLog table. Every method
    /// swallows its own errors — usage logging must never break or slow down the actual feature.
    /// </summary>
    public interface IAiUsageLogger
    {
        Task LogChatAsync(string feature, string model, int promptTokens, int completionTokens,
            bool success, string? userId);

        Task LogTtsAsync(string model, int characterCount, bool success, string? userId);

        Task LogWhisperAsync(string model, bool success, string? userId);
    }
}
