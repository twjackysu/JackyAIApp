using JackyAIApp.Server.Common;
using Microsoft.Extensions.Caching.Memory;

namespace JackyAIApp.Server.Tests.Common
{
    /// <summary>
    /// GetOrCreateSingleFlightAsync is the shared cache+single-flight helper that replaces the
    /// hand-written TryGetValue/Set pairs on the finance market endpoints (macro, overview,
    /// indicators), so a cold-cache burst makes one upstream call, not N.
    /// </summary>
    public class CacheExtensionsTests
    {
        private static MemoryCache NewCache() => new(new MemoryCacheOptions());

        [Fact]
        public async Task CachesAndReusesAcrossCalls()
        {
            using var cache = NewCache();
            var calls = 0;
            Task<string> Factory() { calls++; return Task.FromResult("v"); }

            var a = await cache.GetOrCreateSingleFlightAsync("k", TimeSpan.FromMinutes(5), Factory, default);
            var b = await cache.GetOrCreateSingleFlightAsync("k", TimeSpan.FromMinutes(5), Factory, default);

            Assert.Equal("v", a);
            Assert.Equal("v", b);
            Assert.Equal(1, calls); // second call is a cache hit
        }

        [Fact]
        public async Task ConcurrentMissesShareOneFactoryRun()
        {
            using var cache = NewCache();
            var gate = new TaskCompletionSource();
            var calls = 0;
            async Task<int> Factory()
            {
                Interlocked.Increment(ref calls);
                await gate.Task;
                return 7;
            }

            var key = Guid.NewGuid().ToString();
            var tasks = Enumerable.Range(0, 10)
                .Select(_ => cache.GetOrCreateSingleFlightAsync(key, TimeSpan.FromMinutes(5), Factory, default))
                .ToList();
            await Task.Delay(50);
            gate.SetResult();
            var results = await Task.WhenAll(tasks);

            Assert.All(results, r => Assert.Equal(7, r));
            Assert.Equal(1, calls);
        }

        [Fact]
        public async Task ThrowingFactoryIsNotCached_NextCallRetries()
        {
            using var cache = NewCache();
            var calls = 0;
            Task<string> Factory()
            {
                calls++;
                if (calls == 1) throw new InvalidOperationException("boom");
                return Task.FromResult("ok");
            }

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => cache.GetOrCreateSingleFlightAsync("k", TimeSpan.FromMinutes(5), Factory, default));
            var second = await cache.GetOrCreateSingleFlightAsync("k", TimeSpan.FromMinutes(5), Factory, default);

            Assert.Equal("ok", second);
            Assert.Equal(2, calls);
        }

        [Fact]
        public async Task NullResultIsNotCached()
        {
            using var cache = NewCache();
            var calls = 0;
            Task<string?> Factory() { calls++; return Task.FromResult<string?>(null); }

            await cache.GetOrCreateSingleFlightAsync("k", TimeSpan.FromMinutes(5), Factory, default);
            await cache.GetOrCreateSingleFlightAsync("k", TimeSpan.FromMinutes(5), Factory, default);

            Assert.Equal(2, calls); // null never cached, so both run
        }
    }
}
