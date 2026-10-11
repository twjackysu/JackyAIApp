using JackyAIApp.Server.DTO.Finance;
using JackyAIApp.Server.Services.Finance.DataProviders;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Internal;
using Microsoft.Extensions.Logging.Abstractions;

namespace JackyAIApp.Server.Tests.Services.Finance
{
    /// <summary>
    /// <see cref="CachedMarketIndexHistoryProvider"/>: the number of Yahoo requests must not depend on
    /// how many people load the chart, and an outage must degrade to the last good copy or an error —
    /// never to an empty or partial series. Joins the SingleFlight collection because the provider
    /// shares its process-wide in-flight table.
    /// </summary>
    [Collection(SingleFlightCollection.Name)]
    public class CachedMarketIndexHistoryProviderTests
    {
        // A regression in the single-flight logic should fail these tests, not hang them.
        private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

        private readonly TestClock _clock = new();
        private readonly MemoryCache _cache;
        private readonly FakeInner _inner = new();
        private readonly CachedMarketIndexHistoryProvider _provider;

        public CachedMarketIndexHistoryProviderTests()
        {
            _cache = new MemoryCache(new MemoryCacheOptions { Clock = _clock });
            _provider = new CachedMarketIndexHistoryProvider(
                _inner, _cache, NullLogger<CachedMarketIndexHistoryProvider>.Instance);
        }

        private static MarketIndexHistoryResponse History(int asOfDay) => new(
            "^TWII", "加權指數（價格指數，不含息）", "Yahoo Finance",
            new DateOnly(2026, 10, asOfDay), new[] { new MarketIndexPoint(new DateOnly(2026, 10, asOfDay), 20000m + asOfDay) });

        // ---- Hits and misses -------------------------------------------------------------------

        [Fact]
        public async Task RepeatedCalls_InsideTheFreshWindow_HitUpstreamOnce()
        {
            var history = History(8);
            _inner.Enqueue(history);

            var results = new List<MarketIndexHistoryResponse?>();
            for (var i = 0; i < 5; i++)
            {
                results.Add(await _provider.GetAsync());
                _clock.Advance(TimeSpan.FromMinutes(10)); // 4 x 10 min = 40 min, still inside the hour
            }

            Assert.Equal(1, _inner.Calls);
            Assert.All(results, r => Assert.Same(history, r));
        }

        [Fact]
        public async Task AfterTheFreshWindow_ItFetchesAgain_AndServesTheNewCopy()
        {
            var first = History(8);
            var second = History(9);
            _inner.Enqueue(first);
            _inner.Enqueue(second);

            Assert.Same(first, await _provider.GetAsync());
            _clock.Advance(CachedMarketIndexHistoryProvider.FreshTtl - TimeSpan.FromSeconds(1));
            Assert.Same(first, await _provider.GetAsync());
            Assert.Equal(1, _inner.Calls);

            _clock.Advance(TimeSpan.FromSeconds(2));
            Assert.Same(second, await _provider.GetAsync());
            Assert.Equal(2, _inner.Calls);
        }

        [Fact]
        public void TheFreshWindow_IsOneHour_SoUpstreamSeesAtMost24RequestsADay()
        {
            Assert.Equal(TimeSpan.FromHours(1), CachedMarketIndexHistoryProvider.FreshTtl);
            Assert.Equal(TimeSpan.FromDays(7), CachedMarketIndexHistoryProvider.LastGoodTtl);
        }

        // ---- Single flight ---------------------------------------------------------------------

        [Fact]
        public async Task TwentyConcurrentCallers_ShareOneUpstreamFetch()
        {
            var history = History(8);
            var gate = new TaskCompletionSource<MarketIndexHistoryResponse?>(TaskCreationOptions.RunContinuationsAsynchronously);
            _inner.Enqueue(gate.Task);

            var callers = Enumerable.Range(0, 20).Select(_ => Task.Run(() => _provider.GetAsync())).ToArray();
            await _inner.WaitForCallsAsync(1);
            await Task.Delay(100); // let the other callers reach the flight
            gate.SetResult(history);
            var results = await Task.WhenAll(callers).WaitAsync(Patience);

            Assert.Equal(1, _inner.Calls);
            Assert.All(results, r => Assert.Same(history, r));
        }

