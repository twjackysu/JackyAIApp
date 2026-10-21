namespace JackyAIApp.Server.Common
{
    /// <summary>
    /// Approximate AI provider pricing (OpenAI + Anthropic), centralized so cost estimates live in
    /// one place. These are rough public list rates as of 2026-07 and WILL drift — the raw
    /// token/character counts logged alongside every <see cref="Data.Models.SQL.AiUsageLog"/> row
    /// are authoritative; this only pre-computes a convenient SUM-able cost column for the usage
    /// dashboard. Verify against each provider's billing dashboard for real spend.
    /// </summary>
    public static class AiRates
    {
        // Chat model rates in USD per 1,000,000 tokens (input, output). Output includes reasoning
        // tokens on reasoning-family models.
        private static readonly Dictionary<string, (decimal InputPerM, decimal OutputPerM)> ChatRates =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["gpt-6-luna"] = (0.10m, 0.50m),
                // Kept for the usage rows logged before the chatbot moved to Haiku 5.5.
                ["claude-haiku-4-5"] = (1.00m, 5.00m),
                ["claude-haiku-5-5"] = (0.10m, 0.50m),
            };

        // Models billed on a second, higher rate card once the prompt is longer than a threshold:
        // (prompt tokens above which it applies, input, output) in USD per 1,000,000 tokens.
        private static readonly Dictionary<string, (int AbovePromptTokens, decimal InputPerM, decimal OutputPerM)> LongPromptRates =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["claude-haiku-5-5"] = (100_000, 0.50m, 2.50m),
            };

        // TTS rates in USD per 1,000,000 input characters.
        private static readonly Dictionary<string, decimal> TtsRatesPerMChars =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["tts-1"] = 15m,
                ["tts-1-hd"] = 30m,
                // Azure AI Speech on the F0 tier: 500,000 characters/month at no charge, against
                // measured usage of roughly 2,000/month. Zero rather than null so the dashboard's
                // SUM stays a real total instead of quietly dropping these rows.
                ["azure-neural-tts"] = 0m,
            };

        // Image generation rates in USD per image. Assumes low-quality tier (what the Listening
        // exam's admin generator requests) — gpt-image-1-mini's cheapest official tier as of 2026-07.
        private static readonly Dictionary<string, decimal> ImageRatesPerImage =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["gpt-image-1-mini"] = 0.005m,
            };

        // Anthropic prompt caching, as multiples of the input rate: a write to the 5-minute cache
        // (the only TTL used) and a read from it.
        private const decimal CacheWriteMultiplier = 1.25m;
        private const decimal CacheReadMultiplier = 0.1m;

        /// <summary>USD cost of a chat completion, or null if the model's rate isn't known.</summary>
        /// <param name="promptTokens">All input tokens, including those written to or read from the
        /// prompt cache — the long-prompt rate card is chosen on this total.</param>
        /// <param name="cacheWriteTokens">Of <paramref name="promptTokens"/>, written to the cache.</param>
        /// <param name="cacheReadTokens">Of <paramref name="promptTokens"/>, read from the cache.</param>
        public static decimal? ChatCost(string model, int promptTokens, int completionTokens,
            int cacheWriteTokens = 0, int cacheReadTokens = 0)
        {
            if (!ChatRates.TryGetValue(model, out var r)) return null;
            if (LongPromptRates.TryGetValue(model, out var l) && promptTokens > l.AbovePromptTokens)
                r = (l.InputPerM, l.OutputPerM);
            var uncached = promptTokens - cacheWriteTokens - cacheReadTokens;
            return (uncached + cacheWriteTokens * CacheWriteMultiplier + cacheReadTokens * CacheReadMultiplier)
                   / 1_000_000m * r.InputPerM
                 + completionTokens / 1_000_000m * r.OutputPerM;
        }

        /// <summary>USD cost of a TTS synthesis, or null if the model's rate isn't known.</summary>
        public static decimal? TtsCost(string model, int characterCount)
        {
            if (!TtsRatesPerMChars.TryGetValue(model, out var perM)) return null;
            return characterCount / 1_000_000m * perM;
        }

        /// <summary>USD cost of generating N images, or null if the model's rate isn't known.</summary>
        public static decimal? ImageCost(string model, int imageCount)
        {
            if (!ImageRatesPerImage.TryGetValue(model, out var perImage)) return null;
            return imageCount * perImage;
        }
    }
}
