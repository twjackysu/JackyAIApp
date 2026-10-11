namespace JackyAIApp.Server.Services.Portfolio.DrawdownBrake
{
    /// <summary>
    /// What the refresh has learned about the index data, kept apart from both sides so the
    /// refresh (which writes it) and the signal (which reports it and caches against it) need not
    /// know each other. Process memory only: it starts empty after a restart.
    /// </summary>
    public interface ITaiexDataStatus
    {
        /// <summary>When TWSE last gave an answer that could be read, whether the background
        /// round or a manual refresh asked. Null from start-up until the first such answer.</summary>
        DateTimeOffset? LastCheckedAt { get; }

        /// <summary>Changes every time <see cref="RecordCheck"/> is called, i.e. whenever the stored
        /// series or <see cref="LastCheckedAt"/> may have changed. A cached signal tagged with an
        /// older value is out of date, however recently it was built.</summary>
        long Version { get; }

        /// <summary>Records a readable answer from TWSE. Call it <b>after</b> that answer's days have
        /// been stored, so a signal built from the new version always includes them.</summary>
        void RecordCheck(DateTimeOffset at);
    }

    public sealed class TaiexDataStatus : ITaiexDataStatus
    {
        private long _lastCheckedTicks; // UTC ticks; 0 = never
        private long _version;

        public DateTimeOffset? LastCheckedAt
        {
            get
            {
                var ticks = Interlocked.Read(ref _lastCheckedTicks);
                return ticks == 0 ? null : new DateTimeOffset(ticks, TimeSpan.Zero);
            }
        }

        public long Version => Interlocked.Read(ref _version);

        public void RecordCheck(DateTimeOffset at)
        {
            // The time first, then the version: a reader that sees the new version also sees the
            // new time.
            Interlocked.Exchange(ref _lastCheckedTicks, at.UtcTicks);
            Interlocked.Increment(ref _version);
        }
    }
}
