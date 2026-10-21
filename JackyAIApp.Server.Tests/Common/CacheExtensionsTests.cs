using JackyAIApp.Server.Common;
using Microsoft.Extensions.Caching.Memory;

namespace JackyAIApp.Server.Tests.Common
{
    /// <summary>
    /// GetOrCreateSingleFlightAsync is the shared cache+single-flight helper that replaces the
    /// hand-written TryGetValue/Set pairs in front of shared upstream reads, so a cold-cache burst
    /// makes one upstream call, not N. GroupToken/InvalidateGroup drop a set of entries together
    /// (a user's word lists) without scanning keys.
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

        [Fact]
        public async Task ResultTurnedDownByShouldCache_IsReturnedButNotCached()
        {
            using var cache = NewCache();
            var key = Guid.NewGuid().ToString();
            var calls = 0;
            Task<List<int>> Factory() { calls++; return Task.FromResult(new List<int>()); }

            var first = await cache.GetOrCreateSingleFlightAsync(key, TimeSpan.FromMinutes(5), Factory, default,
                shouldCache: list => list.Count > 0);
            await cache.GetOrCreateSingleFlightAsync(key, TimeSpan.FromMinutes(5), Factory, default,
                shouldCache: list => list.Count > 0);

            Assert.Empty(first);
            Assert.Equal(2, calls);
            Assert.False(cache.TryGetValue(key, out _));
        }

        // ---- Groups ----------------------------------------------------------------------------

        private static void SetInGroup(IMemoryCache cache, string key, string group) =>
            cache.Set(key, "v", new MemoryCacheEntryOptions().AddExpirationToken(cache.GroupToken(group)));

        [Fact]
        public void InvalidateGroup_DropsEveryEntryOfThatGroupAndNoOther()
        {
            using var cache = NewCache();
            SetInGroup(cache, "a1", "a");
            SetInGroup(cache, "a2", "a");
            SetInGroup(cache, "b1", "b");
            cache.Set("plain", "v");

            cache.InvalidateGroup("a");

            Assert.False(cache.TryGetValue("a1", out _));
            Assert.False(cache.TryGetValue("a2", out _));
            Assert.True(cache.TryGetValue("b1", out _));
            Assert.True(cache.TryGetValue("plain", out _));
        }

        [Fact]
        public void InvalidateGroup_EntriesSetAfterwardsFormAFreshGroup()
        {
            using var cache = NewCache();
            SetInGroup(cache, "before", "g");
            cache.InvalidateGroup("g");

            SetInGroup(cache, "after", "g");
            Assert.True(cache.TryGetValue("after", out _)); // not born expired

            cache.InvalidateGroup("g");
            Assert.False(cache.TryGetValue("after", out _));
        }

        [Fact]
        public void InvalidateGroup_ForAGroupNeverUsed_DoesNothing()
        {
            using var cache = NewCache();
            cache.Set("plain", "v");

            cache.InvalidateGroup("never");

            Assert.True(cache.TryGetValue("plain", out _));
        }

        [Fact]
        public void GroupSurvivesCompaction()
        {
            // The group's token source is NeverRemove: evicted under memory pressure, it would
            // leave its entries holding a token no later InvalidateGroup could cancel.
            using var cache = NewCache();
            cache.Set("kept", "v", new MemoryCacheEntryOptions { Priority = CacheItemPriority.NeverRemove }
                .AddExpirationToken(cache.GroupToken("g")));

            cache.Compact(1.0);
            cache.InvalidateGroup("g");

            Assert.False(cache.TryGetValue("kept", out _));
        }
    }
}
