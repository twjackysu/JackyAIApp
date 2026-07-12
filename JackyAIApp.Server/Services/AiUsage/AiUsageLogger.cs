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
    public class AiUsageLogger(AzureSQLDBContext db, ILogger<AiUsageLogger> logger) : IAiUsageLogger
    {
        public Task LogChatAsync(string feature, string model, int promptTokens, int completionTokens,
            bool success, string? userId) =>
            SafeInsert(new AiUsageLog
            {
                Feature = feature,
                Operation = "chat",
                Model = model,
                PromptTokens = promptTokens,
                CompletionTokens = completionTokens,
                TotalTokens = promptTokens + completionTokens,
                EstimatedCostUsd = AiRates.ChatCost(model, promptTokens, completionTokens),
                Success = success,
                UserId = userId,
            });

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
            });

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
            });

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
            });

        private async Task SafeInsert(AiUsageLog entry)
        {
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
