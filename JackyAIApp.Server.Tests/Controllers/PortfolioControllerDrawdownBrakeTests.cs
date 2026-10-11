using JackyAIApp.Server.Common;
using JackyAIApp.Server.Controllers;
using JackyAIApp.Server.DTO.Portfolio;
using JackyAIApp.Server.Services;
using JackyAIApp.Server.Services.Portfolio;
using JackyAIApp.Server.Services.Portfolio.DrawdownBrake;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace JackyAIApp.Server.Tests.Controllers
{
    /// <summary>GET api/portfolio/drawdown-brake: who may call it, and what it hands back.</summary>
    public class PortfolioControllerDrawdownBrakeTests
    {
        private readonly Mock<IMyResponseFactory> _responseFactory = new();
        private readonly Mock<IUserService> _userService = new();
        private readonly Mock<IDrawdownBrakeService> _brake = new();
        private readonly Mock<IDrawdownBrakeAccess> _access = new();
        private readonly PortfolioController _controller;

        public PortfolioControllerDrawdownBrakeTests()
        {
            _responseFactory.Setup(f => f.CreateOKResponse(It.IsAny<DrawdownBrakeDto>()))
                .Returns<DrawdownBrakeDto>(dto => new OkObjectResult(dto));
            _responseFactory.Setup(f => f.CreateErrorResponse(It.IsAny<ErrorCodes>(), It.IsAny<string>()))
                .Returns<ErrorCodes, string>((code, msg) => new ObjectResult(new { code, msg }) { StatusCode = (int)code / 100 % 1000 });

            _controller = new PortfolioController(
                _responseFactory.Object, new Mock<IPortfolioService>().Object, _userService.Object,
                new ServerTiming(), _brake.Object, _access.Object);
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
    }
}
