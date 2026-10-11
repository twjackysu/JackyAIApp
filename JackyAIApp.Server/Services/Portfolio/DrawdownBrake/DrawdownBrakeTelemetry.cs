using System.Diagnostics.Metrics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using JackyAIApp.Server.DTO.Portfolio;

namespace JackyAIApp.Server.Services.Portfolio.DrawdownBrake
{
    /// <summary>How a <c>GET drawdown-brake</c> ended: the <c>outcome</c> tag of
    /// <c>drawdownbrake.views</c>, and the word in the per-view log line.</summary>
    public enum DrawdownBrakeViewOutcome
    {
        /// <summary>A signal was served and the data is current.</summary>
        Ok,

        /// <summary>Not enough history stored yet, so no signal (<see cref="DrawdownBrakeDto.IsInitializing"/>).</summary>
        Initializing,

        /// <summary>A signal was served, but today's close is overdue (<see cref="DrawdownBrakeDto.IsStale"/>).</summary>
        Stale,

        /// <summary>The caller is outside <c>Features:DrawdownBrake:Audience</c>.</summary>
        Forbidden,
    }

    /// <summary>
    /// What the Boss can read off Application Insights about 回落煞車 (TASK-040): how often it is
    /// looked at, by how many distinct people, how often the shared signal had to be rebuilt and
    /// how long that took, and how the manual refresh button fares. The HTTP request and the
    /// outbound TWSE calls are already collected automatically; this adds only what they cannot say.
    ///
    /// <para><b>Nothing here may carry a holding, a quantity, an amount, an email or a Google ID.</b>
    /// This feature's server side never receives the first three (advice is computed in the
    /// browser); the caller's ID is turned into <see cref="UserKey"/> before it goes anywhere near
    /// a log line, and is never a metric tag.</para>
    ///
    /// <para>The instruments live on a <see cref="Meter"/> this object owns, so each instance has
    /// its own (tests rely on that to listen to one instance only); production has one, as a
    /// singleton. The Meter's name must be registered with the OpenTelemetry meter provider
    /// (<c>Program.cs</c>) or none of it reaches Azure Monitor.</para>
    /// </summary>
    public sealed class DrawdownBrakeTelemetry : IDisposable
    {
        public const string MeterName = "JackyAIApp.DrawdownBrake";

        private readonly Meter _meter = new(MeterName);

        private readonly Counter<long> _views;
        private readonly Counter<long> _refreshes;
        private readonly Histogram<double> _computeMs;
        private readonly ILogger<DrawdownBrakeTelemetry> _logger;
        private readonly TimeProvider _time;

        /// <summary>Made up when the process starts and kept nowhere, so a <see cref="UserKey"/> cannot
        /// be recomputed from a Google ID by anyone, this process included, once it has restarted.</summary>
        private readonly string _salt = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));

        public DrawdownBrakeTelemetry(ILogger<DrawdownBrakeTelemetry> logger, TimeProvider? timeProvider = null)
        {
            _logger = logger;
            _time = timeProvider ?? TimeProvider.System;
            _views = _meter.CreateCounter<long>("drawdownbrake.views",
                description: "GET drawdown-brake calls, by outcome and by whether the shared signal was already built (cache=hit) or this call had to build it (cache=miss).");
            _refreshes = _meter.CreateCounter<long>("drawdownbrake.refresh",
                description: "POST drawdown-brake/refresh calls by result.");
            _computeMs = _meter.CreateHistogram<double>("drawdownbrake.compute.duration", unit: "ms",
                description: "Reading the stored series and recomputing the signal; only recorded when the signal was actually built.");
        }

        /// <summary>For tests, which listen to one instance's instruments only.</summary>
        internal Meter Meter => _meter;

        /// <summary>The <c>outcome</c> of serving <paramref name="signal"/>.</summary>
        public static DrawdownBrakeViewOutcome OutcomeOf(DrawdownBrakeDto signal) =>
            signal.IsInitializing ? DrawdownBrakeViewOutcome.Initializing
            : signal.IsStale ? DrawdownBrakeViewOutcome.Stale
            : DrawdownBrakeViewOutcome.Ok;

        /// <summary>
        /// One <c>GET drawdown-brake</c>. <paramref name="cacheHit"/> is whether the caller was handed
        /// a signal someone else had built (<c>true</c>) or built it itself (<c>false</c>); null when
        /// no signal was looked up at all (<see cref="DrawdownBrakeViewOutcome.Forbidden"/>), in which
        /// case the metric has no <c>cache</c> tag rather than a made-up one.
        /// </summary>
        public void RecordView(string userId, DrawdownBrakeViewOutcome outcome, bool? cacheHit)
        {
            var outcomeTag = OutcomeTag(outcome);
            if (cacheHit is { } hit)
                _views.Add(1, Tag("outcome", outcomeTag), Tag("cache", hit ? "hit" : "miss"));
            else
                _views.Add(1, Tag("outcome", outcomeTag));

            // The distinct-people count: dcount(UserKey) over these lines (see ARCHITECTURE.md).
            _logger.LogInformation("DrawdownBrake view {Outcome} {UserKey}", outcomeTag, UserKey(userId));
        }

        /// <summary>One <c>POST drawdown-brake/refresh</c> and how it ended.</summary>
        public void RecordRefresh(DrawdownBrakeRefreshResult result) =>
            _refreshes.Add(1, Tag("result", ResultTag(result)));

        /// <summary>One build of the shared signal (a cache miss).</summary>
        public void RecordCompute(TimeSpan elapsed) => _computeMs.Record(elapsed.TotalMilliseconds);

        /// <summary>
        /// A stand-in for <paramref name="userId"/> that lets one day's distinct people be counted and
        /// nothing else: the first 12 hex digits of SHA-256(<c>userId:UTC-date:salt</c>). The date makes
        /// it change daily (no following a person across days) and the per-process salt makes it
        /// impossible to recompute from a known Google ID; a restart therefore counts the same person
        /// again that day, which is acceptable for something only meant to give a feel for usage.
        /// </summary>
        internal string UserKey(string userId)
        {
            var day = _time.GetUtcNow().UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{userId}:{day}:{_salt}"));
            return Convert.ToHexString(hash, 0, 6).ToLowerInvariant();
        }

        private static KeyValuePair<string, object?> Tag(string name, string value) => new(name, value);

        private static string OutcomeTag(DrawdownBrakeViewOutcome outcome) => outcome switch
        {
            DrawdownBrakeViewOutcome.Ok => "ok",
            DrawdownBrakeViewOutcome.Initializing => "initializing",
            DrawdownBrakeViewOutcome.Stale => "stale",
            DrawdownBrakeViewOutcome.Forbidden => "forbidden",
            // A member added later still records, under its own name; a test lists every member so
            // the table above cannot fall behind. Telemetry must never fail the request.
            _ => outcome.ToString().ToLowerInvariant(),
        };

        private static string ResultTag(DrawdownBrakeRefreshResult result) => result switch
        {
            DrawdownBrakeRefreshResult.Updated => "updated",
            DrawdownBrakeRefreshResult.NoNewData => "no_new_data",
            DrawdownBrakeRefreshResult.SourceUnavailable => "source_unavailable",
            DrawdownBrakeRefreshResult.Throttled => "throttled",
            _ => result.ToString().ToLowerInvariant(), // see OutcomeTag
        };

        public void Dispose() => _meter.Dispose();
    }
}
