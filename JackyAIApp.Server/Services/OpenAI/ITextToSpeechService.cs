namespace JackyAIApp.Server.Services.OpenAI
{
    public record TtsResult(bool Success, byte[]? Audio);

    /// <summary>
    /// Shared TTS synthesis (OpenAI call + deterministic-content cache), factored out of
    /// AudioController so other callers (e.g. the Listening exam's bundled-cost audio playback)
    /// can reuse the same cache and OpenAI call without going through AudioController's
    /// per-request credit charge.
    /// </summary>
    public interface ITextToSpeechService
    {
        /// <summary>Cache lookup only — no OpenAI call, no usage logging. Lets a caller bill
        /// only on an actual cache miss without triggering synthesis as a side effect.</summary>
        bool TryGetCached(string text, string voice, string model, float speed, out byte[]? audio);

        /// <summary>Always calls OpenAI (bypassing the cache check) and caches the result.
        /// Callers that need cache-then-bill-on-miss should call TryGetCached first.</summary>
        Task<TtsResult> SynthesizeAsync(string text, string voice, string model, float speed, string? userId);
    }
}
