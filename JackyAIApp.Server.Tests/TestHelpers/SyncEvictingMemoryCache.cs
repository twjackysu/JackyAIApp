using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;

namespace JackyAIApp.Server.Tests.TestHelpers
{
    /// <summary>
    /// A dictionary-backed <see cref="IMemoryCache"/> whose <see cref="Remove"/> runs the entry's
    /// post-eviction callbacks before returning. The real MemoryCache queues them on the thread pool,
    /// so a test that needs to see what an eviction does would otherwise have to wait and hope.
    /// Expiration is recorded but never acted on.
    /// </summary>
    internal sealed class SyncEvictingMemoryCache : IMemoryCache
    {
        private readonly Dictionary<object, Entry> _entries = [];

        public int Count => _entries.Count;

        public ICacheEntry CreateEntry(object key) => new Entry(key, this);

        public bool TryGetValue(object key, out object? value)
        {
            var found = _entries.TryGetValue(key, out var entry);
            value = entry?.Value;
            return found;
        }

        public void Remove(object key)
        {
            if (!_entries.Remove(key, out var entry)) return;
            foreach (var registration in entry.PostEvictionCallbacks)
                registration.EvictionCallback?.Invoke(key, entry.Value, EvictionReason.Removed, registration.State);
        }

        public void Dispose() { }

        private sealed class Entry(object key, SyncEvictingMemoryCache owner) : ICacheEntry
        {
            public object Key { get; } = key;
            public object? Value { get; set; }
            public DateTimeOffset? AbsoluteExpiration { get; set; }
            public TimeSpan? AbsoluteExpirationRelativeToNow { get; set; }
            public TimeSpan? SlidingExpiration { get; set; }
            public IList<IChangeToken> ExpirationTokens { get; } = [];
            public IList<PostEvictionCallbackRegistration> PostEvictionCallbacks { get; } = [];
            public CacheItemPriority Priority { get; set; }
            public long? Size { get; set; }

            // Like MemoryCache, an entry is committed when it is disposed (the Set extension does that).
            public void Dispose() => owner._entries[Key] = this;
        }
    }
}
