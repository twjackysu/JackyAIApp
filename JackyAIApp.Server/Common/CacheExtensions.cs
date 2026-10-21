using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;

namespace JackyAIApp.Server.Common
{
    public static class CacheExtensions
    {
        /// <summary>
        /// Returns the cached value for <paramref name="key"/>, or computes it once and caches it for
        /// <paramref name="ttl"/>. Concurrent misses for the same key share a single
        /// <paramref name="factory"/> run (see <see cref="SingleFlight"/>) instead of each hitting the
        /// upstream — the de-duplication a plain <c>TryGetValue</c>/<c>Set</c> pair lacks, so on a cold
        /// cache a burst of requests for the same shared resource makes one external call, not N.
        ///
        /// <paramref name="factory"/> is that shared run, so it must not depend on a per-caller
        /// <see cref="CancellationToken"/> (pass <see cref="CancellationToken.None"/> to whatever it
        /// calls); <paramref name="ct"/> only stops THIS caller waiting on the shared task. For the same
        /// reason it may outlive the request that started it, so it must not use that request's
        /// disposables either (its scoped DbContext: take an <c>IDbContextFactory</c> context). A factory
        /// that throws is not cached, so the next request retries; a null result is not cached either,
        /// nor one <paramref name="shouldCache"/> turns down (an empty answer from a failing upstream) —
        /// those are still returned, just asked for again next time.
        /// </summary>
        public static async Task<T> GetOrCreateSingleFlightAsync<T>(
            this IMemoryCache cache, string key, TimeSpan ttl, Func<Task<T>> factory, CancellationToken ct,
            Func<T, bool>? shouldCache = null)
        {
            if (cache.TryGetValue(key, out T? cached) && cached is not null)
                return cached;

            return await SingleFlight.RunAsync(key, async () =>
            {
                // The flight winner re-checks: an earlier flight may have filled the cache while we queued.
                if (cache.TryGetValue(key, out T? current) && current is not null)
                    return current;

                var result = await factory();
                if (result is not null && (shouldCache is null || shouldCache(result)))
                    cache.Set(key, result, ttl);
                return result;
            }, ct);
        }

        /// <summary>
        /// A change token for the entries of <paramref name="group"/>: an entry set with it
        /// (<c>options.AddExpirationToken(cache.GroupToken(group))</c>) is dropped by
        /// <see cref="InvalidateGroup"/>, together with every other entry of the group.
        /// </summary>
        public static IChangeToken GroupToken(this IMemoryCache cache, string group)
        {
            var source = cache.GetOrCreate(GroupKey(group), entry =>
            {
                // Evicting the source alone would orphan the group's entries: a later
                // InvalidateGroup would cancel a new source, not the one they hold.
                entry.Priority = CacheItemPriority.NeverRemove;
                return new CancellationTokenSource();
            })!;
            return new CancellationChangeToken(source.Token);
        }

        /// <summary>Drops every entry set with <see cref="GroupToken"/> for <paramref name="group"/>.</summary>
        public static void InvalidateGroup(this IMemoryCache cache, string group)
        {
            if (!cache.TryGetValue(GroupKey(group), out CancellationTokenSource? source) || source is null)
                return;
            cache.Remove(GroupKey(group));
            // Not disposed: a request may have read the source a moment ago and still be taking its
            // token. A cancelled source holds no timer, so leaving it to the GC costs nothing.
            source.Cancel();
        }

        private static string GroupKey(string group) => "cache-group:" + group;
    }
}
