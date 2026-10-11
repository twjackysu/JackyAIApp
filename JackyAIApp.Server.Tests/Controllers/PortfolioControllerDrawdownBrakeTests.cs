using JackyAIApp.Server.Common;
using JackyAIApp.Server.Controllers;
using JackyAIApp.Server.DTO.Portfolio;
using JackyAIApp.Server.Services;
using JackyAIApp.Server.Services.Portfolio;
using JackyAIApp.Server.Services.Portfolio.DrawdownBrake;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Moq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace JackyAIApp.Server.Tests.Controllers
{
    /// <summary>GET api/portfolio/drawdown-brake and POST …/refresh: who may call them, and what they hand back.</summary>
    public class PortfolioControllerDrawdownBrakeTests
    {
        private readonly Mock<IMyResponseFactory> _responseFactory = new();
        private readonly Mock<IUserService> _userService = new();
        private readonly Mock<IDrawdownBrakeService> _brake = new();
        private readonly Mock<IDrawdownBrakeAccess> _access = new();
        private readonly Mock<ITaiexTotalReturnRefresher> _refresher = new();
        private readonly PortfolioController _controller;

        public PortfolioControllerDrawdownBrakeTests()
        {
            _responseFactory.Setup(f => f.CreateOKResponse(It.IsAny<DrawdownBrakeDto>()))
                .Returns<DrawdownBrakeDto>(dto => new OkObjectResult(dto));
            _responseFactory.Setup(f => f.CreateOKResponse(It.IsAny<DrawdownBrakeRefreshDto>()))
                .Returns<DrawdownBrakeRefreshDto>(dto => new OkObjectResult(dto));
            _responseFactory.Setup(f => f.CreateErrorResponse(It.IsAny<ErrorCodes>(), It.IsAny<string>()))
                .Returns<ErrorCodes, string>((code, msg) => new ObjectResult(new { code, msg }) { StatusCode = (int)code / 100 % 1000 });

            _controller = new PortfolioController(
                _responseFactory.Object, new Mock<IPortfolioService>().Object, _userService.Object,
                new ServerTiming(), _brake.Object, _access.Object, _refresher.Object);
        }

        [Fact]
        public async Task EligibleUser_GetsTheSignal()
        {
            var dto = new DrawdownBrakeDto { State = BrakeState.OFFENSE, AsOfDate = new DateOnly(2026, 10, 8) };
            _userService.Setup(u => u.GetUserId()).Returns("u1");
            _access.Setup(a => a.IsEnabledForUserAsync("u1", It.IsAny<CancellationToken>())).ReturnsAsync(true);
            _brake.Setup(b => b.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(dto);

            var result = await _controller.GetDrawdownBrake(CancellationToken.None);

            Assert.Same(dto, Assert.IsType<OkObjectResult>(result).Value);
        }

        [Fact]
        public async Task IneligibleUser_Gets403_AndNoData()
        {
            _userService.Setup(u => u.GetUserId()).Returns("u1");
            _access.Setup(a => a.IsEnabledForUserAsync("u1", It.IsAny<CancellationToken>())).ReturnsAsync(false);

            var result = await _controller.GetDrawdownBrake(CancellationToken.None);

            Assert.Equal(403, Assert.IsType<ObjectResult>(result).StatusCode);
            _responseFactory.Verify(f => f.CreateErrorResponse(ErrorCodes.DrawdownBrakeNotEnabled, It.IsAny<string>()), Times.Once);
            _brake.Verify(b => b.GetAsync(It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task NoUserId_IsRejectedBeforeAnythingIsLookedUp()
        {
            _userService.Setup(u => u.GetUserId()).Returns((string?)null);

            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _controller.GetDrawdownBrake(CancellationToken.None));

            _access.Verify(a => a.IsEnabledForUserAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
            _brake.Verify(b => b.GetAsync(It.IsAny<CancellationToken>()), Times.Never);
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

        [Fact]
        public void TheForbiddenErrorCode_MapsToHttp403()
        {
            // ResponseFactory derives the HTTP status from digits 3-5 of the code.
            Assert.Equal(403, (int)ErrorCodes.DrawdownBrakeNotEnabled / 100 % 1000);
        }

        // ── POST drawdown-brake/refresh ─────────────────────────────────────

        private void UserIsEligible(bool eligible)
        {
            _userService.Setup(u => u.GetUserId()).Returns("u1");
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
            _brake.Setup(b => b.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(signal);

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
            _brake.Setup(b => b.GetAsync(It.IsAny<CancellationToken>()))
                .Callback(() => order.Add("signal"))
                .ReturnsAsync(new DrawdownBrakeDto());

            await _controller.RefreshDrawdownBrake(CancellationToken.None);

            Assert.Equal(["refresh", "signal"], order);
        }

        [Fact]
        public async Task Refresh_IneligibleUser_Gets403_AndNothingIsRefreshedOrRead()
        {
            UserIsEligible(false);

            var result = await _controller.RefreshDrawdownBrake(CancellationToken.None);

            Assert.Equal(403, Assert.IsType<ObjectResult>(result).StatusCode);
            _responseFactory.Verify(f => f.CreateErrorResponse(ErrorCodes.DrawdownBrakeNotEnabled, It.IsAny<string>()), Times.Once);
            _refresher.Verify(r => r.TryRefreshNowAsync(It.IsAny<CancellationToken>()), Times.Never);
            _brake.Verify(b => b.GetAsync(It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task Refresh_NoUserId_IsRejectedBeforeAnythingIsLookedUp()
        {
            _userService.Setup(u => u.GetUserId()).Returns((string?)null);

            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _controller.RefreshDrawdownBrake(CancellationToken.None));

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
        public void TheResultNames_AreTheDocumentedOnes()
        {
            Assert.Equal(["Updated", "NoNewData", "SourceUnavailable", "Throttled"], Enum.GetNames<DrawdownBrakeRefreshResult>());
        }
    }
}
