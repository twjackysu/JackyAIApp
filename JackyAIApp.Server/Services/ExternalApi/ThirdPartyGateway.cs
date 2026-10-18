using Microsoft.Extensions.Options;

namespace JackyAIApp.Server.Services.ExternalApi
{
    /// <summary>Who is waiting on a request, which decides what it may take from the third party's budget.</summary>
    public enum GatewayPriority
    {
        /// <summary>A person is waiting for the page. Goes ahead of everything queued as background.</summary>
        Interactive,

        /// <summary>Nobody is waiting (a backfill). Leaves part of the budget for interactive requests.</summary>
        Background,
    }

    /// <summary>A point-in-time look at one source's lane, for diagnostics and tests.</summary>
    public readonly record struct GatewayLaneStats(int Queued, int Running, double Tokens);

    /// <summary>
    /// The one door to a third-party API. Every request to a source goes through
    /// <see cref="RunAsync{T}"/>, which
    /// <list type="bullet">
    /// <item>shares one in-flight request between everyone asking for the same thing (the way RTK
    /// Query shares one fetch between components subscribed to the same query),</item>
    /// <item>queues what is not allowed to run yet, interactive requests before background ones,</item>
    /// <item>paces the source to a sustained rate with a burst allowance (a token bucket), and</item>
    /// <item>bounds how many run at once and how long each may take.</item>
    /// </list>
    /// It does not cache: callers keep their own cache (memory, the database) in front of it and
    /// come here only on a miss, so a finished request is never kept in memory by the gateway.
    /// </summary>
    public interface IThirdPartyGateway
    {
        /// <summary>
        /// Runs <paramref name="fetch"/> for <paramref name="source"/> and returns its result. If the
        /// same <paramref name="key"/> (and result type) is already queued or running, joins it instead
        /// and returns that result — the <paramref name="fetch"/> passed by the later caller is not run.
        /// An interactive caller joining a queued background request promotes it.
        /// </summary>
        /// <remarks>
        /// <paramref name="fetch"/> is shared by every caller, so it must not depend on any one
        /// caller's <see cref="CancellationToken"/>; it gets its own token, cancelled by the request
        /// timeout. <paramref name="ct"/> only stops this caller from waiting — the request itself
        /// carries on for the others, and its result is lost if there are none. A failure reaches every
        /// caller that was waiting and is not remembered: the next call tries again.
        /// </remarks>
        Task<T> RunAsync<T>(
            string source, string key, GatewayPriority priority,
            Func<CancellationToken, Task<T>> fetch, CancellationToken ct);

        GatewayLaneStats GetStats(string source);
    }

    public sealed class ThirdPartyGateway(
        IOptionsMonitor<ThirdPartyGatewayOptions> options,
        TimeProvider timeProvider,
        ILogger<ThirdPartyGateway> logger) : IThirdPartyGateway
    {
        // Floating-point slack when comparing a refilled token count against what a request needs.
        private const double TokenEpsilon = 1e-9;

        private readonly Dictionary<string, Lane> _lanes = new(StringComparer.OrdinalIgnoreCase);

        public Task<T> RunAsync<T>(
            string source, string key, GatewayPriority priority,
            Func<CancellationToken, Task<T>> fetch, CancellationToken ct)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(source);
            ArgumentException.ThrowIfNullOrWhiteSpace(key);
            ArgumentNullException.ThrowIfNull(fetch);

            var lane = GetLane(source);
            // The result type is part of the identity, so two callers that happen to pick the same key
            // for different things can never be handed each other's result.
            var flightKey = $"{typeof(T).FullName}|{key}";

            Flight flight;
            lock (lane.Gate)
            {
                if (lane.Flights.TryGetValue(flightKey, out var existing))
                {
                    flight = existing;
                    if (priority == GatewayPriority.Interactive && flight is { Started: false, Priority: GatewayPriority.Background })
                    {
                        lane.Background.Remove(flight.Node!);
                        flight.Priority = GatewayPriority.Interactive;
                        flight.Node = lane.Interactive.AddLast(flight);
                        logger.LogDebug("{Source} {Key} promoted to interactive", source, key);
                    }
                }
                else
                {
                    flight = new Flight(flightKey, priority, async token => await fetch(token).ConfigureAwait(false));
                    flight.Node = (priority == GatewayPriority.Interactive ? lane.Interactive : lane.Background).AddLast(flight);
                    lane.Flights[flightKey] = flight;
                }
            }

            Pump(source, lane);
            return WaitAsync<T>(flight, ct);
        }

        public GatewayLaneStats GetStats(string source)
        {
            var lane = GetLane(source);
            lock (lane.Gate)
            {
                Refill(lane, options.CurrentValue.For(source));
                return new GatewayLaneStats(lane.Interactive.Count + lane.Background.Count, lane.Running, lane.Tokens);
            }
        }

        private static async Task<T> WaitAsync<T>(Flight flight, CancellationToken ct) =>
            (T)(await flight.Completion.Task.WaitAsync(ct).ConfigureAwait(false))!;

        private Lane GetLane(string source)
        {
            lock (_lanes)
            {
                if (_lanes.TryGetValue(source, out var lane)) return lane;
                lane = new Lane(options.CurrentValue.For(source).Burst, timeProvider.GetTimestamp());
                _lanes[source] = lane;
                return lane;
            }
        }