        [Fact]
        public async Task TheSharedFetch_DoesNotBelongToAnyOneCaller()
        {
            // The first visitor gives up; the one queued behind it must still get the answer, and
            // the fetch itself must not be handed the first visitor's (now cancelled) token.
            var history = History(8);
            var gate = new TaskCompletionSource<MarketIndexHistoryResponse?>(TaskCreationOptions.RunContinuationsAsynchronously);
            _inner.Enqueue(gate.Task);
            using var firstCaller = new CancellationTokenSource();

            var first = _provider.GetAsync(firstCaller.Token);
            await _inner.WaitForCallsAsync(1);
            var second = _provider.GetAsync();

            firstCaller.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first.WaitAsync(Patience));
            Assert.False(second.IsCompleted);

            gate.SetResult(history);

            Assert.Same(history, await second.WaitAsync(Patience));
            Assert.Equal(1, _inner.Calls);
            Assert.False(_inner.TokensSeen.Single().CanBeCanceled);
        }

        // ---- Failure handling ------------------------------------------------------------------

        [Fact]
        public async Task FailureWithNothingCached_IsNull()
        {
            _inner.EnqueueFailure();

            Assert.Null(await _provider.GetAsync());
        }

        [Fact]
        public async Task ProviderThatThrows_IsTreatedAsAFailure_NotPropagated()
        {
            _inner.EnqueueThrow(new InvalidOperationException("boom"));

            Assert.Null(await _provider.GetAsync());
        }

        [Fact]
        public async Task FailedRefresh_ServesTheLastGoodCopy_WithItsOwnAsOf()
        {
            var old = History(8);
            _inner.Enqueue(old);
            _inner.EnqueueFailure();

            await _provider.GetAsync();
            _clock.Advance(CachedMarketIndexHistoryProvider.FreshTtl + TimeSpan.FromMinutes(1));
            var served = await _provider.GetAsync();

            Assert.Equal(2, _inner.Calls);
            Assert.Same(old, served);
            Assert.Equal(new DateOnly(2026, 10, 8), served!.AsOf);
        }

        [Fact]
        public async Task AfterAFailure_UpstreamIsLeftAloneForFiveMinutes()
        {
            _inner.Enqueue(History(8));
            _inner.EnqueueFailure();
            _inner.EnqueueFailure();

            await _provider.GetAsync();
            _clock.Advance(CachedMarketIndexHistoryProvider.FreshTtl + TimeSpan.FromMinutes(1));
            await _provider.GetAsync();                      // the failed refresh
            Assert.Equal(2, _inner.Calls);

            // A burst of page views during the outage costs nothing upstream...
            for (var i = 0; i < 50; i++) await _provider.GetAsync();
            _clock.Advance(CachedMarketIndexHistoryProvider.FailureBackoff - TimeSpan.FromSeconds(1));
            await _provider.GetAsync();
            Assert.Equal(2, _inner.Calls);

            // ...and one more try is made once the backoff is over.
            _clock.Advance(TimeSpan.FromSeconds(2));
            await _provider.GetAsync();
            Assert.Equal(3, _inner.Calls);
        }

        [Fact]
        public async Task AfterAFailureWithNothingCached_ItStillBacksOff_AndKeepsAnswering_Null()
        {
            _inner.EnqueueFailure();
            _inner.Enqueue(History(8));

            Assert.Null(await _provider.GetAsync());
            Assert.Null(await _provider.GetAsync());
            Assert.Equal(1, _inner.Calls);

            _clock.Advance(CachedMarketIndexHistoryProvider.FailureBackoff + TimeSpan.FromSeconds(1));
            Assert.NotNull(await _provider.GetAsync());
            Assert.Equal(2, _inner.Calls);
        }

