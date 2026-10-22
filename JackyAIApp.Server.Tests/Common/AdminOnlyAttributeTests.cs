using JackyAIApp.Server.Common;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.Data.Models.SQL;
using JackyAIApp.Server.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace JackyAIApp.Server.Tests.Common
{
    /// <summary>
    /// [AdminOnly] with the real <see cref="AdminAccess"/>: the action runs only for a user whose
    /// row says IsAdmin = true; anyone else gets 403 and the action never runs.
    /// </summary>
    public class AdminOnlyAttributeTests : IDisposable
    {
        private readonly AzureSQLDBContext _db = new(new DbContextOptionsBuilder<AzureSQLDBContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        private readonly Mock<IMyResponseFactory> _responseFactory = new();
        private readonly IActionResult _forbidden = new StatusCodeResult(403);

        public AdminOnlyAttributeTests()
        {
            _db.Users.AddRange(
                new User { Id = "admin", LastUpdated = DateTime.UtcNow, IsAdmin = true },
                new User { Id = "member", LastUpdated = DateTime.UtcNow, IsAdmin = false },
                new User { Id = "unset", LastUpdated = DateTime.UtcNow, IsAdmin = null });
            _db.SaveChanges();
            _responseFactory.Setup(f => f.CreateErrorResponse(ErrorCodes.Forbidden, "Admin access required.")).Returns(_forbidden);
        }

        public void Dispose() => _db.Dispose();

        /// <summary>Runs the filter for this user; whether the action ran, and the result it set.</summary>
        private async Task<(bool ActionRan, IActionResult? Result)> RunAsAsync(string? userId)
        {
            var currentUser = new Mock<ICurrentUser>();
            currentUser.Setup(u => u.Id).Returns(userId);
            var services = new ServiceCollection()
                .AddSingleton<IAdminAccess>(new AdminAccess(_db, currentUser.Object))
                .AddSingleton(_responseFactory.Object)
                .BuildServiceProvider();
            var context = new ActionExecutingContext(
                new ActionContext(new DefaultHttpContext { RequestServices = services }, new RouteData(), new ActionDescriptor()),
                new List<IFilterMetadata>(),
                new Dictionary<string, object?>(),
                controller: new object());

            var ran = false;
            await new AdminOnlyAttribute().OnActionExecutionAsync(context, () =>
            {
                ran = true;
                return Task.FromResult<ActionExecutedContext>(null!);
            });
            return (ran, context.Result);
        }

        [Fact]
        public async Task AnAdmin_GetsThrough()
        {
            var (ran, result) = await RunAsAsync("admin");

            Assert.True(ran);
            Assert.Null(result);
        }

        [Theory]
        [InlineData("member")]
        [InlineData("unset")]
        [InlineData("no-row")]
        [InlineData(null)]
        public async Task AnyoneElse_IsForbidden_AndTheActionNeverRuns(string? userId)
        {
            var (ran, result) = await RunAsAsync(userId);

            Assert.False(ran);
            Assert.Same(_forbidden, result);
        }
    }
}
