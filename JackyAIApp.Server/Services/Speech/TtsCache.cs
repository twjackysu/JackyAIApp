using DotnetSdkUtilities.Services;

namespace JackyAIApp.Server.Services.Speech
{
    /// <summary>
    /// The cache shared by every <see cref="ITextToSpeechService"/> implementation.
    ///
    /// Lives here rather than inside one implementation so the key format and lifetime cannot
    /// drift apart between providers: <c>AudioController</c> calls
    /// <see cref="ITextToSpeechService.TryGetCached"/> to decide whether to charge credits, so a
    /// key computed differently on the read and the write path would bill the user for every
    /// playback instead of only the first.
    /// </summary>
    internal static class TtsCache
    {
        // Same text always produces the same audio for a given (model, voice, speed) — see
        // AudioController for the full cache-lifetime rationale. Shared so every caller
        // (billed dictionary/chat playback, unbilled Listening-exam playback) hits one cache.
        public static readonly TimeSpan Ttl = TimeSpan.FromDays(30);

        public static string Key(string text, string voice, string model, float speed) =>
            $"tts_{model}_{voice}_{speed}_{text}";

        public static bool TryGet(IExtendedMemoryCache cache, string text, string voice, string model, float speed, out byte[]? audio)
        {
            if (cache.TryGetValue(Key(text, voice, model, speed), out object? cached) && cached is byte[] cachedAudio)
            {
                audio = cachedAudio;
                return true;
            }
            audio = null;
            return false;
        }

        public static void Set(IExtendedMemoryCache cache, string text, string voice, string model, float speed, byte[] audio) =>
            cache.Set(Key(text, voice, model, speed), audio, Ttl);
    }
}
