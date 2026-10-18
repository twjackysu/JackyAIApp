namespace JackyAIApp.Server.Tests.TestSupport
{
    /// <summary>
    /// A clock that only moves when a test says so, with timers that fire during <see cref="Advance"/>.
    /// Covers what <c>Task.Delay(.., TimeProvider)</c>, <c>CancellationTokenSource(.., TimeProvider)</c>
    /// and a hand-made <c>CreateTimer</c> need, so time-dependent code can be tested without sleeping.
    /// </summary>
    public sealed class ManualTimeProvider : TimeProvider
    {
        private readonly object _gate = new();
        private readonly List<ManualTimer> _timers = [];
        private DateTimeOffset _now = new(2026, 10, 18, 9, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() { lock (_gate) return _now; }
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() { lock (_gate) return _now.UtcTicks; }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(this, callback, state);
            timer.Change(dueTime, period);
            return timer;
        }

        /// <summary>Moves the clock forward, firing every timer that falls due on the way, in order.</summary>
        public void Advance(TimeSpan by)
        {
            DateTimeOffset target;
            lock (_gate) target = _now + by;

            while (true)
            {
                ManualTimer? next;
                lock (_gate)
                {
                    next = _timers.Where(t => t.Due is not null && t.Due <= target).OrderBy(t => t.Due).FirstOrDefault();
                    if (next is null)
                    {
                        _now = target;
                        return;
                    }
                    if (next.Due > _now) _now = next.Due.Value;
                    next.Due = next.Period > TimeSpan.Zero ? _now + next.Period : null;
                }
                next.Invoke();   // outside the lock: the callback may schedule more timers
            }
        }

        public int PendingTimers { get { lock (_gate) return _timers.Count(t => t.Due is not null); } }

        private sealed class ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state) : ITimer
        {
            public DateTimeOffset? Due { get; set; }
            public TimeSpan Period { get; private set; }

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                lock (owner._gate)
                {
                    Period = period == Timeout.InfiniteTimeSpan ? TimeSpan.Zero : period;
                    Due = dueTime == Timeout.InfiniteTimeSpan ? null : owner._now + dueTime;
                    if (!owner._timers.Contains(this)) owner._timers.Add(this);
                }
                return true;
            }

            public void Invoke() => callback(state);

            public void Dispose()
            {
                lock (owner._gate) owner._timers.Remove(this);
            }

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }
}
