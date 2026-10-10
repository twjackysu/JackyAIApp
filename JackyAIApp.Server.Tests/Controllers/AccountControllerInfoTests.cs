using JackyAIApp.Server.Common;
using JackyAIApp.Server.Controllers;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.Data.Models.SQL;
using JackyAIApp.Server.Services;
using JackyAIApp.Server.Services.Portfolio.DrawdownBrake;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace JackyAIApp.Server.Tests.Controllers
{
    /// <summary>GET api/account/info tells the client whether to show 回落煞車 (drawdownBrakeEnabled).</summary>
    public class AccountControllerInfoTests : IDisposable
    {
        private readonly AzureSQLDBContext _db = new(new DbContextOptionsBuilder<AzureSQLDBContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
        private readonly Mock<IUserService> _userService = new();
        private readonly Mock<IDrawdownBrakeAccess> _access = new();
        private readonly AccountController _controller;

        public AccountControllerInfoTests()
        {
            var responseFactory = new Mock<IMyResponseFactory>();
            responseFactory.Setup(f => f.CreateOKResponse(It.IsAny<User>()))
                .Returns<User>(u => new OkObjectResult(u));
            _controller = new AccountController(
                NullLogger<AccountController>.Instance, _db, _userService.Object, responseFactory.Object, _access.Object);
        }

        public void Dispose() => _db.Dispose();

        private async Task<User> InfoForAsync(bool? isAdmin, bool enabled)
        {
            _db.Users.Add(new User { Id = "u1", LastUpdated = DateTime.UtcNow, IsAdmin = isAdmin });
            await _db.SaveChangesAsync();
            _userService.Setup(u => u.GetUserId()).Returns("u1");
            _access.Setup(a => a.IsEnabledFor(isAdmin)).Returns(enabled);

            var result = await _controller.GetUserInfo();
            return Assert.IsType<User>(Assert.IsType<OkObjectResult>(result).Value);
        }

        [Fact]
        public async Task Info_FeatureOpenToTheUser_SaysEnabled() =>
            Assert.True((await InfoForAsync(isAdmin: true, enabled: true)).DrawdownBrakeEnabled);

        [Fact]
        public async Task Info_FeatureClosedToTheUser_SaysDisabled() =>
            Assert.False((await InfoForAsync(isAdmin: null, enabled: false)).DrawdownBrakeEnabled);

        [Fact]
        public void TheFlag_IsComputedNotStored() =>
            Assert.DoesNotContain(
                _db.Model.FindEntityType(typeof(User))!.GetProperties(),
                p => p.Name == nameof(User.DrawdownBrakeEnabled));
    }
}
