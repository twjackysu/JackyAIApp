using Microsoft.Extensions.Caching.Memory;

namespace JackyAIApp.Server.Services.Speech
{
    /// <summary>
    /// The cache shared by every <see cref="ITextToSpeechService"/> implementation (a singleton).
    ///
    /// Lives here rather than inside one implementation so the key format and lifetime cannot
    /// drift apart between providers: <c>AudioController</c> calls
    /// <see cref="ITextToSpeechService.TryGetCached"/> to decide whether to charge usage, so a
    /// key computed differently on the read and the write path would bill the user for every
    /// playback instead of only the first.
    ///
    /// Its own <see cref="MemoryCache"/>, bounded by the bytes it holds (D-173): the endpoint takes
    /// anonymous text, and every new text is a new clip (up to about a megabyte), so in the app's shared,
    /// unbounded cache a loop of made-up sentences could fill the server's memory within its 30-day lifetime.
    /// Past <see cref="MaxBytes"/> a new clip is not kept and the least recently read are dropped; a clip
    /// not kept is only made again when asked again.
    /// </summary>
    public sealed class TtsCache : IDisposable
    {
        // Same text always produces the same audio for a given (model, voice, speed) — see
        // AudioController for the full cache-lifetime rationale. Shared so every caller
        // (billed dictionary/chat playback, unbilled Listening-exam playback) hits one cache.
        public static readonly TimeSpan Ttl = TimeSpan.FromDays(30);

        /// <summary>
        /// The most audio held at once. The App Service (B1) has 1.75 GB for everything; a dictionary
        /// sentence is tens of kilobytes, so this still keeps thousands of them.
        /// </summary>
        public const long MaxBytes = 128L * 1024 * 1024;

        private readonly MemoryCache _cache;

        public TtsCache() : this(MaxBytes) { }

        internal TtsCache(long maxBytes) =>
            _cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = maxBytes });

        public static string Key(string text, string voice, string model, float speed) =>
            $"tts_{model}_{voice}_{speed}_{text}";

        public bool TryGet(string text, string voice, string model, float speed, out byte[]? audio)
        {
            if (_cache.TryGetValue(Key(text, voice, model, speed), out object? cached) && cached is byte[] cachedAudio)
            {
                audio = cachedAudio;
                return true;
            }
            audio = null;
            return false;
        }

        public void Set(string text, string voice, string model, float speed, byte[] audio) =>
            _cache.Set(Key(text, voice, model, speed), audio, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = Ttl,
                // At least 1: an entry of size 0 would never count against the limit.
                Size = Math.Max(1, audio.Length),
            });

        /// <summary>How many clips are held (tests).</summary>
        internal int Count => _cache.Count;

        public void Dispose() => _cache.Dispose();
    }
}
