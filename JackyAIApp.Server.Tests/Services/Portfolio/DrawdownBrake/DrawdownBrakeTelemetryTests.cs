using System.Diagnostics.Metrics;
using System.Text.RegularExpressions;
using JackyAIApp.Server.DTO.Portfolio;
using JackyAIApp.Server.Services.Portfolio.DrawdownBrake;
using JackyAIApp.Server.Tests.TestHelpers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace JackyAIApp.Server.Tests.Services.Portfolio.DrawdownBrake
{
    /// <summary>TASK-040: the meter, its three instruments, the per-view log line and the pseudonym in it.</summary>
    public class DrawdownBrakeTelemetryTests : IDisposable
    {
        // Not hex, so it can never turn up inside a UserKey by chance.
        private const string UserId = "google-oauth-id-1234567890";

        private readonly RecordingLogger<DrawdownBrakeTelemetry> _log = new();
        private readonly FakeTime _time = new() { Now = new DateTimeOffset(2026, 10, 8, 3, 0, 0, TimeSpan.Zero) };
        private readonly DrawdownBrakeTelemetry _telemetry;
        private readonly MeterCapture _metrics;

        public DrawdownBrakeTelemetryTests()
        {
            _telemetry = new DrawdownBrakeTelemetry(_log, _time);
            _metrics = new MeterCapture(_telemetry.Meter);
        }

        public void Dispose()
        {
            _metrics.Dispose();
            _telemetry.Dispose();
        }

        // ── the meter and its instruments ───────────────────────────────────

        [Fact]
        public void TheMeter_IsTheOneProgramCsRegisters()
        {
            // Program.cs passes this constant to AddMeter; a rename here that missed that line would
            // silently send nothing, so the documented name is pinned.
            Assert.Equal("JackyAIApp.DrawdownBrake", DrawdownBrakeTelemetry.MeterName);
            Assert.Equal(DrawdownBrakeTelemetry.MeterName, _telemetry.Meter.Name);
        }

        [Fact]
        public void ThereAreExactlyThreeInstruments_NamedAndTypedAsDocumented()
        {
            var instruments = _metrics.Instruments.Select(i => (i.Name, i.Type, i.Unit)).ToList();

            Assert.Equal(3, instruments.Count);
            Assert.Contains(("drawdownbrake.views", typeof(Counter<long>), (string?)null), instruments);
            Assert.Contains(("drawdownbrake.refresh", typeof(Counter<long>), (string?)null), instruments);
            Assert.Contains(("drawdownbrake.compute.duration", typeof(Histogram<double>), (string?)"ms"), instruments);
        }

        // ── drawdownbrake.views ─────────────────────────────────────────────

        [Theory]
        [InlineData(DrawdownBrakeViewOutcome.Ok, true, "ok", "hit")]
        [InlineData(DrawdownBrakeViewOutcome.Ok, false, "ok", "miss")]
        [InlineData(DrawdownBrakeViewOutcome.Initializing, true, "initializing", "hit")]
        [InlineData(DrawdownBrakeViewOutcome.Initializing, false, "initializing", "miss")]
        [InlineData(DrawdownBrakeViewOutcome.Stale, true, "stale", "hit")]
        [InlineData(DrawdownBrakeViewOutcome.Stale, false, "stale", "miss")]
        public void AView_CountsOnce_TaggedWithItsOutcomeAndCacheStatus(DrawdownBrakeViewOutcome outcome, bool cacheHit, string outcomeTag, string cacheTag)
        {
            _telemetry.RecordView(UserId, outcome, cacheHit);

            var m = Assert.Single(_metrics.All);
            Assert.Equal("drawdownbrake.views", m.Instrument);
            Assert.Equal(1, m.Value);
            Assert.Equal(new Dictionary<string, string?> { ["outcome"] = outcomeTag, ["cache"] = cacheTag }, m.Tags);
        }

        [Fact]
        public void AForbiddenView_HasNoCacheTag_ForNothingWasLookedUp()
        {
            _telemetry.RecordView(UserId, DrawdownBrakeViewOutcome.Forbidden, cacheHit: null);

            var m = Assert.Single(_metrics.Of("drawdownbrake.views"));
            Assert.Equal(new Dictionary<string, string?> { ["outcome"] = "forbidden" }, m.Tags);
        }

        [Fact]
        public void EveryViewOutcome_HasADocumentedTag()
        {
            // The documented set (TASK-040). A new member must be added here on purpose.
            var documented = new Dictionary<DrawdownBrakeViewOutcome, string>
            {
                [DrawdownBrakeViewOutcome.Ok] = "ok",
                [DrawdownBrakeViewOutcome.Initializing] = "initializing",
                [DrawdownBrakeViewOutcome.Stale] = "stale",
                [DrawdownBrakeViewOutcome.Forbidden] = "forbidden",
            };

            foreach (var outcome in Enum.GetValues<DrawdownBrakeViewOutcome>())
            {
                Assert.True(documented.ContainsKey(outcome), $"{outcome} has no documented tag");
                _telemetry.RecordView(UserId, outcome, cacheHit: null);
            }

            Assert.Equal(documented.Values.Order(), _metrics.Of("drawdownbrake.views").Select(m => m.Tags["outcome"]!).Order());
        }

        [Fact]
        public void TheOutcome_IsInitializing_Stale_OrOk_InThatOrder()
        {
            Assert.Equal(DrawdownBrakeViewOutcome.Ok, DrawdownBrakeTelemetry.OutcomeOf(new DrawdownBrakeDto()));
            Assert.Equal(DrawdownBrakeViewOutcome.Stale, DrawdownBrakeTelemetry.OutcomeOf(new DrawdownBrakeDto { IsStale = true }));
            Assert.Equal(DrawdownBrakeViewOutcome.Initializing, DrawdownBrakeTelemetry.OutcomeOf(new DrawdownBrakeDto { IsInitializing = true }));
            Assert.Equal(DrawdownBrakeViewOutcome.Initializing, DrawdownBrakeTelemetry.OutcomeOf(new DrawdownBrakeDto { IsInitializing = true, IsStale = true }));
        }

        // ── drawdownbrake.refresh ───────────────────────────────────────────

        [Fact]
        public void EveryRefreshResult_IsCountedUnderItsDocumentedTag()
        {
            var documented = new Dictionary<DrawdownBrakeRefreshResult, string>
            {
                [DrawdownBrakeRefreshResult.Updated] = "updated",
                [DrawdownBrakeRefreshResult.NoNewData] = "no_new_data",
                [DrawdownBrakeRefreshResult.SourceUnavailable] = "source_unavailable",
                [DrawdownBrakeRefreshResult.Throttled] = "throttled",
            };

            foreach (var result in Enum.GetValues<DrawdownBrakeRefreshResult>())
            {
                Assert.True(documented.ContainsKey(result), $"{result} has no documented tag");
                _telemetry.RecordRefresh(result);
            }

            var recorded = _metrics.Of("drawdownbrake.refresh");
            Assert.Equal(documented.Count, recorded.Count);
            Assert.All(recorded, m =>
            {
                Assert.Equal(1, m.Value);
                Assert.Equal(["result"], m.Tags.Keys);
            });
            Assert.Equal(documented.Values.Order(), recorded.Select(m => m.Tags["result"]!).Order());
        }

        [Fact]
        public void ARefresh_IsNotAView()
        {
            _telemetry.RecordRefresh(DrawdownBrakeRefreshResult.Updated);

            Assert.Empty(_metrics.Of("drawdownbrake.views"));
            Assert.Empty(_log.Entries);
        }

        // ── drawdownbrake.compute.duration ──────────────────────────────────

        [Fact]
        public void ABuild_IsRecordedInMilliseconds_WithNoTags()
        {
            _telemetry.RecordCompute(TimeSpan.FromMilliseconds(12.5));

            var m = Assert.Single(_metrics.All);
            Assert.Equal("drawdownbrake.compute.duration", m.Instrument);
            Assert.Equal(12.5, m.Value);
            Assert.Empty(m.Tags);
        }

        // ── the per-view log line and UserKey ───────────────────────────────

        [Fact]
        public void AView_LogsOneInformationLine_WithTheOutcomeAndTheUserKey()
        {
            _telemetry.RecordView(UserId, DrawdownBrakeViewOutcome.Stale, cacheHit: true);

            var entry = Assert.Single(_log.Entries);
            var key = _telemetry.UserKey(UserId);
            Assert.Equal(LogLevel.Information, entry.Level);
            // The KQL in ARCHITECTURE.md finds these by their prefix and reads the two properties.
            Assert.Equal($"DrawdownBrake view stale {key}", entry.Message);
            Assert.Equal("stale", entry.Properties["Outcome"]);
            Assert.Equal(key, entry.Properties["UserKey"]);
        }

        [Fact]
        public void EveryOutcome_IsLogged_TheForbiddenOneIncluded()
        {
            foreach (var outcome in Enum.GetValues<DrawdownBrakeViewOutcome>())
                _telemetry.RecordView(UserId, outcome, cacheHit: null);

            Assert.Equal(
                ["ok", "initializing", "stale", "forbidden"],
                _log.Entries.Select(e => (string)e.Properties["Outcome"]!));
        }

        [Fact]
        public void NeitherTheLogNorTheMetricsCarryTheUserId()
        {
            _telemetry.RecordView(UserId, DrawdownBrakeViewOutcome.Ok, cacheHit: false);
            _telemetry.RecordView(UserId, DrawdownBrakeViewOutcome.Forbidden, cacheHit: null);

            foreach (var entry in _log.Entries)
            {
                Assert.DoesNotContain(UserId, entry.Message);
                Assert.DoesNotContain("1234567890", entry.Message);
                Assert.All(entry.Properties, p => Assert.DoesNotContain(UserId, p.Value?.ToString() ?? "", StringComparison.Ordinal));
            }
            Assert.All(_metrics.All, m => Assert.All(m.Tags, t => Assert.DoesNotContain("1234567890", t.Value ?? "")));
        }

        [Fact]
        public void TheUserKey_IsTwelveLowercaseHexDigits()
        {
            Assert.Matches(new Regex("^[0-9a-f]{12}$"), _telemetry.UserKey(UserId));
        }

        [Fact]
        public void TheSamePerson_OnTheSameDay_HasTheSameKey_AllDay()
        {
            var morning = _telemetry.UserKey(UserId);
            _time.Now = new DateTimeOffset(2026, 10, 8, 23, 59, 59, TimeSpan.Zero);
            var night = _telemetry.UserKey(UserId);

            Assert.Equal(morning, night);
        }

        [Fact]
        public void TheSamePerson_OnAnotherDay_HasAnotherKey()
        {
            _time.Now = new DateTimeOffset(2026, 10, 8, 23, 59, 59, TimeSpan.Zero);
            var before = _telemetry.UserKey(UserId);
            _time.Now = new DateTimeOffset(2026, 10, 9, 0, 0, 0, TimeSpan.Zero); // the next UTC day, by one second
            var after = _telemetry.UserKey(UserId);

            Assert.NotEqual(before, after);
        }

        [Fact]
        public void TheDay_IsUtc_NotTaipei()
        {
            // 15:59 UTC is 23:59 in Taipei, 16:00 UTC is already the next day there. Same UTC day, same key.
            _time.Now = new DateTimeOffset(2026, 10, 8, 15, 59, 0, TimeSpan.Zero);
            var a = _telemetry.UserKey(UserId);
            _time.Now = new DateTimeOffset(2026, 10, 8, 16, 0, 0, TimeSpan.Zero);
            var b = _telemetry.UserKey(UserId);

            Assert.Equal(a, b);
        }

        [Fact]
        public void TwoPeople_HaveTwoKeys()
        {
            Assert.NotEqual(_telemetry.UserKey("google-oauth-id-aaaa"), _telemetry.UserKey("google-oauth-id-bbbb"));
        }

        [Fact]
        public void ANewProcess_CannotReproduceAnotherOnesKey()
        {
            // The salt is made up at start-up and never stored: the same person, the same day, a
            // different process — a different key. (Which is also why a restart counts them twice.)
            using var restarted = new DrawdownBrakeTelemetry(new RecordingLogger<DrawdownBrakeTelemetry>(), _time);

            Assert.NotEqual(_telemetry.UserKey(UserId), restarted.UserKey(UserId));
        }

        [Fact]
        public void ThePerViewLine_IsNotFilteredOutByTheAppsLogLevels()
        {
            // Application Insights only sees what the ILogger filter lets through. This runs the
            // app's real appsettings.json "Logging" section against this category: if someone raises
            // the default above Information (or adds a JackyAIApp.* rule), the distinct-user count
            // would silently drop to zero, so it fails here instead.
            var config = new ConfigurationBuilder()
                .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"), optional: false)
                .Build();
            var recorded = new RecordingLogger<DrawdownBrakeTelemetry>();
            using var factory = LoggerFactory.Create(b => b
                .AddConfiguration(config.GetSection("Logging"))
                .AddProvider(new SingleLoggerProvider(recorded)));

            using var telemetry = new DrawdownBrakeTelemetry(factory.CreateLogger<DrawdownBrakeTelemetry>(), _time);
            telemetry.RecordView(UserId, DrawdownBrakeViewOutcome.Ok, cacheHit: true);

            Assert.Single(recorded.Entries);
        }

        private sealed class SingleLoggerProvider(ILogger logger) : ILoggerProvider
        {
            public ILogger CreateLogger(string categoryName) => logger;
            public void Dispose() { }
        }

        private sealed class FakeTime : TimeProvider
        {
            public DateTimeOffset Now { get; set; }
            public override DateTimeOffset GetUtcNow() => Now;
        }
    }
}