        [Fact]
        public async Task RecoveryAfterAnOutage_ReplacesTheLastGoodCopy_AndResumesNormalCaching()
        {
            var old = History(8);
            var recovered = History(15);
            _inner.Enqueue(old);
            _inner.EnqueueFailure();
            _inner.Enqueue(recovered);

            await _provider.GetAsync();
            _clock.Advance(CachedMarketIndexHistoryProvider.FreshTtl + TimeSpan.FromMinutes(1));
            Assert.Same(old, await _provider.GetAsync());                       // outage
            _clock.Advance(CachedMarketIndexHistoryProvider.FailureBackoff + TimeSpan.FromSeconds(1));
            Assert.Same(recovered, await _provider.GetAsync());                 // back
            Assert.Same(recovered, await _provider.GetAsync());                 // and fresh again
            Assert.Equal(3, _inner.Calls);
        }

        [Fact]
        public async Task LastGoodCopy_IsKeptForSevenDays_ThenTheAnswerIsNull()
        {
            _inner.Enqueue(History(8));
            await _provider.GetAsync();

            // Yahoo stays down from now on.
            for (var i = 0; i < 50; i++) _inner.EnqueueFailure();

            _clock.Advance(CachedMarketIndexHistoryProvider.LastGoodTtl - TimeSpan.FromHours(1));
            Assert.NotNull(await _provider.GetAsync());

            _clock.Advance(TimeSpan.FromHours(2));
            Assert.Null(await _provider.GetAsync());
        }

        [Fact]
        public async Task AFailedFetch_IsNeverCachedAsFresh()
        {
            _inner.EnqueueFailure();
            await _provider.GetAsync();

            Assert.False(_cache.TryGetValue(CachedMarketIndexHistoryProvider.FreshKey, out _));
            Assert.False(_cache.TryGetValue(CachedMarketIndexHistoryProvider.LastGoodKey, out _));
        }

        // ---- Fakes -----------------------------------------------------------------------------

        private sealed class TestClock : ISystemClock
        {
            public DateTimeOffset UtcNow { get; private set; } = new(2026, 10, 8, 6, 0, 0, TimeSpan.Zero);
            public void Advance(TimeSpan by) => UtcNow += by;
        }

        /// <summary>Hands out queued results (or tasks) in order, counting calls and recording tokens.</summary>
        private sealed class FakeInner : IMarketIndexHistoryProvider
        {
            private readonly Queue<Func<Task<MarketIndexHistoryResponse?>>> _script = new();
            private readonly object _lock = new();
            private int _calls;
            public List<CancellationToken> TokensSeen { get; } = new();

            public int Calls => Volatile.Read(ref _calls);

            public void Enqueue(MarketIndexHistoryResponse result) => _script.Enqueue(() => Task.FromResult<MarketIndexHistoryResponse?>(result));
            public void EnqueueFailure() => _script.Enqueue(() => Task.FromResult<MarketIndexHistoryResponse?>(null));
            public void Enqueue(Task<MarketIndexHistoryResponse?> result) => _script.Enqueue(() => result);
            public void EnqueueThrow(Exception ex) => _script.Enqueue(() => throw ex);

            public Task<MarketIndexHistoryResponse?> GetAsync(CancellationToken cancellationToken = default)
            {
                Func<Task<MarketIndexHistoryResponse?>> next;
                lock (_lock)
                {
                    TokensSeen.Add(cancellationToken);
                    Interlocked.Increment(ref _calls);
                    Assert.True(_script.Count > 0, "upstream was called more often than the test scripted");
                    next = _script.Dequeue();
                }
                return next();
            }

            public async Task WaitForCallsAsync(int count)
            {
                var deadline = DateTime.UtcNow.AddSeconds(5);
                while (Calls < count)
                {
                    Assert.True(DateTime.UtcNow < deadline, $"upstream was never called {count} time(s)");
                    await Task.Delay(10);
                }
            }
        }
    }
}
