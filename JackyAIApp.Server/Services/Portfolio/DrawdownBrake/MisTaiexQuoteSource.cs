namespace JackyAIApp.Server.Services.Portfolio.DrawdownBrake
{
    /// <param name="Quote">What MIS said; null if it gave nothing usable (that failure is held too).</param>
    /// <param name="FetchedAt">When the request was answered.</param>
    public sealed record MisQuoteSnapshot(MisIndexQuote? Quote, DateTimeOffset FetchedAt);

    public interface IMisTaiexQuoteSource
    {
        /// <summary>
        /// The site's current reading of the index, asking MIS only if what is held is
        /// <paramref name="maxAge"/> old or older (or nothing is held). However many callers ask,
        /// MIS is asked once per <paramref name="maxAge"/>: whoever waits on a request in progress is
        /// handed its answer. An answer that was a failure is held as well, so a MIS outage is not
        /// retried by every viewer.
        /// </summary>
        Task<MisQuoteSnapshot> GetAsync(TimeSpan maxAge, CancellationToken ct = default);
    }

    /// <summary>
    /// The one shared copy of the latest MIS reading (TASK-038), and the thing that keeps the load on
    /// MIS independent of the number of users: viewers take the copy up to
    /// <see cref="Common.CacheTtl.Portfolio.DrawdownBrakeEstimateQuote"/> old, the refresh button
    /// anything older than <see cref="Common.CacheTtl.Portfolio.DrawdownBrakeEstimateRefresh"/>, so
    /// the shortest gap between two requests, however hard it is pressed, is the latter.
    ///
    /// <para>Singleton. The held snapshot is an immutable object replaced whole, so a caller
    /// comparing it with an earlier one by reference learns whether MIS was asked again in between.</para>
    /// </summary>
    public class MisTaiexQuoteSource(IMisTaiexClient client, TimeProvider? timeProvider = null) : IMisTaiexQuoteSource
    {
        private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

        /// <summary>Held by the one caller on its way to MIS. Not a per-caller token's business: a caller
        /// that gives up simply lets the next one ask.</summary>
        private readonly SemaphoreSlim _gate = new(1, 1);

        private volatile MisQuoteSnapshot? _held;

        public async Task<MisQuoteSnapshot> GetAsync(TimeSpan maxAge, CancellationToken ct = default)
        {
            if (Fresh(maxAge) is { } held) return held;

            await _gate.WaitAsync(ct);
            try
            {
                // Whoever held the gate may have just asked.
                if (Fresh(maxAge) is { } asked) return asked;

                var quote = await client.GetAsync(ct);
                var snapshot = new MisQuoteSnapshot(quote, _time.GetUtcNow());
                _held = snapshot;
                return snapshot;
            }
            finally
            {
                _gate.Release();
            }
        }

        private MisQuoteSnapshot? Fresh(TimeSpan maxAge) =>
            _held is { } snapshot && _time.GetUtcNow() - snapshot.FetchedAt < maxAge ? snapshot : null;
    }
}
