using JackyAIApp.Server.Services.ExternalApi;
using JackyAIApp.Server.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace JackyAIApp.Server.Tests.Services.ExternalApi
{
    /// <summary>
    /// The gateway in front of every third-party API: one shared request per thing asked for,
    /// interactive before background, a burst then a sustained pace, bounded concurrency and a
    /// timeout. Time is a <see cref="ManualTimeProvider"/>, so pacing is asserted exactly.
    /// </summary>
    public class ThirdPartyGatewayTests
    {
        private const string Source = "test-source";

        private readonly ManualTimeProvider _clock = new();
        private readonly ThirdPartyGatewayOptions _options = new();

        private ThirdPartyGateway Gateway(ThirdPartySourceOptions? source = null)
        {
            _options.Sources[Source] = source ?? new ThirdPartySourceOptions();
            var monitor = new Mock<IOptionsMonitor<ThirdPartyGatewayOptions>>();
            monitor.Setup(m => m.CurrentValue).Returns(_options);
            return new ThirdPartyGateway(monitor.Object, _clock, NullLogger<ThirdPartyGateway>.Instance);
        }

        /// <summary>Records which requests actually ran, and lets a test hold one open.</summary>
        private sealed class Probe
        {
            private readonly object _gate = new();
            private readonly List<string> _started = [];

            public IReadOnlyList<string> Started { get { lock (_gate) return [.. _started]; } }

            public Func<CancellationToken, Task<int>> Fetch(string name, int result = 0, Task? hold = null) => async _ =>
            {
                lock (_gate) _started.Add(name);
                if (hold is not null) await hold;
                return result;
            };
        }

        /// <summary>The gateway starts work on the thread pool; waits (in real time) until a condition holds.</summary>
        private static async Task Eventually(Func<bool> condition, string because)
        {
            for (var i = 0; i < 400 && !condition(); i++) await Task.Delay(5);
            Assert.True(condition(), because);
        }

        /// <summary>Lets anything that is going to start have started, then checks it did not go further.</summary>
        private static async Task Settle() => await Task.Delay(60);

        private static Task<int> Run(ThirdPartyGateway g, string key, Func<CancellationToken, Task<int>> fetch,
            GatewayPriority priority = GatewayPriority.Interactive, CancellationToken ct = default) =>
            g.RunAsync(Source, key, priority, fetch, ct);

        // ---- one shared request ----------------------------------------------------------------

        [Fact]
        public async Task TheSameRequestInFlight_RunsOnce_AndEveryCallerGetsItsResult()
        {
            var gateway = Gateway();
            var probe = new Probe();
            var release = new TaskCompletionSource();

            var calls = Enumerable.Range(0, 5).Select(_ => Run(gateway, "2330:202610", probe.Fetch("fetch", 42, release.Task))).ToList();
            await Eventually(() => probe.Started.Count == 1, "the first caller starts the request");
            await Settle();
            release.SetResult();

            Assert.All(await Task.WhenAll(calls), r => Assert.Equal(42, r));
            Assert.Single(probe.Started);
        }

        [Fact]
        public async Task DifferentRequests_RunSeparately()
        {
            var gateway = Gateway();
            var probe = new Probe();

            var results = await Task.WhenAll(
                Run(gateway, "2330:202610", probe.Fetch("a", 1)),
                Run(gateway, "2330:202609", probe.Fetch("b", 2)));

            Assert.Equal(new[] { 1, 2 }, results);
            Assert.Equal(["a", "b"], probe.Started.Order());
        }

        [Fact]
        public async Task TheSameKeyForADifferentResultType_IsADifferentRequest()
        {
            var gateway = Gateway();

            var number = gateway.RunAsync(Source, "k", GatewayPriority.Interactive, _ => Task.FromResult(7), default);
            var text = gateway.RunAsync(Source, "k", GatewayPriority.Interactive, _ => Task.FromResult("seven"), default);

            Assert.Equal(7, await number);
            Assert.Equal("seven", await text);
        }

        [Fact]
        public async Task AFinishedRequest_IsNotKept_SoTheNextCallRunsAgain()
        {
            var gateway = Gateway();
            var probe = new Probe();

            await Run(gateway, "k", probe.Fetch("first"));
            await Run(gateway, "k", probe.Fetch("second"));

            Assert.Equal(["first", "second"], probe.Started);
        }

        [Fact]
        public async Task AFailure_ReachesEveryWaiter_AndIsNotRemembered()
        {
            var gateway = Gateway();
            var release = new TaskCompletionSource();
            var runs = 0;
            Func<CancellationToken, Task<int>> flaky = async _ =>
            {
                if (Interlocked.Increment(ref runs) == 1)
                {
                    await release.Task;
                    throw new InvalidOperationException("upstream said no");
                }
                return 5;
            };

            var first = Run(gateway, "k", flaky);
            var second = Run(gateway, "k", flaky);
            await Eventually(() => runs == 1, "the request started");
            release.SetResult();

            await Assert.ThrowsAsync<InvalidOperationException>(() => first);
            await Assert.ThrowsAsync<InvalidOperationException>(() => second);
            Assert.Equal(5, await Run(gateway, "k", flaky));   // the next call tries again
            Assert.Equal(2, runs);
        }

        [Fact]
        public async Task ACallerThatStopsWaiting_DoesNotCancelTheRequestForTheOthers()
        {
            var gateway = Gateway();
            var release = new TaskCompletionSource();
            var fetchWasCancelled = false;
            using var leaves = new CancellationTokenSource();
            Func<CancellationToken, Task<int>> fetch = async token =>
            {
                await release.Task;
                fetchWasCancelled = token.IsCancellationRequested;
                return 9;
            };

            var impatient = Run(gateway, "k", fetch, ct: leaves.Token);
            var patient = Run(gateway, "k", fetch);
            await Eventually(() => gateway.GetStats(Source).Running == 1, "the request is running");
            leaves.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => impatient);
            release.SetResult();

            Assert.Equal(9, await patient);
            Assert.False(fetchWasCancelled);
        }

        // ---- pacing ----------------------------------------------------------------------------

        [Fact]
        public async Task ABurstGoesOutAtOnce_ThenTheSourceIsPaced()
        {
            var gateway = Gateway(new() { MinIntervalMs = 1000, Burst = 2, BackgroundReserve = 0 });
            var probe = new Probe();

            var calls = Enumerable.Range(1, 5).Select(i => Run(gateway, $"r{i}", probe.Fetch($"r{i}", i))).ToList();
            await Eventually(() => probe.Started.Count == 2, "the burst allowance goes out at once");
            await Settle();
            Assert.Equal(2, probe.Started.Count);

            _clock.Advance(TimeSpan.FromMilliseconds(999));
            await Settle();
            Assert.Equal(2, probe.Started.Count);   // not a moment early

            _clock.Advance(TimeSpan.FromMilliseconds(1));
            await Eventually(() => probe.Started.Count == 3, "one request after an interval");
            _clock.Advance(TimeSpan.FromSeconds(1));
            await Eventually(() => probe.Started.Count == 4, "and one more");
            _clock.Advance(TimeSpan.FromSeconds(1));
            await Eventually(() => probe.Started.Count == 5, "and the last");

            Assert.Equal(new[] { 1, 2, 3, 4, 5 }, await Task.WhenAll(calls));
        }

        [Fact]
        public async Task ASourceWithNoPacing_StillBoundsConcurrency()
        {
            var gateway = Gateway(new() { MinIntervalMs = 0, MaxConcurrency = 2 });
            var probe = new Probe();
            var release = new TaskCompletionSource();

            var calls = Enumerable.Range(1, 4).Select(i => Run(gateway, $"r{i}", probe.Fetch($"r{i}", i, release.Task))).ToList();
            await Eventually(() => probe.Started.Count == 2, "two run");
            await Settle();
            Assert.Equal(2, probe.Started.Count);
            Assert.Equal(new GatewayLaneStats(Queued: 2, Running: 2, Tokens: 30), gateway.GetStats(Source));

            release.SetResult();
            Assert.Equal(new[] { 1, 2, 3, 4 }, await Task.WhenAll(calls));
        }

        [Fact]
        public async Task ASourceThatIsNotConfigured_UsesTheDefault_AndRunsEverything()
        {
            var gateway = Gateway();
            var probe = new Probe();

            var results = await Task.WhenAll(Enumerable.Range(1, 10)
                .Select(i => gateway.RunAsync("never-heard-of-it", $"r{i}", GatewayPriority.Interactive, probe.Fetch($"r{i}", i), default)));

            Assert.Equal(Enumerable.Range(1, 10), results);
        }

        // ---- priority --------------------------------------------------------------------------

        [Fact]
        public async Task InteractiveRequests_GoBeforeBackgroundOnesThatWereQueuedEarlier()
        {
            var gateway = Gateway(new() { MinIntervalMs = 1000, Burst = 1, BackgroundReserve = 0 });
            var probe = new Probe();

            var first = Run(gateway, "x", probe.Fetch("x"));
            await Eventually(() => probe.Started.Count == 1, "x takes the only token");

            var queued = new[]
            {
                Run(gateway, "a", probe.Fetch("a"), GatewayPriority.Background),
                Run(gateway, "b", probe.Fetch("b"), GatewayPriority.Background),
                Run(gateway, "c", probe.Fetch("c"), GatewayPriority.Interactive),
            };

            _clock.Advance(TimeSpan.FromSeconds(1));
            await Eventually(() => probe.Started.Count == 2, "a token is back");
            Assert.Equal(["x", "c"], probe.Started);   // the interactive one jumped the queue

            _clock.Advance(TimeSpan.FromSeconds(1));
            await Eventually(() => probe.Started.Count == 3, "next token");
            _clock.Advance(TimeSpan.FromSeconds(1));
            await Eventually(() => probe.Started.Count == 4, "last token");
            Assert.Equal(["x", "c", "a", "b"], probe.Started);
            await Task.WhenAll(queued.Append(first));
        }

        [Fact]
        public async Task BackgroundRequests_LeaveTheReserveForInteractiveOnes()
        {
            var gateway = Gateway(new() { MinIntervalMs = 1000, Burst = 3, BackgroundReserve = 2 });
            var probe = new Probe();

            var b1 = Run(gateway, "b1", probe.Fetch("b1"), GatewayPriority.Background);
            var b2 = Run(gateway, "b2", probe.Fetch("b2"), GatewayPriority.Background);
            await Eventually(() => probe.Started.Count == 1, "background may use the bucket while it is full");
            await Settle();
            Assert.Equal(["b1"], probe.Started);   // b2 would dip into the reserve: it waits

            // A person arriving now is not behind the backfill: the reserve is still there.
            var i1 = Run(gateway, "i1", probe.Fetch("i1"));
            await Eventually(() => probe.Started.Contains("i1"), "interactive uses the reserve at once");

            _clock.Advance(TimeSpan.FromSeconds(1));
            await Settle();
            Assert.DoesNotContain("b2", probe.Started);   // the reserve is not refilled yet

            _clock.Advance(TimeSpan.FromSeconds(1));
            await Eventually(() => probe.Started.Contains("b2"), "once the reserve is whole again, background goes on");
            await Task.WhenAll(b1, b2, i1);
        }

        [Fact]
        public async Task AnInteractiveCallerJoiningAQueuedBackgroundRequest_PromotesIt_AndItRunsOnce()
        {
            var gateway = Gateway(new() { MinIntervalMs = 1000, Burst = 1, BackgroundReserve = 0 });
            var probe = new Probe();

            var x = Run(gateway, "x", probe.Fetch("x"));
            await Eventually(() => probe.Started.Count == 1, "x takes the only token");
            var z = Run(gateway, "z", probe.Fetch("z"), GatewayPriority.Background);
            var k = Run(gateway, "k", probe.Fetch("k-background", 1), GatewayPriority.Background);
            var kAgain = Run(gateway, "k", probe.Fetch("k-interactive", 2), GatewayPriority.Interactive);

            _clock.Advance(TimeSpan.FromSeconds(1));
            await Eventually(() => probe.Started.Count == 2, "a token is back");
            Assert.Equal(["x", "k-background"], probe.Started);   // promoted past z, and the first fetch is the one that runs

            _clock.Advance(TimeSpan.FromSeconds(1));
            await Eventually(() => probe.Started.Count == 3, "z gets the next token");
            Assert.Equal(new[] { 1, 1 }, await Task.WhenAll(k, kAgain));
            await Task.WhenAll(x, z);
        }

        // ---- timeout ---------------------------------------------------------------------------

        [Fact]
        public async Task ARequestThatNeverAnswers_TimesOut_AndFreesTheLane()
        {
            var gateway = Gateway(new() { MinIntervalMs = 0, MaxConcurrency = 1, RequestTimeoutSeconds = 5 });
            var probe = new Probe();

            var stuck = Run(gateway, "stuck", async token =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return 0;
            });
            var waiting = Run(gateway, "next", probe.Fetch("next", 3));
            await Eventually(() => gateway.GetStats(Source).Running == 1, "the stuck request is running");
            await Settle();
            Assert.Empty(probe.Started);   // concurrency 1: "next" is behind it

            _clock.Advance(TimeSpan.FromSeconds(5));

            await Assert.ThrowsAsync<TimeoutException>(() => stuck);
            Assert.Equal(3, await waiting);
        }

        // ---- configuration ---------------------------------------------------------------------

        [Fact]
        public async Task AReserveAsBigAsTheBucket_DoesNotStarveBackgroundRequestsForGood()
        {
            var gateway = Gateway(new() { MinIntervalMs = 1000, Burst = 2, BackgroundReserve = 50 });

            var result = await Run(gateway, "b", _ => Task.FromResult(1), GatewayPriority.Background);

            Assert.Equal(1, result);
        }

        [Fact]
        public async Task ChangingTheOptions_AppliesToTheNextDecision()
        {
            var gateway = Gateway(new() { MinIntervalMs = 1000, Burst = 1, BackgroundReserve = 0 });
            var probe = new Probe();
            var first = Run(gateway, "a", probe.Fetch("a"));
            var second = Run(gateway, "b", probe.Fetch("b"));
            await Eventually(() => probe.Started.Count == 1, "a ran, b is paced");

            _options.Sources[Source].MinIntervalMs = 0;   // as if the config file was edited
            var third = Run(gateway, "c", probe.Fetch("c"));

            await Eventually(() => probe.Started.Count == 3, "with pacing off, everything queued goes");
            await Task.WhenAll(first, second, third);
        }
    }
}
