namespace JackyAIApp.Server.Services.Speech
{
    public record TtsResult(bool Success, byte[]? Audio);

    /// <summary>
    /// Shared TTS synthesis (provider call + deterministic-content cache), factored out of
    /// AudioController so other callers (e.g. the Listening exam's bundled-cost audio playback)
    /// can reuse the same cache and provider call without going through AudioController's
    /// per-request usage charge.
    ///
    /// Two implementations exist and exactly one is registered, chosen at startup by
    /// configuration (see <c>ServiceCollectionExtensions.AddCoreServices</c>):
    /// <see cref="AzureSpeechTextToSpeechService"/> when an Azure Speech key is present,
    /// otherwise <c>OpenAI.TextToSpeechService</c>. Callers cannot tell them apart — both take
    /// the same OpenAI-flavoured (voice, model, speed) triple and return mp3 bytes.
    /// </summary>
    public interface ITextToSpeechService
    {
        /// <summary>Cache lookup only — no provider call, no usage logging. Lets a caller bill
        /// only on an actual cache miss without triggering synthesis as a side effect.</summary>
        bool TryGetCached(string text, string voice, string model, float speed, out byte[]? audio);

        /// <summary>Always calls the provider (bypassing the cache check) and caches the result.
        /// Callers that need cache-then-bill-on-miss should call TryGetCached first.</summary>
        Task<TtsResult> SynthesizeAsync(string text, string voice, string model, float speed, string? userId);
    }
}
