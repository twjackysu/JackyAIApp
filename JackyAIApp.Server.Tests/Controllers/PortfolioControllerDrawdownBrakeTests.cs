using JackyAIApp.Server.Common;
using JackyAIApp.Server.Controllers;
using JackyAIApp.Server.DTO.Portfolio;
using JackyAIApp.Server.Services;
using JackyAIApp.Server.Services.Portfolio;
using JackyAIApp.Server.Services.Portfolio.DrawdownBrake;
using JackyAIApp.Server.Tests.TestHelpers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Moq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace JackyAIApp.Server.Tests.Controllers
{
    /// <summary>GET api/portfolio/drawdown-brake and POST …/refresh: who may call them, and what they hand back.</summary>
    public class PortfolioControllerDrawdownBrakeTests : IDisposable
    {
        private readonly Mock<IMyResponseFactory> _responseFactory = new();
        private readonly Mock<ICurrentUser> _currentUser = new();
        private readonly Mock<IDrawdownBrakeService> _brake = new();
        private readonly Mock<IDrawdownBrakeAccess> _access = new();
        private readonly Mock<ITaiexTotalReturnRefresher> _refresher = new();
        private readonly RecordingLogger<DrawdownBrakeTelemetry> _log = new();
        private readonly DrawdownBrakeTelemetry _telemetry;
        private readonly MeterCapture _metrics;
        private readonly PortfolioController _controller;

        public void Dispose()
        {
            _metrics.Dispose();
            _telemetry.Dispose();
        }

        public PortfolioControllerDrawdownBrakeTests()
        {
            // A clock that does not move, so the UserKey the controller logged is the one computed here.
            _telemetry = new DrawdownBrakeTelemetry(_log, new FixedTime(new DateTimeOffset(2026, 10, 8, 3, 0, 0, TimeSpan.Zero)));
            _metrics = new MeterCapture(_telemetry.Meter);
            _responseFactory.Setup(f => f.CreateOKResponse(It.IsAny<DrawdownBrakeDto>()))
                .Returns<DrawdownBrakeDto>(dto => new OkObjectResult(dto));
            _responseFactory.Setup(f => f.CreateOKResponse(It.IsAny<DrawdownBrakeRefreshDto>()))
                .Returns<DrawdownBrakeRefreshDto>(dto => new OkObjectResult(dto));
            _responseFactory.Setup(f => f.CreateErrorResponse(It.IsAny<ErrorCodes>(), It.IsAny<string>()))
                .Returns<ErrorCodes, string>((code, msg) => new ObjectResult(new { code, msg }) { StatusCode = (int)code / 100 % 1000 });

            _controller = new PortfolioController(
                _responseFactory.Object, new Mock<IPortfolioService>().Object, new Mock<IHoldingSearchService>().Object,
                new Mock<IHoldingPriceService>().Object, new Mock<IHoldingAiSummaryService>().Object, _currentUser.Object,
                new ServerTiming(), _brake.Object, _access.Object, _refresher.Object, _telemetry);
        }

        [Fact]
        public async Task EligibleUser_GetsTheSignal()
        {
            var dto = new DrawdownBrakeDto { State = BrakeState.OFFENSE, AsOfDate = new DateOnly(2026, 10, 8) };
            _currentUser.Setup(u => u.Id).Returns("u1");
            _access.Setup(a => a.IsEnabledForUserAsync("u1", It.IsAny<CancellationToken>())).ReturnsAsync(true);
            _brake.Setup(b => b.GetWithCacheStatusAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DrawdownBrakeLookup(dto, CacheHit: true));

            var result = await _controller.GetDrawdownBrake(CancellationToken.None);

            Assert.Same(dto, Assert.IsType<OkObjectResult>(result).Value);
        }

        [Fact]
        public async Task IneligibleUser_Gets403_AndNoData()
        {
            _currentUser.Setup(u => u.Id).Returns("u1");
            _access.Setup(a => a.IsEnabledForUserAsync("u1", It.IsAny<CancellationToken>())).ReturnsAsync(false);

            var result = await _controller.GetDrawdownBrake(CancellationToken.None);

            Assert.Equal(403, Assert.IsType<ObjectResult>(result).StatusCode);
            _responseFactory.Verify(f => f.CreateErrorResponse(ErrorCodes.DrawdownBrakeNotEnabled, It.IsAny<string>()), Times.Once);
            _brake.Verify(b => b.GetWithCacheStatusAsync(It.IsAny<CancellationToken>()), Times.Never);
            _brake.Verify(b => b.GetAsync(It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task NoUserId_IsRejectedBeforeAnythingIsLookedUp()
        {
            _currentUser.Setup(u => u.Id).Returns((string?)null);

            await Assert.ThrowsAsync<NotSignedInException>(() => _controller.GetDrawdownBrake(CancellationToken.None));

            _access.Verify(a => a.IsEnabledForUserAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
            _brake.Verify(b => b.GetWithCacheStatusAsync(It.IsAny<CancellationToken>()), Times.Never);
            // Nobody to count either: no view, no log line.
            Assert.Empty(_metrics.All);
            Assert.Empty(_log.Entries);
        }

        [Fact]
        public void TheEndpoint_RequiresLogin_AndCarriesNoUsageCost()
        {
            var action = typeof(PortfolioController).GetMethod(nameof(PortfolioController.GetDrawdownBrake))!;

            // [Authorize] on the controller is what turns an anonymous call into a 401, and nothing
            // on the action lifts it.
            Assert.NotEmpty(typeof(PortfolioController).GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true));
            Assert.Empty(action.GetCustomAttributes(typeof(AllowAnonymousAttribute), inherit: true));
            // No AI behind it, so nothing to meter.
            Assert.Empty(action.GetCustomAttributes(typeof(UsageLimitAttribute), inherit: true));
        }

        private sealed class FixedTime(DateTimeOffset now) : TimeProvider
        {
            public override DateTimeOffset GetUtcNow() => now;
        }

        [Fact]
        public void TheForbiddenErrorCode_MapsToHttp403()
        {
            // ResponseFactory derives the HTTP status from digits 3-5 of the code.
            Assert.Equal(403, (int)ErrorCodes.DrawdownBrakeNotEnabled / 100 % 1000);
        }

        // ── POST drawdown-brake/refresh ─────────────────────────────────────

        private void UserIsEligible(bool eligible)
        {
            _currentUser.Setup(u => u.Id).Returns("u1");
            _access.Setup(a => a.IsEnabledForUserAsync("u1", It.IsAny<CancellationToken>())).ReturnsAsync(eligible);
        }

        [Theory]
        [InlineData(DrawdownBrakeRefreshResult.Updated, null)]
        [InlineData(DrawdownBrakeRefreshResult.NoNewData, null)]
        [InlineData(DrawdownBrakeRefreshResult.SourceUnavailable, null)]
        [InlineData(DrawdownBrakeRefreshResult.Throttled, 42)]
        public async Task Refresh_EligibleUser_Gets200_WithTheResult_AndTheLatestSignal(DrawdownBrakeRefreshResult result, int? retryAfter)
        {
            var signal = new DrawdownBrakeDto { State = BrakeState.OFFENSE, AsOfDate = new DateOnly(2026, 10, 8) };
            UserIsEligible(true);
            _refresher.Setup(r => r.TryRefreshNowAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DrawdownBrakeRefreshOutcome(result, retryAfter));
            _brake.Setup(b => b.GetAfterEstimateRefreshAsync(It.IsAny<CancellationToken>())).ReturnsAsync(signal);

            var action = await _controller.RefreshDrawdownBrake(CancellationToken.None);

            // Always a plain 200 — Throttled included (429 is the usage limit's).
            var body = Assert.IsType<DrawdownBrakeRefreshDto>(Assert.IsType<OkObjectResult>(action).Value);
            Assert.Equal(result, body.Result);
            Assert.Equal(retryAfter, body.RetryAfterSeconds);
            Assert.Same(signal, body.Signal);
        }

        [Fact]
        public async Task Refresh_TheSignalIsReadAfterTheRefreshRan_NotBefore()
        {
            var order = new List<string>();
            UserIsEligible(true);
            _refresher.Setup(r => r.TryRefreshNowAsync(It.IsAny<CancellationToken>()))
                .Callback(() => order.Add("refresh"))
                .ReturnsAsync(new DrawdownBrakeRefreshOutcome(DrawdownBrakeRefreshResult.Updated));
            _brake.Setup(b => b.GetAfterEstimateRefreshAsync(It.IsAny<CancellationToken>()))
                .Callback(() => order.Add("signal"))
                .ReturnsAsync(new DrawdownBrakeDto());

            await _controller.RefreshDrawdownBrake(CancellationToken.None);

            Assert.Equal(["refresh", "signal"], order);
        }

        [Fact]
        public async Task Refresh_AsksForTheSignalThatRefreshesTheEstimate_NotThePlainOne()
        {
            UserIsEligible(true);
            _refresher.Setup(r => r.TryRefreshNowAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DrawdownBrakeRefreshOutcome(DrawdownBrakeRefreshResult.Throttled, 30));
            _brake.Setup(b => b.GetAfterEstimateRefreshAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new DrawdownBrakeDto());

            await _controller.RefreshDrawdownBrake(CancellationToken.None);

            // Even when the official refresh was throttled, the estimate gets its own (10 s) chance.
            _brake.Verify(b => b.GetAfterEstimateRefreshAsync(It.IsAny<CancellationToken>()), Times.Once);
            _brake.Verify(b => b.GetAsync(It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task AMisFailure_DoesNotChangeTheRefreshResult()
        {
            // The service answers a MIS failure with a signal that simply has no estimate; the result
            // stays what the official refresh said (SourceUnavailable means TWSE's index data).
            UserIsEligible(true);
            _refresher.Setup(r => r.TryRefreshNowAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DrawdownBrakeRefreshOutcome(DrawdownBrakeRefreshResult.Updated));
            _brake.Setup(b => b.GetAfterEstimateRefreshAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DrawdownBrakeDto { State = BrakeState.OFFENSE, Estimate = null });

            var action = await _controller.RefreshDrawdownBrake(CancellationToken.None);

            var body = Assert.IsType<DrawdownBrakeRefreshDto>(Assert.IsType<OkObjectResult>(action).Value);
            Assert.Equal(DrawdownBrakeRefreshResult.Updated, body.Result);
            Assert.Null(body.Signal.Estimate);
        }

        [Fact]
        public async Task Refresh_IneligibleUser_Gets403_AndNothingIsRefreshedOrRead()
        {
            UserIsEligible(false);

            var result = await _controller.RefreshDrawdownBrake(CancellationToken.None);

            Assert.Equal(403, Assert.IsType<ObjectResult>(result).StatusCode);
            _responseFactory.Verify(f => f.CreateErrorResponse(ErrorCodes.DrawdownBrakeNotEnabled, It.IsAny<string>()), Times.Once);
            _refresher.Verify(r => r.TryRefreshNowAsync(It.IsAny<CancellationToken>()), Times.Never);
            _brake.Verify(b => b.GetAfterEstimateRefreshAsync(It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task Refresh_NoUserId_IsRejectedBeforeAnythingIsLookedUp()
        {
            _currentUser.Setup(u => u.Id).Returns((string?)null);

            await Assert.ThrowsAsync<NotSignedInException>(() => _controller.RefreshDrawdownBrake(CancellationToken.None));

            _access.Verify(a => a.IsEnabledForUserAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
            _refresher.Verify(r => r.TryRefreshNowAsync(It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public void TheRefreshEndpoint_IsAPost_RequiresLogin_AndCarriesNoUsageCost()
        {
            var action = typeof(PortfolioController).GetMethod(nameof(PortfolioController.RefreshDrawdownBrake))!;

            var route = Assert.Single(action.GetCustomAttributes(typeof(HttpPostAttribute), inherit: true).Cast<HttpPostAttribute>());
            Assert.Equal("drawdown-brake/refresh", route.Template);
            Assert.NotEmpty(typeof(PortfolioController).GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true));
            Assert.Empty(action.GetCustomAttributes(typeof(AllowAnonymousAttribute), inherit: true));
            Assert.Empty(action.GetCustomAttributes(typeof(UsageLimitAttribute), inherit: true));
        }

        // ── telemetry (TASK-040) ────────────────────────────────────────────

        private const string GoogleId = "google-oauth-id-777";

        private void CallerIs(string userId, bool eligible)
        {
            _currentUser.Setup(u => u.Id).Returns(userId);
            _access.Setup(a => a.IsEnabledForUserAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(eligible);
        }

        private void SignalIs(DrawdownBrakeDto dto, bool cacheHit) =>
            _brake.Setup(b => b.GetWithCacheStatusAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DrawdownBrakeLookup(dto, cacheHit));

        [Theory]
        [InlineData(false, false, false, "ok", "miss")]
        [InlineData(false, false, true, "ok", "hit")]
        [InlineData(true, false, true, "initializing", "hit")]
        [InlineData(true, false, false, "initializing", "miss")]
        [InlineData(false, true, true, "stale", "hit")]
        [InlineData(false, true, false, "stale", "miss")]
        public async Task AView_IsCountedOnce_WithTheSignalsOutcomeAndTheCacheStatus(bool initializing, bool stale, bool cacheHit, string outcome, string cache)
        {
            CallerIs(GoogleId, eligible: true);
            SignalIs(new DrawdownBrakeDto { IsInitializing = initializing, IsStale = stale }, cacheHit);

            await _controller.GetDrawdownBrake(CancellationToken.None);

            var m = Assert.Single(_metrics.All);
            Assert.Equal("drawdownbrake.views", m.Instrument);
            Assert.Equal(1, m.Value);
            Assert.Equal(new Dictionary<string, string?> { ["outcome"] = outcome, ["cache"] = cache }, m.Tags);
        }

        [Fact]
        public async Task AnIneligibleView_IsCountedAsForbidden_WithNoCacheTag()
        {
            CallerIs(GoogleId, eligible: false);

            await _controller.GetDrawdownBrake(CancellationToken.None);

            var m = Assert.Single(_metrics.All);
            Assert.Equal("drawdownbrake.views", m.Instrument);
            Assert.Equal(new Dictionary<string, string?> { ["outcome"] = "forbidden" }, m.Tags);
        }

        [Fact]
        public async Task EveryView_LogsOneLine_WithAUserKeyAndNeverTheGoogleId()
        {
            CallerIs(GoogleId, eligible: true);
            SignalIs(new DrawdownBrakeDto(), cacheHit: false);

            await _controller.GetDrawdownBrake(CancellationToken.None);
            CallerIs(GoogleId, eligible: false);
            await _controller.GetDrawdownBrake(CancellationToken.None);

            Assert.Equal(2, _log.Entries.Count);
            Assert.All(_log.Entries, e =>
            {
                Assert.StartsWith("DrawdownBrake view ", e.Message);
                Assert.DoesNotContain(GoogleId, e.Message);
                Assert.Equal(_telemetry.UserKey(GoogleId), e.Properties["UserKey"]);
                Assert.All(e.Properties, p => Assert.DoesNotContain(GoogleId, p.Value?.ToString() ?? ""));
            });
            Assert.Equal(["ok", "forbidden"], _log.Entries.Select(e => (string)e.Properties["Outcome"]!));
            Assert.All(_metrics.All, m => Assert.All(m.Tags, t => Assert.DoesNotContain("777", t.Value ?? "")));
        }

        [Fact]
        public async Task TheSamePersonViewingTwiceOnOneDay_IsOneUserKey()
        {
            CallerIs(GoogleId, eligible: true);
            SignalIs(new DrawdownBrakeDto(), cacheHit: true);

            await _controller.GetDrawdownBrake(CancellationToken.None);
            await _controller.GetDrawdownBrake(CancellationToken.None);

            Assert.Single(_log.Entries.Select(e => e.Properties["UserKey"]).Distinct());
            Assert.Equal(2, _metrics.Of("drawdownbrake.views").Count);
        }

        [Theory]
        [InlineData(DrawdownBrakeRefreshResult.Updated, "updated")]
        [InlineData(DrawdownBrakeRefreshResult.NoNewData, "no_new_data")]
        [InlineData(DrawdownBrakeRefreshResult.SourceUnavailable, "source_unavailable")]
        [InlineData(DrawdownBrakeRefreshResult.Throttled, "throttled")]
        public async Task ARefresh_IsCountedUnderItsResult_AndIsNotAView(DrawdownBrakeRefreshResult result, string tag)
        {
            CallerIs(GoogleId, eligible: true);
            _refresher.Setup(r => r.TryRefreshNowAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DrawdownBrakeRefreshOutcome(result, result == DrawdownBrakeRefreshResult.Throttled ? 30 : null));
            _brake.Setup(b => b.GetAfterEstimateRefreshAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new DrawdownBrakeDto());

            await _controller.RefreshDrawdownBrake(CancellationToken.None);

            var m = Assert.Single(_metrics.All);
            Assert.Equal("drawdownbrake.refresh", m.Instrument);
            Assert.Equal(1, m.Value);
            Assert.Equal(new Dictionary<string, string?> { ["result"] = tag }, m.Tags);
            // Reading the signal for the response is not a view: no views counter, no log line.
            _brake.Verify(b => b.GetWithCacheStatusAsync(It.IsAny<CancellationToken>()), Times.Never);
            Assert.Empty(_log.Entries);
        }

        [Fact]
        public async Task AnIneligibleRefresh_CountsNothing()
        {
            CallerIs(GoogleId, eligible: false);

            await _controller.RefreshDrawdownBrake(CancellationToken.None);

            Assert.Empty(_metrics.All);
            Assert.Empty(_log.Entries);
        }

        // ── the JSON contract (ARCHITECTURE.md) ─────────────────────────────

        // The options Program.cs gives MVC: web defaults (camelCase) plus enums by name.
        private static readonly JsonSerializerOptions AppJson = new(JsonSerializerDefaults.Web)
        {
            Converters = { new JsonStringEnumConverter() },
        };

        [Fact]
        public void RefreshResponse_SerializesToTheDocumentedShape()
        {
            var dto = new DrawdownBrakeRefreshDto
            {
                Result = DrawdownBrakeRefreshResult.Throttled,
                RetryAfterSeconds = 42,
                Signal = new DrawdownBrakeDto
                {
                    State = BrakeState.DEFENSE,
                    LastCheckedAt = new DateTimeOffset(2026, 10, 8, 7, 5, 0, TimeSpan.Zero),
                    GeneratedAt = new DateTimeOffset(2026, 10, 8, 7, 6, 0, TimeSpan.Zero),
                },
            };

            using var json = JsonDocument.Parse(JsonSerializer.Serialize(dto, AppJson));
            var root = json.RootElement;

            Assert.Equal("Throttled", root.GetProperty("result").GetString());
            Assert.Equal(42, root.GetProperty("retryAfterSeconds").GetInt32());
            var signal = root.GetProperty("signal");
            Assert.Equal("DEFENSE", signal.GetProperty("state").GetString());
            Assert.Equal(new DateTimeOffset(2026, 10, 8, 7, 5, 0, TimeSpan.Zero), signal.GetProperty("lastCheckedAt").GetDateTimeOffset());
            Assert.Equal(new DateTimeOffset(2026, 10, 8, 7, 6, 0, TimeSpan.Zero), signal.GetProperty("generatedAt").GetDateTimeOffset());
        }

        [Fact]
        public void RefreshResponse_NotThrottled_HasANullRetryAfter_AndASignalWithNoLastChecked()
        {
            var dto = new DrawdownBrakeRefreshDto { Result = DrawdownBrakeRefreshResult.NoNewData, Signal = new DrawdownBrakeDto() };

            using var json = JsonDocument.Parse(JsonSerializer.Serialize(dto, AppJson));
            var root = json.RootElement;

            Assert.Equal("NoNewData", root.GetProperty("result").GetString());
            Assert.Equal(JsonValueKind.Null, root.GetProperty("retryAfterSeconds").ValueKind);
            Assert.Equal(JsonValueKind.Null, root.GetProperty("signal").GetProperty("lastCheckedAt").ValueKind);
        }

        [Fact]
        public void TheEstimate_SerializesToTheDocumentedShape()
        {
            var dto = new DrawdownBrakeDto
            {
                State = BrakeState.OFFENSE,
                EstimateCorrected = true,
                Estimate = new DrawdownBrakeEstimateDto
                {
                    AsOfTime = new DateTimeOffset(2026, 10, 8, 13, 25, 10, TimeSpan.FromHours(8)),
                    EstimatedClose = 41234.5678m,
                    State = BrakeState.DEFENSE,
                    PrevState = BrakeState.OFFENSE,
                    DrawdownPct = -8.8m,
                },
            };

            using var json = JsonDocument.Parse(JsonSerializer.Serialize(dto, AppJson));
            var root = json.RootElement;
            var estimate = root.GetProperty("estimate");

            Assert.True(root.GetProperty("estimateCorrected").GetBoolean());
            Assert.Equal(["asOfTime", "estimatedClose", "state", "prevState", "drawdownPct"],
                estimate.EnumerateObject().Select(p => p.Name));
            Assert.Equal(new DateTimeOffset(2026, 10, 8, 13, 25, 10, TimeSpan.FromHours(8)), estimate.GetProperty("asOfTime").GetDateTimeOffset());
            Assert.Equal(41234.5678m, estimate.GetProperty("estimatedClose").GetDecimal());
            Assert.Equal("DEFENSE", estimate.GetProperty("state").GetString());
            Assert.Equal("OFFENSE", estimate.GetProperty("prevState").GetString());
            Assert.Equal(-8.8m, estimate.GetProperty("drawdownPct").GetDecimal());
        }

        [Fact]
        public void WithoutAnEstimate_TheFieldIsNull_AndNotCorrected()
        {
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(new DrawdownBrakeDto(), AppJson));

            Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("estimate").ValueKind);
            Assert.False(json.RootElement.GetProperty("estimateCorrected").GetBoolean());
        }

        [Fact]
        public void TheResultNames_AreTheDocumentedOnes()
        {
            Assert.Equal(["Updated", "NoNewData", "SourceUnavailable", "Throttled"], Enum.GetNames<DrawdownBrakeRefreshResult>());
        }
    }
}
