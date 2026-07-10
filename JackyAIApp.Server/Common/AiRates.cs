namespace JackyAIApp.Server.Common
{
    /// <summary>
    /// Approximate OpenAI pricing, centralized so cost estimates live in one place. These are
    /// rough public list rates as of 2026-07 and WILL drift — the raw token/character counts logged
    /// alongside every <see cref="Data.Models.SQL.AiUsageLog"/> row are authoritative; this only
    /// pre-computes a convenient SUM-able cost column for the usage dashboard. Verify against the
    /// OpenAI billing dashboard for real spend.
    /// </summary>
    public static class AiRates
    {
        // Chat model rates in USD per 1,000,000 tokens (input, output). Output includes reasoning
        // tokens on reasoning-family models.
        private static readonly Dictionary<string, (decimal InputPerM, decimal OutputPerM)> ChatRates =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["gpt-5-nano"] = (0.05m, 0.40m),
                ["gpt-4.1-nano"] = (0.10m, 0.40m),
                ["gpt-4o-mini"] = (0.15m, 0.60m),
            };

        // TTS rates in USD per 1,000,000 input characters.
        private static readonly Dictionary<string, decimal> TtsRatesPerMChars =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["tts-1"] = 15m,
                ["tts-1-hd"] = 30m,
            };

        /// <summary>USD cost of a chat completion, or null if the model's rate isn't known.</summary>
        public static decimal? ChatCost(string model, int promptTokens, int completionTokens)
        {
            if (!ChatRates.TryGetValue(model, out var r)) return null;
            return promptTokens / 1_000_000m * r.InputPerM
                 + completionTokens / 1_000_000m * r.OutputPerM;
        }

        /// <summary>USD cost of a TTS synthesis, or null if the model's rate isn't known.</summary>
        public static decimal? TtsCost(string model, int characterCount)
        {
            if (!TtsRatesPerMChars.TryGetValue(model, out var perM)) return null;
            return characterCount / 1_000_000m * perM;
        }
    }
}
