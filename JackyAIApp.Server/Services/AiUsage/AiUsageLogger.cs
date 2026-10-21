using JackyAIApp.Server.Common;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.Data.Models.SQL;

namespace JackyAIApp.Server.Services.AiUsage
{
    /// <summary>
    /// Writes one AiUsageLog row per billable AI call. Inserts inline (one fast INSERT after a
    /// multi-second AI call is negligible) but wrapped so any failure is logged and swallowed —
    /// usage accounting must never surface as a user-facing error.
    /// </summary>
    public class AiUsageLogger(AzureSQLDBContext db, UsageLedger ledger, ILogger<AiUsageLogger> logger) : IAiUsageLogger
    {
        public Task LogChatAsync(string feature, string model, int promptTokens, int completionTokens,
            bool success, string? userId, int? cacheWriteTokens = null, int? cacheReadTokens = null) =>
            SafeInsert(new AiUsageLog
            {
                Feature = feature,
                Operation = "chat",
                Model = model,
                PromptTokens = promptTokens,
                CompletionTokens = completionTokens,
                TotalTokens = promptTokens + completionTokens,
                CacheWriteTokens = cacheWriteTokens,
                CacheReadTokens = cacheReadTokens,
                EstimatedCostUsd = AiRates.ChatCost(model, promptTokens, completionTokens,
                    cacheWriteTokens ?? 0, cacheReadTokens ?? 0),
                Success = success,
                UserId = userId,
            }, billed: promptTokens + completionTokens > 0);

        public Task LogTtsAsync(string model, int characterCount, bool success, string? userId) =>
            SafeInsert(new AiUsageLog
            {
                Feature = "tts",
                Operation = "tts",
                Model = model,
                CharacterCount = characterCount,
                EstimatedCostUsd = AiRates.TtsCost(model, characterCount),
                Success = success,
                UserId = userId,
            }, billed: success);

        public Task LogWhisperAsync(string model, bool success, string? userId) =>
            SafeInsert(new AiUsageLog
            {
                Feature = "whisper",
                Operation = "whisper",
                Model = model,
                // Whisper is billed per minute of audio; we don't have the duration at this layer,
                // so cost is left null and only the call count is recorded.
                Success = success,
                UserId = userId,
            }, billed: success);

        public Task LogImageGenerationAsync(string feature, string model, int imageCount, bool success, string? userId) =>
            SafeInsert(new AiUsageLog
            {
                Feature = feature,
                Operation = "image",
                Model = model,
                ImageCount = imageCount,
                EstimatedCostUsd = AiRates.ImageCost(model, imageCount),
                Success = success,
                UserId = userId,
            }, billed: success);

        /// <param name="entry">The row to write.</param>
        /// <param name="billed">Whether the provider charged for this call — a chat call that consumed
        /// any tokens (even one that failed or came back unusable), or a TTS / transcription / image call
        /// that succeeded. Reported to the request's <see cref="UsageLedger"/>, which keeps the user's
        /// usage charge from being refunded once the author has paid for the call.</param>
        private async Task SafeInsert(AiUsageLog entry, bool billed)
        {
            if (billed) ledger.MarkAiBilled();

            try
            {
                db.AiUsageLogs.Add(entry);
                await db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to write AiUsageLog row for feature {Feature}", entry.Feature);
            }
        }
    }
}
