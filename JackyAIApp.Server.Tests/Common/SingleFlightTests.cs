using JackyAIApp.Server.Common;

namespace JackyAIApp.Server.Tests.Common
{
    /// <summary>
    /// Covers the generic de-duplication mechanism TASK-025 added to collapse concurrent
    /// cache-miss fetches for the same key (used by PortfolioService's stock-price/snapshots
    /// fetch, FundDataService's NAV fetch, and DividendService's dividend fetches).
    /// </summary>
    public class SingleFlightTests
    {
        [Fact]
        public async Task RunAsync_ConcurrentCallsSameKey_FactoryInvokedOnce()
        {
            var gate = new TaskCompletionSource();
            var callCount = 0;

            async Task<int> Factory()
            {
                Interlocked.Increment(ref callCount);
                await gate.Task; // held open until every caller has joined
                return 42;
            }

            var key = Guid.NewGuid().ToString();
            var calls = Enumerable.Range(0, 10)
                .Select(_ => SingleFlight.RunAsync(key, Factory, CancellationToken.None))
                .ToList();

            // Give every call a chance to reach SingleFlight.RunAsync before releasing the factory.
            await Task.Delay(50);
            gate.SetResult();
            var results = await Task.WhenAll(calls);

            Assert.Equal(1, callCount);
            Assert.All(results, r => Assert.Equal(42, r));
        }

        [Fact]
        public async Task RunAsync_DifferentKeys_FactoryInvokedOncePerKey()
        {
            var callCount = 0;
            Task<int> Factory()
            {
                Interlocked.Increment(ref callCount);
                return Task.FromResult(1);
            }

            await Task.WhenAll(
                SingleFlight.RunAsync(Guid.NewGuid().ToString(), Factory, CancellationToken.None),
                SingleFlight.RunAsync(Guid.NewGuid().ToString(), Factory, CancellationToken.None));

            Assert.Equal(2, callCount);
        }

        [Fact]
        public async Task RunAsync_AfterPriorCallCompletes_NextCallWithSameKeyRunsFactoryAgain()
        {
            // SingleFlight only de-duplicates calls that overlap in time — it is not a cache.
            // Caching (and how long a result stays fresh) stays the caller's own responsibility.
            var callCount = 0;
            Task<int> Factory()
            {
                Interlocked.Increment(ref callCount);
                return Task.FromResult(1);
            }

            var key = Guid.NewGuid().ToString();
            await SingleFlight.RunAsync(key, Factory, CancellationToken.None);
            await SingleFlight.RunAsync(key, Factory, CancellationToken.None);

            Assert.Equal(2, callCount);
        }

        [Fact]
        public async Task RunAsync_FactoryThrows_PropagatesToEveryWaiterAndAllowsRetry()
        {
            var key = Guid.NewGuid().ToString();

            async Task<int> FailingFactory()
            {
                await Task.Yield();
                throw new InvalidOperationException("upstream down");
            }

            var call1 = SingleFlight.RunAsync(key, FailingFactory, CancellationToken.None);
            var call2 = SingleFlight.RunAsync(key, FailingFactory, CancellationToken.None);

            await Assert.ThrowsAsync<InvalidOperationException>(() => call1);
            await Assert.ThrowsAsync<InvalidOperationException>(() => call2);

            // Failure isn't cached inside SingleFlight — the key is free for a fresh attempt.
            var result = await SingleFlight.RunAsync(key, () => Task.FromResult(7), CancellationToken.None);
            Assert.Equal(7, result);
        }

        [Fact]
        public async Task RunAsync_OneCallerCancels_OtherCallersStillGetTheSharedResult()
        {
            var key = Guid.NewGuid().ToString();
            var gate = new TaskCompletionSource<int>();
            Task<int> Factory() => gate.Task;

            using var cts = new CancellationTokenSource();
            var cancelledCall = SingleFlight.RunAsync(key, Factory, cts.Token);
            var otherCall = SingleFlight.RunAsync(key, Factory, CancellationToken.None);

            cts.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelledCall);

            // The shared fetch must still be running for the other caller — releasing it now
            // must resolve otherCall successfully, proving cancelledCall's cancellation never
            // touched the underlying factory task.
            gate.SetResult(99);
            Assert.Equal(99, await otherCall);
        }
    }
}