        /// <summary>
        /// Starts as many queued requests as the lane may run now, and if the next one has to wait
        /// for tokens, arranges to be called again when it can go. Called whenever something changes:
        /// a request arrives, one finishes (a slot frees up), or a wait for tokens is over.
        /// </summary>
        private void Pump(string source, Lane lane)
        {
            var config = options.CurrentValue.For(source);
            var maxConcurrency = Math.Max(1, config.MaxConcurrency);
            var pace = config.MinIntervalMs > 0;
            var burst = Math.Max(1, config.Burst);
            // A reserve of the whole bucket would starve background requests for good.
            var reserve = Math.Clamp(config.BackgroundReserve, 0, burst - 1);

            var toStart = new List<Flight>();
            lock (lane.Gate)
            {
                while (lane.Running + toStart.Count < maxConcurrency)
                {
                    var next = lane.Interactive.First?.Value ?? lane.Background.First?.Value;
                    if (next is null) break;

                    if (pace)
                    {
                        Refill(lane, config);
                        var needed = next.Priority == GatewayPriority.Interactive ? 1 : reserve + 1;
                        if (lane.Tokens < needed - TokenEpsilon)
                        {
                            var wait = TimeSpan.FromMilliseconds(Math.Ceiling((needed - lane.Tokens) * config.MinIntervalMs));
                            ScheduleWake(source, lane, wait);
                            break;
                        }
                        lane.Tokens -= 1;
                    }

                    (next.Priority == GatewayPriority.Interactive ? lane.Interactive : lane.Background).Remove(next.Node!);
                    next.Node = null;
                    next.Started = true;
                    toStart.Add(next);
                }
                lane.Running += toStart.Count;
            }

            foreach (var flight in toStart) _ = RunFlightAsync(source, lane, flight, config);
        }

        private async Task RunFlightAsync(string source, Lane lane, Flight flight, ThirdPartySourceOptions config)
        {
            object? result = null;
            Exception? failure = null;
            try
            {
                // Off the caller's thread, so a fetch that blocks before its first await cannot stall
                // the pump or the request that happened to trigger it.
                await Task.Yield();
                using var timeout = config.RequestTimeoutSeconds > 0
                    ? new CancellationTokenSource(TimeSpan.FromSeconds(config.RequestTimeoutSeconds), timeProvider)
                    : new CancellationTokenSource();
                try
                {
                    result = await flight.Fetch(timeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (timeout.IsCancellationRequested)
                {
                    failure = new TimeoutException($"{source} did not answer within {config.RequestTimeoutSeconds} s.");
                }
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                // Forget the request before telling anyone, so a call made after this moment starts a
                // fresh one and never joins a request that is already over.
                lock (lane.Gate)
                {
                    lane.Flights.Remove(flight.Key);
                    lane.Running--;
                }
                if (failure is null) flight.Completion.TrySetResult(result);
                else flight.Completion.TrySetException(failure);
                Pump(source, lane);
            }
        }

        /// <summary>Adds the tokens earned since the last look. Call with the lane's gate held.</summary>
        private void Refill(Lane lane, ThirdPartySourceOptions config)
        {
            var burst = Math.Max(1, config.Burst);
            if (config.MinIntervalMs <= 0)
            {
                lane.Tokens = burst;
                return;
            }
            var now = timeProvider.GetTimestamp();
            var elapsedMs = timeProvider.GetElapsedTime(lane.LastRefill, now).TotalMilliseconds;
            lane.LastRefill = now;
            lane.Tokens = Math.Min(burst, lane.Tokens + Math.Max(0, elapsedMs) / config.MinIntervalMs);
        }

        private void ScheduleWake(string source, Lane lane, TimeSpan delay)
        {
            var due = timeProvider.GetTimestamp() + (long)(delay.TotalSeconds * timeProvider.TimestampFrequency);
            // An earlier wake already pending covers this one: it re-evaluates and schedules again.
            if (lane.WakeTimer is not null && lane.WakeAt <= due) return;

            lane.WakeTimer?.Dispose();
            lane.WakeAt = due;
            lane.WakeTimer = timeProvider.CreateTimer(_ =>
            {
                lock (lane.Gate)
                {
                    lane.WakeTimer?.Dispose();
                    lane.WakeTimer = null;
                    lane.WakeAt = 0;
                }
                Pump(source, lane);
            }, null, delay, Timeout.InfiniteTimeSpan);
        }

        private sealed class Flight(string key, GatewayPriority priority, Func<CancellationToken, Task<object?>> fetch)
        {
            public string Key { get; } = key;
            public GatewayPriority Priority { get; set; } = priority;
            public Func<CancellationToken, Task<object?>> Fetch { get; } = fetch;
            public TaskCompletionSource<object?> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

            /// <summary>Its place in a queue, while it waits there.</summary>
            public LinkedListNode<Flight>? Node { get; set; }

            public bool Started { get; set; }
        }

        private sealed class Lane
        {
            public Lane(int burst, long now)
            {
                Tokens = Math.Max(1, burst);
                LastRefill = now;
            }

            public object Gate { get; } = new();
            public Dictionary<string, Flight> Flights { get; } = [];
            public LinkedList<Flight> Interactive { get; } = new();
            public LinkedList<Flight> Background { get; } = new();
            public int Running { get; set; }
            public double Tokens { get; set; }
            public long LastRefill { get; set; }
            public ITimer? WakeTimer { get; set; }
            public long WakeAt { get; set; }
        }
    }
}
