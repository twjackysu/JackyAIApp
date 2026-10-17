using Microsoft.Extensions.Caching.Memory;

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
        /// calls); <paramref name="ct"/> only stops THIS caller waiting on the shared task. A factory
        /// that throws is not cached, so the next request retries; a null result is not cached either.
        /// </summary>
        public static async Task<T> GetOrCreateSingleFlightAsync<T>(
            this IMemoryCache cache, string key, TimeSpan ttl, Func<Task<T>> factory, CancellationToken ct)
        {
            if (cache.TryGetValue(key, out T? cached) && cached is not null)
                return cached;

            return await SingleFlight.RunAsync(key, async () =>
            {
                // The flight winner re-checks: an earlier flight may have filled the cache while we queued.
                if (cache.TryGetValue(key, out T? current) && current is not null)
                    return current;

                var result = await factory();
                if (result is not null)
                    cache.Set(key, result, ttl);
                return result;
            }, ct);
        }
    }
}
